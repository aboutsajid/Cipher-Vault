using CipherVault.Core.Models;

namespace CipherVault.Core.Services;

public class AuditResult
{
    public string EntryTitle { get; set; } = string.Empty;
    public int EntryId { get; set; }
    public List<string> Issues { get; set; } = new();
}

/// <summary>Audits decrypted passwords for weaknesses and reuse. All checks happen in memory only.</summary>
public class PasswordAuditService
{
    private const int MinRecommendedLength = 12;

    public List<AuditResult> Audit(IEnumerable<VaultEntryPlain> entries)
    {
        var list = entries.ToList();
        var results = new List<AuditResult>();

        // Track password occurrences for reuse detection (in memory only)
        var pwOccurrences = new Dictionary<string, List<(int Id, string Title)>>(StringComparer.Ordinal);

        foreach (var entry in list)
        {
            var issues = new List<string>();
            var pw = entry.Password;

            if (string.IsNullOrEmpty(pw))
            {
                issues.Add("Password is empty.");
            }
            else
            {
                if (pw.Length < MinRecommendedLength)
                    issues.Add($"Password is too short ({pw.Length} chars; recommend {MinRecommendedLength}+).");

                if (!pw.Any(char.IsUpper))
                    issues.Add("Missing uppercase letters.");
                if (!pw.Any(char.IsLower))
                    issues.Add("Missing lowercase letters.");
                if (!pw.Any(char.IsDigit))
                    issues.Add("Missing numbers.");
                if (!pw.Any(c => !char.IsLetterOrDigit(c)))
                    issues.Add("Missing symbols.");

                if (!pwOccurrences.ContainsKey(pw))
                    pwOccurrences[pw] = new();
                pwOccurrences[pw].Add((entry.Id, entry.Title));
            }

            if (issues.Count > 0)
                results.Add(new AuditResult { EntryId = entry.Id, EntryTitle = entry.Title, Issues = issues });
        }

        // Add reuse issues
        foreach (var (pw, usages) in pwOccurrences)
        {
            if (usages.Count > 1)
            {
                foreach (var (id, title) in usages)
                {
                    var existing = results.FirstOrDefault(r => r.EntryId == id);
                    if (existing == null)
                    {
                        existing = new AuditResult { EntryId = id, EntryTitle = title };
                        results.Add(existing);
                    }
                    existing.Issues.Add($"Password reused across {usages.Count} entries.");
                }
            }
        }

        return results;
    }
}

