using System.Collections.Generic;

namespace CipherVault.Core.Services;

/// <summary>
/// Evaluates password quality and reuse risk for decision prompts (e.g., history rollback).
/// </summary>
public class PasswordRiskAdvisorService
{
    private const int MinRecommendedLength = 12;

    public List<string> EvaluateWarnings(string password, IEnumerable<string> otherPasswords)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            warnings.Add("Password is empty.");
            return warnings;
        }

        if (password.Length < MinRecommendedLength)
            warnings.Add($"Password is too short ({password.Length} chars; recommend {MinRecommendedLength}+).");
        if (!password.Any(char.IsUpper))
            warnings.Add("Missing uppercase letters.");
        if (!password.Any(char.IsLower))
            warnings.Add("Missing lowercase letters.");
        if (!password.Any(char.IsDigit))
            warnings.Add("Missing numbers.");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            warnings.Add("Missing symbols.");

        int reuseCount = otherPasswords.Count(p => string.Equals(p, password, StringComparison.Ordinal));
        if (reuseCount > 0)
            warnings.Add($"Password reused in {reuseCount} other entr{(reuseCount == 1 ? "y" : "ies")}.");

        return warnings;
    }
}
