using System.Security.Cryptography;

namespace CipherVault.Core.Crypto;

/// <summary>
/// Provides cryptographically secure random byte generation.
/// </summary>
public static class SecureRandom
{
    /// <summary>
    /// Generates a cryptographically secure random byte array of the specified length.
    /// </summary>
    public static byte[] GetBytes(int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        return RandomNumberGenerator.GetBytes(length);
    }

    /// <summary>
    /// Generates a cryptographically secure random integer in [0, maxExclusive).
    /// </summary>
    public static int GetInt(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}

