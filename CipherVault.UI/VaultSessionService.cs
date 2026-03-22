using CipherVault.Core.Crypto;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using System.Text.Json;

namespace CipherVault.UI;

/// <summary>
/// Central session manager. Holds the derived key in memory while unlocked.
/// Provides encrypt/decrypt helpers for vault entries.
/// SECURITY: Key is zeroed when locked or app exits.
/// </summary>
public class VaultSessionService : IDisposable
{
    private byte[]? _key;
    private bool _isUnlocked;
    private readonly CryptoService _crypto;
    private readonly KeyDerivationService _kdf;
    private readonly IVaultRepository _vaultRepo;
    private readonly SecureClipboardService _clipboard;
    private bool _disposed;

    public bool IsUnlocked => _isUnlocked;
    public event EventHandler? Locked;
    public event EventHandler? Unlocked;

    public VaultSessionService(
        CryptoService crypto,
        KeyDerivationService kdf,
        IVaultRepository vaultRepo,
        SecureClipboardService clipboard)
    {
        _crypto = crypto;
        _kdf = kdf;
        _vaultRepo = vaultRepo;
        _clipboard = clipboard;
    }

    /// <summary>
    /// Creates a new vault with the given master password.
    /// Stores encrypted canary in DB to verify future unlocks.
    /// </summary>
    public async Task CreateVaultAsync(string masterPassword, int memoryMB = 256, int iterations = 3)
    {
        byte[] salt = _kdf.GenerateSalt();
        int parallelism = Math.Min(Environment.ProcessorCount, 4);
        byte[] key = _kdf.DeriveKey(masterPassword, salt, memoryMB, iterations, parallelism);

        // Encrypt a known canary value
        byte[] canaryBlob = _crypto.EncryptString(key, "CIPHERVAULT_CANARY_OK");

        var meta = new VaultMeta
        {
            Salt = salt,
            ArgonMemoryMB = memoryMB,
            ArgonIterations = iterations,
            ArgonParallelism = parallelism,
            CanaryBlob = canaryBlob,
            CreatedAt = DateTime.UtcNow
        };

        await _vaultRepo.SaveVaultMetaAsync(meta);

        SetKey(key);
        Unlocked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Attempts to unlock the vault. Returns false if wrong password.
    /// </summary>
    public async Task<bool> TryUnlockAsync(string masterPassword)
    {
        var meta = await _vaultRepo.GetVaultMetaAsync();
        if (meta == null) throw new InvalidOperationException("No vault found. Create a vault first.");

        byte[] key = _kdf.DeriveKey(masterPassword, meta.Salt, meta.ArgonMemoryMB, meta.ArgonIterations, meta.ArgonParallelism);
        return TryUnlockWithValidatedKey(key, meta);
    }

    /// <summary>
    /// Attempts to unlock the vault using an already-derived key.
    /// Used by Windows Hello quick unlock.
    /// </summary>
    public async Task<bool> TryUnlockWithKeyAsync(byte[] keyCandidate)
    {
        if (keyCandidate == null || keyCandidate.Length == 0)
            return false;

        var meta = await _vaultRepo.GetVaultMetaAsync();
        if (meta == null) return false;

        byte[] key = keyCandidate.ToArray();
        return TryUnlockWithValidatedKey(key, meta);
    }

    private bool TryUnlockWithValidatedKey(byte[] key, VaultMeta meta)
    {
        try
        {
            string canary = _crypto.DecryptString(key, meta.CanaryBlob);
            if (canary != "CIPHERVAULT_CANARY_OK")
            {
                Array.Clear(key, 0, key.Length);
                return false;
            }
        }
        catch
        {
            Array.Clear(key, 0, key.Length);
            return false;
        }

        SetKey(key);
        Unlocked?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Locks the vault, zeroing the key in memory.</summary>
    public void Lock()
    {
        WipeKey();
        _clipboard.ClearNow();
        _isUnlocked = false;
        Locked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Encrypts a plain entry to a DB record.</summary>
    public VaultEntryRecord EncryptEntry(VaultEntryPlain plain)
    {
        EnsureUnlocked();

        var payload = new VaultEntrySecretPayload
        {
            Username = plain.Username,
            Password = plain.Password,
            Notes = plain.Notes,
            Url = plain.Url,
            Tags = plain.Tags,
            TotpSecret = plain.TotpSecret,
            PasswordHistory = NormalizeHistory(plain.PasswordHistory),
            PasswordReminderDays = Math.Clamp(plain.PasswordReminderDays, 0, 3650),
            PasswordLastChangedUtc = NormalizeUtcTimestamp(plain.PasswordLastChangedUtc)
        };

        string json = JsonSerializer.Serialize(payload);
        byte[] blob = _crypto.EncryptString(_key!, json);

        return new VaultEntryRecord
        {
            Id = plain.Id,
            Title = plain.Title,
            EncryptedBlob = blob,
            Favorite = plain.Favorite,
            FolderId = plain.FolderId,
            IsDeleted = plain.IsDeleted,
            DeletedAtUtc = plain.DeletedAtUtc,
            CreatedAt = plain.CreatedAt == default ? DateTime.UtcNow : plain.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Decrypts a DB record to a plain entry.</summary>
    public VaultEntryPlain DecryptEntry(VaultEntryRecord record)
    {
        EnsureUnlocked();
        string json = _crypto.DecryptString(_key!, record.EncryptedBlob);

        VaultEntrySecretPayload? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<VaultEntrySecretPayload>(json);
        }
        catch
        {
            // Fallback below.
        }

        if (payload != null)
        {
            return new VaultEntryPlain
            {
                Id = record.Id,
                Title = record.Title,
                Username = payload.Username ?? string.Empty,
                Password = payload.Password ?? string.Empty,
                Notes = payload.Notes ?? string.Empty,
                Url = payload.Url ?? string.Empty,
                Tags = payload.Tags ?? string.Empty,
                TotpSecret = payload.TotpSecret ?? string.Empty,
                PasswordHistory = NormalizeHistory(payload.PasswordHistory),
                PasswordReminderDays = Math.Clamp(payload.PasswordReminderDays, 0, 3650),
                PasswordLastChangedUtc = NormalizeUtcTimestamp(payload.PasswordLastChangedUtc),
                Favorite = record.Favorite,
                FolderId = record.FolderId,
                IsDeleted = record.IsDeleted,
                DeletedAtUtc = record.DeletedAtUtc,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            };
        }

        // Compatibility path for unexpected payload formats.
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        return new VaultEntryPlain
        {
            Id = record.Id,
            Title = record.Title,
            Username = data.GetValueOrDefault("Username", string.Empty),
            Password = data.GetValueOrDefault("Password", string.Empty),
            Notes = data.GetValueOrDefault("Notes", string.Empty),
            Url = data.GetValueOrDefault("Url", string.Empty),
            Tags = data.GetValueOrDefault("Tags", string.Empty),
            TotpSecret = data.GetValueOrDefault("TotpSecret", string.Empty),
            PasswordHistory = new List<PasswordHistoryItem>(),
            PasswordReminderDays = 0,
            PasswordLastChangedUtc = null,
            Favorite = record.Favorite,
            FolderId = record.FolderId,
            IsDeleted = record.IsDeleted,
            DeletedAtUtc = record.DeletedAtUtc,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }

    public byte[] GetKey()
    {
        EnsureUnlocked();
        return _key!;
    }

    private void SetKey(byte[] key)
    {
        WipeKey();
        _key = key;
        _isUnlocked = true;
    }

    private void WipeKey()
    {
        if (_key != null)
        {
            Array.Clear(_key, 0, _key.Length);
            _key = null;
        }
    }

    private void EnsureUnlocked()
    {
        if (!_isUnlocked || _key == null)
            throw new InvalidOperationException("Vault is locked. Unlock before accessing entries.");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            WipeKey();
            _disposed = true;
        }
    }

    private static List<PasswordHistoryItem> NormalizeHistory(IEnumerable<PasswordHistoryItem>? history)
    {
        var normalized = new List<PasswordHistoryItem>();
        if (history == null) return normalized;

        foreach (var item in history)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Password))
                continue;

            normalized.Add(new PasswordHistoryItem
            {
                Password = item.Password,
                ChangedAtUtc = item.ChangedAtUtc == default ? DateTime.UtcNow : item.ChangedAtUtc
            });
        }

        return normalized;
    }

    private static DateTime? NormalizeUtcTimestamp(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
            return null;

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private sealed class VaultEntrySecretPayload
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Notes { get; set; }
        public string? Url { get; set; }
        public string? Tags { get; set; }
        public string? TotpSecret { get; set; }
        public List<PasswordHistoryItem>? PasswordHistory { get; set; }
        public int PasswordReminderDays { get; set; }
        public DateTime? PasswordLastChangedUtc { get; set; }
    }
}

