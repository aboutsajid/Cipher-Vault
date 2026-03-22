using CipherVault.Core.Models;

namespace CipherVault.UI.Services;

public readonly record struct WeeklyChallengeResult(string Title, string Status, bool Completed);
public readonly record struct BadgeResult(string Title, string Subtitle);

public sealed class SecurityInsightsService
{
    private const int ChecklistTarget = 5;

    public int CalculateChecklistCompletedCount(AppSettings settings)
    {
        int count = 0;
        if (settings.OnboardingMasterPasswordConfirmed) count++;
        if (settings.OnboardingBackupConfirmed) count++;
        if (settings.OnboardingTransparencyConfirmed) count++;
        if (settings.AutoLockMinutes <= 10) count++;
        if (settings.ClipboardClearSeconds <= 30) count++;
        return count;
    }

    public IReadOnlyList<string> BuildSmartSuggestions(
        AppSettings settings,
        int totalEntriesCount,
        int weakEntriesCount,
        int reusedEntriesCount,
        int dueSoonCount,
        bool isOnboardingComplete,
        DateTime utcNow)
    {
        var suggestions = new List<string>();

        if (totalEntriesCount < 5)
            suggestions.Add("Add key accounts (email, banking, social) so your vault is truly useful daily.");

        if (weakEntriesCount > 0)
            suggestions.Add($"Fix {weakEntriesCount} weak entries from Audit for an instant security jump.");

        if (reusedEntriesCount > 0)
            suggestions.Add("Replace reused passwords with unique values from Generator.");

        if (dueSoonCount > 0)
            suggestions.Add($"Rotate {dueSoonCount} password reminder entr{(dueSoonCount == 1 ? "y" : "ies")} within 7 days.");

        if (!isOnboardingComplete)
            suggestions.Add("Complete the trust checklist to secure baseline settings.");

        if (!settings.LastBackupUtc.HasValue)
        {
            suggestions.Add("Create your first encrypted backup from Import/Export.");
        }
        else
        {
            var daysSinceBackup = (utcNow - settings.LastBackupUtc.Value).TotalDays;
            if (daysSinceBackup >= 14)
                suggestions.Add("Backup is older than 14 days. Create a fresh encrypted backup.");
        }

        if (!settings.BrowserCaptureSilentMode && string.IsNullOrWhiteSpace(settings.BrowserCaptureAutoSaveDomains))
            suggestions.Add("Add trusted auto-save domains in Settings for faster browser workflow.");

        if (suggestions.Count == 0)
            suggestions.Add("Excellent posture. Keep your streak alive and complete weekly challenge.");

        return suggestions;
    }

    public WeeklyChallengeResult ResolveWeeklyChallenge(
        AppSettings settings,
        int totalEntriesCount,
        int weakEntriesCount,
        int favoriteEntriesCount,
        DateTime utcNow)
    {
        int weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(utcNow);
        int challengeType = weekNumber % 3;

        return challengeType switch
        {
            0 => ResolveZeroWeakChallenge(totalEntriesCount, weakEntriesCount),
            1 => ResolveBackupChallenge(settings, utcNow),
            _ => ResolveFavoritesChallenge(favoriteEntriesCount)
        };
    }

    public BadgeResult ResolveBadge(
        int trustScorePercent,
        int securityHealthPercent,
        int securityStreakDays,
        int completedChallengeCount)
    {
        int points = 0;

        if (trustScorePercent >= 100) points += 2;
        else if (trustScorePercent >= 80) points += 1;

        if (securityHealthPercent >= 90) points += 2;
        else if (securityHealthPercent >= 75) points += 1;

        if (securityStreakDays >= 7) points += 2;
        else if (securityStreakDays >= 3) points += 1;

        if (completedChallengeCount >= 5) points += 2;
        else if (completedChallengeCount >= 1) points += 1;

        return points switch
        {
            >= 7 => new BadgeResult("Vault Legend", "Elite consistency across trust score, streak and challenges."),
            >= 5 => new BadgeResult("Vault Guardian", "Strong and reliable security routine."),
            >= 3 => new BadgeResult("Security Pro", "Good momentum. Keep challenge completions coming."),
            >= 1 => new BadgeResult("Rising Defender", "Foundations are ready. Build consistency next."),
            _ => new BadgeResult("Starter", "Complete checklist + first backup to level up.")
        };
    }

    public string ResolveBackupConfidenceLabel(DateTime? lastBackupUtc, DateTime utcNow)
    {
        if (!lastBackupUtc.HasValue)
            return "Backup confidence: Low (no encrypted backup yet).";

        int days = (int)Math.Floor((utcNow - lastBackupUtc.Value).TotalDays);
        if (days <= 3)
            return $"Backup confidence: High (last backup {days} day(s) ago).";
        if (days <= 14)
            return $"Backup confidence: Medium (last backup {days} day(s) ago).";
        return $"Backup confidence: Low (backup is {days} day(s) old).";
    }

    public string ResolveBrowserFlowLabel(bool silentMode, int autoSaveDomainCount)
    {
        if (silentMode)
            return "Browser flow: Turbo (silent mode ON).";

        return autoSaveDomainCount > 0
            ? $"Browser flow: Smart ({autoSaveDomainCount} auto-save domain(s))."
            : "Browser flow: Approval mode.";
    }

    public string ResolveOnboardingSummary(AppSettings settings)
    {
        int completed = CalculateChecklistCompletedCount(settings);
        return completed >= ChecklistTarget
            ? "Security checklist complete."
            : $"Security checklist: {completed}/{ChecklistTarget} complete.";
    }

    private static WeeklyChallengeResult ResolveZeroWeakChallenge(int totalEntriesCount, int weakEntriesCount)
    {
        bool completed = totalEntriesCount >= 3 && weakEntriesCount == 0;
        string status = completed
            ? "Completed: no weak passwords across 3+ entries."
            : $"Progress: {Math.Max(0, totalEntriesCount - weakEntriesCount)}/{Math.Max(3, totalEntriesCount)} strong entries.";
        return new WeeklyChallengeResult("Weekly Challenge: Zero Weak Passwords", status, completed);
    }

    private static WeeklyChallengeResult ResolveBackupChallenge(AppSettings settings, DateTime utcNow)
    {
        bool completed = settings.LastBackupUtc.HasValue &&
                         (utcNow - settings.LastBackupUtc.Value).TotalDays <= 7;
        string status = completed
            ? "Completed: encrypted backup created within last 7 days."
            : "Pending: create one encrypted backup this week.";
        return new WeeklyChallengeResult("Weekly Challenge: Backup This Week", status, completed);
    }

    private static WeeklyChallengeResult ResolveFavoritesChallenge(int favoriteEntriesCount)
    {
        bool completed = favoriteEntriesCount >= 3;
        string status = completed
            ? "Completed: 3+ critical entries marked favorite."
            : $"Progress: {favoriteEntriesCount}/3 favorites marked.";
        return new WeeklyChallengeResult("Weekly Challenge: Mark 3 Favorites", status, completed);
    }
}
