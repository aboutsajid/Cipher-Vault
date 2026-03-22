using CipherVault.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace CipherVault.UI.ViewModels;

public partial class MainViewModel
{
    private async Task UpdateSecurityInsightsAsync(bool persistProgress)
    {
        var auditResults = await Task.Run(() => _auditService.Audit(_allEntries));
        var weakEntryIds = auditResults.Select(r => r.EntryId).Distinct().ToHashSet();

        WeakEntriesCount = weakEntryIds.Count;
        ReusedEntriesCount = auditResults.Count(r => r.Issues.Any(i =>
            i.Contains("reused", StringComparison.OrdinalIgnoreCase)));
        SecureEntriesCount = Math.Max(0, _allEntries.Count - WeakEntriesCount);
        SecurityHealthPercent = _allEntries.Count == 0
            ? 100
            : (int)Math.Round((SecureEntriesCount * 100.0) / _allEntries.Count, MidpointRounding.AwayFromZero);

        OnPropertyChanged(nameof(TotalEntriesCount));
        OnPropertyChanged(nameof(FavoriteEntriesCount));
        OnPropertyChanged(nameof(SecurityHealthLabel));

        BuildSmartSuggestions();
        await UpdateChallengeAndStreakAsync(persistProgress);
        UpdateBadge();

        OnPropertyChanged(nameof(HasSuggestions));
    }

    private void BuildSmartSuggestions()
    {
        int dueSoonCount = CountPasswordsDueForRotation(7);
        var suggestions = _securityInsightsService.BuildSmartSuggestions(
            settings: Settings,
            totalEntriesCount: _allEntries.Count,
            weakEntriesCount: WeakEntriesCount,
            reusedEntriesCount: ReusedEntriesCount,
            dueSoonCount: dueSoonCount,
            isOnboardingComplete: IsOnboardingComplete,
            utcNow: DateTime.UtcNow);

        SmartSuggestions = new ObservableCollection<string>(suggestions);
    }

    private async Task UpdateChallengeAndStreakAsync(bool persistProgress)
    {
        var challenge = _securityInsightsService.ResolveWeeklyChallenge(
            settings: Settings,
            totalEntriesCount: TotalEntriesCount,
            weakEntriesCount: WeakEntriesCount,
            favoriteEntriesCount: FavoriteEntriesCount,
            utcNow: DateTime.UtcNow);

        WeeklyChallengeTitle = challenge.Title;
        WeeklyChallengeStatus = challenge.Status;
        IsWeeklyChallengeComplete = challenge.Completed;

        bool shouldPersist = false;
        var today = DateTime.UtcNow.Date;
        var lastCheck = Settings.LastSecurityCheckUtc?.Date;
        bool strongDailyState = TrustScorePercent >= 80 && SecurityHealthPercent >= 80;

        if (lastCheck != today)
        {
            if (strongDailyState)
            {
                Settings.SecurityStreakDays = lastCheck == today.AddDays(-1)
                    ? Settings.SecurityStreakDays + 1
                    : 1;
            }
            else
            {
                Settings.SecurityStreakDays = 0;
            }

            Settings.LastSecurityCheckUtc = DateTime.UtcNow;
            shouldPersist = true;
        }

        if (challenge.Completed)
        {
            var lastCompletedDay = Settings.LastChallengeCompletedUtc?.Date;
            if (lastCompletedDay != today)
            {
                Settings.CompletedChallengeCount += 1;
                Settings.LastChallengeCompletedUtc = DateTime.UtcNow;
                shouldPersist = true;
            }
        }

        OnPropertyChanged(nameof(SecurityStreakDays));
        OnPropertyChanged(nameof(CompletedChallengeCount));

        if (persistProgress && shouldPersist)
        {
            var settingsRepo = App.Services!.GetRequiredService<ISettingsRepository>();
            await settingsRepo.SaveSettingsAsync(Settings);
        }
    }

    private void UpdateBadge()
    {
        var badge = _securityInsightsService.ResolveBadge(
            trustScorePercent: TrustScorePercent,
            securityHealthPercent: SecurityHealthPercent,
            securityStreakDays: Settings.SecurityStreakDays,
            completedChallengeCount: Settings.CompletedChallengeCount);

        BadgeTitle = badge.Title;
        BadgeSubtitle = badge.Subtitle;
    }

    private void UpdateBackupConfidenceLabel()
    {
        BackupConfidenceLabel = _securityInsightsService.ResolveBackupConfidenceLabel(
            Settings.LastBackupUtc,
            DateTime.UtcNow);
    }

    private void UpdateBrowserFlowLabel()
    {
        int autoSaveDomainCount = _browserDomainService.ParseDomainList(Settings.BrowserCaptureAutoSaveDomains).Count;
        BrowserFlowLabel = _securityInsightsService.ResolveBrowserFlowLabel(Settings.BrowserCaptureSilentMode, autoSaveDomainCount);
    }

    private void UpdateOnboardingSummary()
    {
        OnboardingSummary = _securityInsightsService.ResolveOnboardingSummary(Settings);
        OnPropertyChanged(nameof(IsOnboardingComplete));
        OnPropertyChanged(nameof(TrustScorePercent));
        OnPropertyChanged(nameof(TrustScoreLabel));
    }
}
