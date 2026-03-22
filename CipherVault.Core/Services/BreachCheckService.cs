using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace CipherVault.Core.Services;

/// <summary>
/// Checks passwords against HaveIBeenPwned using k-anonymity.
/// PRIVACY: Only the first 5 hex chars of the SHA-1 hash are sent over the network.
/// The full password is never transmitted. Network calls are user-triggered only.
/// </summary>
public class BreachCheckService
{
    private readonly HttpClient _httpClient;

    public BreachCheckService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CipherVault-BreachCheck/1.0");
    }

    /// <summary>
    /// Checks if a password has appeared in known data breaches using HIBP k-anonymity API.
    /// Returns the number of times it appeared (0 = not found).
    /// </summary>
    /// <param name="password">The password to check (never transmitted in full).</param>
    public async Task<int> CheckPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        // SHA-1 hash of password
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(password));
        string hashHex = Convert.ToHexString(hash).ToUpperInvariant();

        string prefix = hashHex[..5];
        string suffix = hashHex[5..];

        // Only prefix (5 chars) sent to HIBP
        string url = $"https://api.pwnedpasswords.com/range/{prefix}";
        string response = await _httpClient.GetStringAsync(url, cancellationToken);

        // Parse response: each line is "SUFFIX:COUNT"
        foreach (string line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Trim().Split(':');
            if (parts.Length == 2 && parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(parts[1], out int count))
                    return count;
            }
        }

        return 0;
    }
}

