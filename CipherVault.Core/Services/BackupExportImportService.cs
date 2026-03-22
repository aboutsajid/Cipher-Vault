using CipherVault.Core.Crypto;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using Microsoft.VisualBasic.FileIO;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CipherVault.Core.Services;

/// <summary>
/// Handles encrypted export/import of vault data.
/// Export format: Argon2id-derived key + AES-256-GCM encrypted JSON.
/// File header: [magic(8)][salt(16)][argon params(12)][encrypted payload]
/// </summary>
public class BackupExportImportService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CPHRBAK1");
    private const int MagicLength = 8;
    private const int SaltLength = KeyDerivationService.SaltLengthBytes;
    private const int ArgonHeaderLength = sizeof(int) * 3;
    private const int HeaderLength = MagicLength + SaltLength + ArgonHeaderLength;
    private const int MinEncryptedPayloadLength = 28; // nonce(12) + tag(16)
    private const long MaxBackupFileBytes = 64L * 1024 * 1024;
    private const int MinMemoryMb = 64;
    private const int MaxMemoryMb = 4096;
    private const int MinIterations = 1;
    private const int MaxIterations = 12;
    private const int MinParallelism = 1;
    private const int MaxParallelism = 64;

    private readonly KeyDerivationService _kdf;
    private readonly CryptoService _crypto;

    public BackupExportImportService(KeyDerivationService kdf, CryptoService crypto)
    {
        _kdf = kdf;
        _crypto = crypto;
    }

    public async Task ExportAsync(
        string filePath,
        string exportPassword,
        List<VaultEntryPlain> entries,
        List<Folder> folders)
    {
        byte[] salt = _kdf.GenerateSalt();
        byte[] key = _kdf.DeriveKey(exportPassword, salt);
        try
        {
            var payload = new BackupPayload
            {
                Entries = entries,
                Folders = folders,
                ExportedAt = DateTime.UtcNow
            };

            string json = JsonSerializer.Serialize(payload);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = _crypto.Encrypt(key, jsonBytes);
            Array.Clear(jsonBytes, 0, jsonBytes.Length);

            // Write: magic + salt + memory(4) + iterations(4) + parallelism(4) + encrypted
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await fs.WriteAsync(Magic);
            await fs.WriteAsync(salt);
            await fs.WriteAsync(BitConverter.GetBytes(256)); // memoryMB
            await fs.WriteAsync(BitConverter.GetBytes(3));   // iterations
            await fs.WriteAsync(BitConverter.GetBytes(Math.Min(Environment.ProcessorCount, 4)));
            await fs.WriteAsync(encrypted);
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }
    }

    public async Task<BackupPayload> ImportAsync(string filePath, string exportPassword)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Backup file not found.", filePath);
        if (fileInfo.Length > MaxBackupFileBytes)
            throw new InvalidDataException($"Backup file is too large (max {MaxBackupFileBytes / (1024 * 1024)} MB).");
        if (fileInfo.Length < HeaderLength + MinEncryptedPayloadLength)
            throw new InvalidDataException("Backup file is truncated or invalid.");

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
        int offset = 0;

        EnsureAvailable(fileBytes, offset, MagicLength, "Backup file is truncated (missing magic header).");
        byte[] magic = fileBytes[offset..(offset + MagicLength)];
        offset += MagicLength;

        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Not a valid CipherVault backup file.");

        EnsureAvailable(fileBytes, offset, SaltLength, "Backup file is truncated (missing salt).");
        byte[] salt = fileBytes[offset..(offset + SaltLength)];
        offset += SaltLength;

        EnsureAvailable(fileBytes, offset, sizeof(int), "Backup file is truncated (missing memory parameter).");
        int memoryMB = BitConverter.ToInt32(fileBytes, offset);
        offset += sizeof(int);

        EnsureAvailable(fileBytes, offset, sizeof(int), "Backup file is truncated (missing iterations parameter).");
        int iterations = BitConverter.ToInt32(fileBytes, offset);
        offset += sizeof(int);

        EnsureAvailable(fileBytes, offset, sizeof(int), "Backup file is truncated (missing parallelism parameter).");
        int parallelism = BitConverter.ToInt32(fileBytes, offset);
        offset += sizeof(int);

        ValidateArgonParameters(memoryMB, iterations, parallelism);

        int encryptedLength = fileBytes.Length - offset;
        if (encryptedLength < MinEncryptedPayloadLength)
            throw new InvalidDataException("Backup payload is truncated.");

        byte[] encrypted = fileBytes[offset..];

        byte[] key = _kdf.DeriveKey(exportPassword, salt, memoryMB, iterations, parallelism);
        try
        {
            byte[] jsonBytes = _crypto.Decrypt(key, encrypted);
            try
            {
                string json = Encoding.UTF8.GetString(jsonBytes);
                var payload = JsonSerializer.Deserialize<BackupPayload>(json)
                    ?? throw new InvalidDataException("Backup payload is null.");

                payload.Entries ??= new List<VaultEntryPlain>();
                payload.Folders ??= new List<Folder>();
                return payload;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Backup payload JSON is invalid.", ex);
            }
            finally
            {
                Array.Clear(jsonBytes, 0, jsonBytes.Length);
            }
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }
    }

    /// <summary>Parses a Bitwarden/KeePass-style CSV. Warns caller to delete file after use.</summary>
    public List<VaultEntryPlain> ImportCsv(string csvContent)
    {
        var entries = new List<VaultEntryPlain>();

        using var reader = new StringReader(csvContent);
        using var parser = new TextFieldParser(reader)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        if (parser.EndOfData)
            return entries;

        string[] headers = parser.ReadFields() ?? Array.Empty<string>();
        if (headers.Length == 0)
            return entries;

        try
        {
            while (!parser.EndOfData)
            {
                string[] cols = parser.ReadFields() ?? Array.Empty<string>();
                if (cols.Length == 0)
                    continue;

                var entry = new VaultEntryPlain { CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

                for (int h = 0; h < headers.Length && h < cols.Length; h++)
                {
                    switch (headers[h].Trim().ToLowerInvariant())
                    {
                        case "name":
                        case "title":
                            entry.Title = cols[h];
                            break;
                        case "username":
                        case "login_username":
                            entry.Username = cols[h];
                            break;
                        case "password":
                        case "login_password":
                            entry.Password = cols[h];
                            break;
                        case "url":
                        case "login_uri":
                            entry.Url = cols[h];
                            break;
                        case "notes":
                            entry.Notes = cols[h];
                            break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(entry.Title))
                    entries.Add(entry);
            }
        }
        catch (MalformedLineException ex)
        {
            throw new InvalidDataException($"Invalid CSV format near line {ex.LineNumber}.", ex);
        }

        return entries;
    }

    private static void EnsureAvailable(byte[] bytes, int offset, int length, string message)
    {
        if (offset < 0 || length < 0 || offset + length > bytes.Length)
            throw new InvalidDataException(message);
    }

    private static void ValidateArgonParameters(int memoryMB, int iterations, int parallelism)
    {
        if (memoryMB < MinMemoryMb || memoryMB > MaxMemoryMb)
            throw new InvalidDataException($"Backup file contains invalid Argon2 memory setting ({memoryMB} MB).");
        if (iterations < MinIterations || iterations > MaxIterations)
            throw new InvalidDataException($"Backup file contains invalid Argon2 iterations setting ({iterations}).");
        if (parallelism < MinParallelism || parallelism > MaxParallelism)
            throw new InvalidDataException($"Backup file contains invalid Argon2 parallelism setting ({parallelism}).");
    }
}

public class BackupPayload
{
    public List<VaultEntryPlain> Entries { get; set; } = new();
    public List<Folder> Folders { get; set; } = new();
    public DateTime ExportedAt { get; set; }
}
