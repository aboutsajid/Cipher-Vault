using Konscious.Security.Cryptography;
using System.Text;

namespace CipherVault.Core.Crypto;

/// <summary>
/// Derives cryptographic keys from master passwords using Argon2id.
/// SECURITY: Master password is never stored. Only the derived key is held in memory while unlocked.
/// </summary>
public class KeyDerivationService
{
    public const int DefaultMemoryMB = 256;
    public const int DefaultIterations = 3;
    public const int KeyLengthBytes = 32;
    public const int SaltLengthBytes = 16;

    /// <summary>
    /// Derives a 256-bit key from the master password and salt using Argon2id.
    /// </summary>
    /// <param name="password">The master password (UTF-8 encoded).</param>
    /// <param name="salt">Random salt (at least 16 bytes).</param>
    /// <param name="memoryMB">Memory cost in MB (min 64, default 256).</param>
    /// <param name="iterations">Time cost (number of iterations, default 3).</param>
    /// <param name="parallelism">Degree of parallelism (auto-detected if 0).</param>
    /// <returns>32-byte derived key. Caller is responsible for zeroing this after use.</returns>
    public byte[] DeriveKey(
        string password,
        byte[] salt,
        int memoryMB = DefaultMemoryMB,
        int iterations = DefaultIterations,
        int parallelism = 0)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
        if (salt is null || salt.Length < SaltLengthBytes)
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));

        memoryMB = Math.Max(64, memoryMB);
        iterations = Math.Max(1, iterations);
        if (parallelism <= 0)
            parallelism = Math.Min(Environment.ProcessorCount, 4);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = memoryMB * 1024, // KB
                Iterations = iterations,
                DegreeOfParallelism = parallelism
            };
            return argon2.GetBytes(KeyLengthBytes);
        }
        finally
        {
            Array.Clear(passwordBytes, 0, passwordBytes.Length);
        }
    }

    /// <summary>
    /// Generates a fresh random salt for a new vault.
    /// </summary>
    public byte[] GenerateSalt() => SecureRandom.GetBytes(SaltLengthBytes);
}

