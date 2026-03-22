using CipherVault.Core.Crypto;

namespace CipherVault.Core.Services;

public class PasswordGeneratorOptions
{
    public int Length { get; set; } = 16;
    public bool IncludeUppercase { get; set; } = true;
    public bool IncludeLowercase { get; set; } = true;
    public bool IncludeNumbers { get; set; } = true;
    public bool IncludeSymbols { get; set; } = true;
    public bool ExcludeSimilarChars { get; set; } = false;
}

/// <summary>Generates cryptographically secure random passwords.</summary>
public class PasswordGeneratorService
{
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Numbers = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}|;:,.<>?";
    private const string SimilarChars = "Il1O0";

    public string Generate(PasswordGeneratorOptions options)
    {
        if (options.Length < 8 || options.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(options.Length), "Length must be 8-64.");

        string charset = BuildCharset(options);
        if (string.IsNullOrEmpty(charset))
            throw new InvalidOperationException("At least one character class must be selected.");

        // Ensure at least one char from each required class
        var required = new List<char>();
        if (options.IncludeUppercase) required.Add(GetRandomFrom(FilterSimilar(Uppercase, options)));
        if (options.IncludeLowercase) required.Add(GetRandomFrom(FilterSimilar(Lowercase, options)));
        if (options.IncludeNumbers) required.Add(GetRandomFrom(FilterSimilar(Numbers, options)));
        if (options.IncludeSymbols) required.Add(GetRandomFrom(FilterSimilar(Symbols, options)));

        var result = new char[options.Length];
        for (int i = 0; i < options.Length; i++)
            result[i] = GetRandomFrom(charset);

        // Overwrite first N positions with required chars, then shuffle
        for (int i = 0; i < required.Count && i < options.Length; i++)
            result[i] = required[i];

        // Fisher-Yates shuffle
        for (int i = result.Length - 1; i > 0; i--)
        {
            int j = SecureRandom.GetInt(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return new string(result);
    }

    private static string BuildCharset(PasswordGeneratorOptions opt)
    {
        var sb = new System.Text.StringBuilder();
        if (opt.IncludeUppercase) sb.Append(FilterSimilar(Uppercase, opt));
        if (opt.IncludeLowercase) sb.Append(FilterSimilar(Lowercase, opt));
        if (opt.IncludeNumbers) sb.Append(FilterSimilar(Numbers, opt));
        if (opt.IncludeSymbols) sb.Append(FilterSimilar(Symbols, opt));
        return sb.ToString();
    }

    private static string FilterSimilar(string chars, PasswordGeneratorOptions opt)
        => opt.ExcludeSimilarChars ? new string(chars.Where(c => !SimilarChars.Contains(c)).ToArray()) : chars;

    private static char GetRandomFrom(string charset)
        => charset[SecureRandom.GetInt(charset.Length)];
}

