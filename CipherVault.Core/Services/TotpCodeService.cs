using OtpNet;

namespace CipherVault.Core.Services;

public sealed class TotpCodeResult
{
    public bool IsValid { get; init; }
    public string Code { get; init; } = string.Empty;
    public int SecondsRemaining { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>
/// Parses TOTP secrets and computes current one-time codes.
/// Supports raw Base32 secrets and otpauth:// URIs.
/// </summary>
public sealed class TotpCodeService
{
    private const int DefaultDigits = 6;
    private const int DefaultStepSeconds = 30;

    public TotpCodeResult GetCurrentCode(string? secretOrUri, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(secretOrUri))
        {
            return new TotpCodeResult
            {
                IsValid = false,
                ErrorMessage = "No TOTP secret configured."
            };
        }

        if (!TryBuildTotp(secretOrUri, out var totp, out int stepSeconds, out string error))
        {
            return new TotpCodeResult
            {
                IsValid = false,
                ErrorMessage = error
            };
        }

        DateTime timestamp = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        string code = totp.ComputeTotp(timestamp);
        int secondsRemaining = GetSecondsRemaining(timestamp, stepSeconds);

        return new TotpCodeResult
        {
            IsValid = true,
            Code = code,
            SecondsRemaining = secondsRemaining
        };
    }

    private static bool TryBuildTotp(
        string secretOrUri,
        out Totp totp,
        out int stepSeconds,
        out string errorMessage)
    {
        totp = null!;
        stepSeconds = DefaultStepSeconds;
        errorMessage = string.Empty;

        string normalizedSecret = NormalizeSecret(secretOrUri);
        int digits = DefaultDigits;
        OtpHashMode mode = OtpHashMode.Sha1;

        if (secretOrUri.TrimStart().StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseOtpAuthUri(secretOrUri, out normalizedSecret, out stepSeconds, out digits, out mode, out errorMessage))
                return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedSecret))
        {
            errorMessage = "Invalid TOTP secret.";
            return false;
        }

        try
        {
            byte[] secretBytes = Base32Encoding.ToBytes(normalizedSecret);
            totp = new Totp(secretBytes, step: stepSeconds, mode: mode, totpSize: digits);
            return true;
        }
        catch
        {
            errorMessage = "Invalid TOTP secret format.";
            return false;
        }
    }

    private static bool TryParseOtpAuthUri(
        string uriText,
        out string secret,
        out int stepSeconds,
        out int digits,
        out OtpHashMode mode,
        out string errorMessage)
    {
        secret = string.Empty;
        stepSeconds = DefaultStepSeconds;
        digits = DefaultDigits;
        mode = OtpHashMode.Sha1;
        errorMessage = string.Empty;

        if (!Uri.TryCreate(uriText.Trim(), UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Invalid otpauth URI.";
            return false;
        }

        string? rawSecret = GetQueryParameter(uri, "secret");
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            errorMessage = "TOTP URI does not include a secret.";
            return false;
        }

        secret = NormalizeSecret(rawSecret);

        string? period = GetQueryParameter(uri, "period");
        if (int.TryParse(period, out int parsedPeriod) && parsedPeriod > 0)
            stepSeconds = parsedPeriod;

        string? digitsParam = GetQueryParameter(uri, "digits");
        if (int.TryParse(digitsParam, out int parsedDigits) && parsedDigits is >= 6 and <= 10)
            digits = parsedDigits;

        string? algorithm = GetQueryParameter(uri, "algorithm");
        if (!string.IsNullOrWhiteSpace(algorithm))
        {
            mode = algorithm.Trim().ToUpperInvariant() switch
            {
                "SHA256" => OtpHashMode.Sha256,
                "SHA512" => OtpHashMode.Sha512,
                _ => OtpHashMode.Sha1
            };
        }

        return true;
    }

    private static string? GetQueryParameter(Uri uri, string key)
    {
        string query = uri.Query;
        if (string.IsNullOrEmpty(query))
            return null;

        string trimmed = query[0] == '?' ? query[1..] : query;
        foreach (string pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int idx = pair.IndexOf('=');
            string name;
            string value;
            if (idx < 0)
            {
                name = Uri.UnescapeDataString(pair);
                value = string.Empty;
            }
            else
            {
                name = Uri.UnescapeDataString(pair[..idx]);
                value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            }

            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static string NormalizeSecret(string secret)
        => secret.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);

    private static int GetSecondsRemaining(DateTime utcNow, int stepSeconds)
    {
        long unixSeconds = new DateTimeOffset(utcNow).ToUnixTimeSeconds();
        int remainder = (int)(unixSeconds % stepSeconds);
        int remaining = stepSeconds - remainder;
        return remaining == 0 ? stepSeconds : remaining;
    }
}
