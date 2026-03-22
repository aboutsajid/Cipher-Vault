using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CipherVault.Core.Crypto;

/// <summary>
/// Provides AES-256-GCM authenticated encryption and decryption.
/// SECURITY: Each encryption uses a fresh random nonce. Authentication tag is verified on decrypt.
/// Format: [12-byte nonce][16-byte tag][ciphertext]
/// </summary>
public class CryptoService
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Encrypts plaintext bytes using AES-256-GCM with a fresh random nonce.
    /// </summary>
    /// <param name="key">32-byte AES key.</param>
    /// <param name="plaintext">Data to encrypt.</param>
    /// <returns>Combined blob: nonce + tag + ciphertext.</returns>
    public byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        ValidateKey(key);
        if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));

        byte[] nonce = SecureRandom.GetBytes(NonceSizeBytes);
        byte[] tag = new byte[TagSizeBytes];
        byte[] ciphertext = new byte[plaintext.Length];

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Pack: nonce(12) + tag(16) + ciphertext
        byte[] result = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

        Array.Clear(nonce, 0, nonce.Length);
        Array.Clear(tag, 0, tag.Length);
        Array.Clear(ciphertext, 0, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypts an encrypted blob. Throws <see cref="AuthenticationTagMismatchException"/> if tampered.
    /// </summary>
    /// <param name="key">32-byte AES key.</param>
    /// <param name="encryptedBlob">Combined blob from Encrypt().</param>
    /// <returns>Decrypted plaintext.</returns>
    public byte[] Decrypt(byte[] key, byte[] encryptedBlob)
    {
        ValidateKey(key);
        if (encryptedBlob is null || encryptedBlob.Length < NonceSizeBytes + TagSizeBytes)
            throw new ArgumentException("Invalid encrypted blob.", nameof(encryptedBlob));

        byte[] nonce = new byte[NonceSizeBytes];
        byte[] tag = new byte[TagSizeBytes];
        int ciphertextLength = encryptedBlob.Length - NonceSizeBytes - TagSizeBytes;
        byte[] ciphertext = new byte[ciphertextLength];
        byte[] plaintext = new byte[ciphertextLength];

        Buffer.BlockCopy(encryptedBlob, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(encryptedBlob, NonceSizeBytes, tag, 0, TagSizeBytes);
        Buffer.BlockCopy(encryptedBlob, NonceSizeBytes + TagSizeBytes, ciphertext, 0, ciphertextLength);

        try
        {
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            Array.Clear(plaintext, 0, plaintext.Length);
            throw;
        }
        finally
        {
            Array.Clear(nonce, 0, nonce.Length);
            Array.Clear(tag, 0, tag.Length);
            Array.Clear(ciphertext, 0, ciphertext.Length);
        }
    }

    /// <summary>Encrypts a UTF-8 string.</summary>
    public byte[] EncryptString(byte[] key, string plaintext)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
        try { return Encrypt(key, bytes); }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }

    /// <summary>Decrypts to a UTF-8 string.</summary>
    public string DecryptString(byte[] key, byte[] encryptedBlob)
    {
        byte[] bytes = Decrypt(key, encryptedBlob);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }

    /// <summary>Serializes an object to JSON and encrypts it.</summary>
    public byte[] EncryptObject<T>(byte[] key, T obj)
    {
        string json = JsonSerializer.Serialize(obj);
        return EncryptString(key, json);
    }

    /// <summary>Decrypts and deserializes a JSON object.</summary>
    public T? DecryptObject<T>(byte[] key, byte[] encryptedBlob)
    {
        string json = DecryptString(key, encryptedBlob);
        return JsonSerializer.Deserialize<T>(json);
    }

    private static void ValidateKey(byte[] key)
    {
        if (key is null || key.Length != 32)
            throw new ArgumentException("Key must be exactly 32 bytes (AES-256).", nameof(key));
    }
}

