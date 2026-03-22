using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace CipherVault.UI.ViewModels;

public partial class MainViewModel
{
    private void EnsureBrowserCaptureRunning()
    {
        if (_browserCapture.IsRunning) return;

        try
        {
            _browserCapture.Start();
            StatusText = $"Browser capture active on 127.0.0.1:{_browserCapture.Port}";
        }
        catch (Exception ex)
        {
            StatusText = $"Browser capture unavailable: {ex.Message}";
        }
    }

    private void OnCredentialCaptured(object? sender, BrowserCredentialCapturedEventArgs captured)
    {
        if (!_session.IsUnlocked) return;

        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await HandleCredentialCapturedAsync(captured);
            }
            catch (Exception ex)
            {
                StatusText = $"Capture processing failed: {ex.Message}";
            }
        });
    }

    private async Task HandleCredentialCapturedAsync(BrowserCredentialCapturedEventArgs captured)
    {
        StatusText = $"Capture detected from {captured.SourceBrowser}: {captured.Title}";

        var normalizedHost = _browserDomainService.NormalizeDomain(captured.Url);
        var locationPart = normalizedHost.Length > 0
            ? normalizedHost
            : captured.Url.Trim().ToLowerInvariant();
        var captureFingerprint = $"{locationPart}|{captured.Username.Trim().ToLowerInvariant()}|{captured.Password}";

        if (IsRecentCaptureDuplicate(captureFingerprint))
        {
            StatusText = $"Duplicate capture ignored for {captured.Title}.";
            return;
        }

        var existing = _allEntries.FirstOrDefault(e =>
            _browserDomainService.HostsMatch(e.Url, captured.Url) &&
            string.Equals(e.Username.Trim(), captured.Username.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Password, captured.Password, StringComparison.Ordinal));

        if (existing != null)
        {
            StatusText = $"Credential already saved for {captured.Title}.";
            return;
        }

        bool autoSaveByDomain = IsAutoSaveDomainMatch(normalizedHost);
        if (Settings.BrowserCaptureSilentMode || autoSaveByDomain)
        {
            await SaveCapturedEntryDirectAsync(captured);
            var reason = autoSaveByDomain ? "domain rule" : "silent mode";
            StatusText = $"Captured and auto-saved for {captured.Title} ({reason}).";
            return;
        }

        var owner = BringMainWindowToFront();
        var decision = owner != null
            ? MessageBox.Show(
                owner,
                $"Detected login from {captured.SourceBrowser}\nSite: {captured.Title}\nUsername: {(string.IsNullOrWhiteSpace(captured.Username) ? "(empty)" : captured.Username)}\n\nSave this credential to Cipher™ Vault?",
                "Browser Credential Detected",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            : MessageBox.Show(
            $"Detected login from {captured.SourceBrowser}\nSite: {captured.Title}\nUsername: {(string.IsNullOrWhiteSpace(captured.Username) ? "(empty)" : captured.Username)}\n\nSave this credential to Cipher™ Vault?",
            "Browser Credential Detected",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (decision != MessageBoxResult.Yes)
        {
            StatusText = $"Capture skipped for {captured.Title}.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(normalizedHost))
        {
            var alwaysDecision = owner != null
                ? MessageBox.Show(
                    owner,
                    $"Always auto-save future captures for '{normalizedHost}'?",
                    "Auto-Save Domain",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                : MessageBox.Show(
                    $"Always auto-save future captures for '{normalizedHost}'?",
                    "Auto-Save Domain",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (alwaysDecision == MessageBoxResult.Yes)
                await AddAutoSaveDomainAsync(normalizedHost);
        }

        await SaveCapturedEntryDirectAsync(captured);
        StatusText = $"Captured and saved for {captured.Title}.";
    }

    private Window? BringMainWindowToFront()
    {
        var owner = Application.Current.MainWindow;
        if (owner == null) return null;

        if (!owner.IsVisible)
        {
            owner.ShowInTaskbar = true;
            owner.Show();
        }

        if (owner.WindowState == WindowState.Minimized)
            owner.WindowState = WindowState.Normal;

        owner.Activate();
        owner.Topmost = true;
        owner.Topmost = false;
        owner.Focus();
        return owner;
    }

    private async Task SaveCapturedEntryDirectAsync(BrowserCredentialCapturedEventArgs captured)
    {
        var entry = BuildCapturedEntry(captured);
        await SaveEntryAsync(entry, refresh: false);
        _allEntries.Add(entry);
        await ApplyFiltersAsync();
        await UpdateSecurityInsightsAsync(persistProgress: false);
    }

    private static VaultEntryPlain BuildCapturedEntry(BrowserCredentialCapturedEventArgs captured)
    {
        return new VaultEntryPlain
        {
            Id = 0,
            Title = string.IsNullOrWhiteSpace(captured.Title) ? "Browser Login" : captured.Title,
            Username = captured.Username,
            Password = captured.Password,
            Url = captured.Url,
            Notes = $"Captured from {captured.SourceBrowser} at {captured.CapturedAtUtc.ToLocalTime():g}",
            Tags = "browser-capture",
            Favorite = false,
            PasswordReminderDays = 90,
            PasswordLastChangedUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private bool IsRecentCaptureDuplicate(string fingerprint)
    {
        var now = DateTime.UtcNow;
        var staleKeys = _recentCaptureFingerprints
            .Where(x => now - x.Value > RecentCaptureWindow)
            .Select(x => x.Key)
            .ToList();

        foreach (var staleKey in staleKeys)
            _recentCaptureFingerprints.Remove(staleKey);

        if (_recentCaptureFingerprints.TryGetValue(fingerprint, out var previous)
            && now - previous <= RecentCaptureWindow)
        {
            return true;
        }

        _recentCaptureFingerprints[fingerprint] = now;
        return false;
    }

    private bool IsAutoSaveDomainMatch(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        var configured = _browserDomainService.ParseDomainList(Settings.BrowserCaptureAutoSaveDomains);
        return configured.Any(domain => _browserDomainService.IsDomainOrSubdomainMatch(host, domain));
    }

    private async Task AddAutoSaveDomainAsync(string host)
    {
        var normalizedHost = _browserDomainService.NormalizeDomain(host);
        if (string.IsNullOrWhiteSpace(normalizedHost)) return;

        var configured = _browserDomainService.ParseDomainList(Settings.BrowserCaptureAutoSaveDomains);
        if (!configured.Add(normalizedHost)) return;

        Settings.BrowserCaptureAutoSaveDomains = string.Join(", ", configured.OrderBy(x => x));

        var settingsRepo = App.Services!.GetRequiredService<ISettingsRepository>();
        await settingsRepo.SaveSettingsAsync(Settings);
        UpdateBrowserFlowLabel();
        await UpdateSecurityInsightsAsync(persistProgress: false);
    }

    private Task<IReadOnlyList<BrowserAutofillCredential>> ResolveAutofillQueryAsync(BrowserAutofillQueryRequest query)
    {
        if (!_session.IsUnlocked)
            return Task.FromResult<IReadOnlyList<BrowserAutofillCredential>>(Array.Empty<BrowserAutofillCredential>());

        int limit = Math.Clamp(query.Limit, 1, 12);
        string host = _browserDomainService.NormalizeDomain(query.Url);
        if (string.IsNullOrWhiteSpace(host))
            return Task.FromResult<IReadOnlyList<BrowserAutofillCredential>>(Array.Empty<BrowserAutofillCredential>());

        var matches = _allEntries
            .Where(entry => _browserDomainService.HostsCompatibleForAutofill(entry.Url, query.Url))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Password))
            .OrderByDescending(entry => entry.Favorite)
            .ThenByDescending(entry => entry.UpdatedAt)
            .Take(limit)
            .Select(entry => new BrowserAutofillCredential
            {
                Title = string.IsNullOrWhiteSpace(entry.Title) ? host : entry.Title,
                Username = entry.Username,
                Password = entry.Password,
                Url = entry.Url,
                Favorite = entry.Favorite
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<BrowserAutofillCredential>>(matches);
    }

    private static string SerializeEntryIds(IEnumerable<int> entryIds, bool preserveOrder)
    {
        var normalizedIds = new List<int>();
        var seen = new HashSet<int>();

        foreach (int entryId in entryIds ?? Enumerable.Empty<int>())
        {
            if (entryId <= 0 || !seen.Add(entryId))
                continue;

            normalizedIds.Add(entryId);
        }

        if (!preserveOrder)
            normalizedIds.Sort();

        if (normalizedIds.Count > MaxPersistedRemediationEntryIds)
            normalizedIds = normalizedIds.Take(MaxPersistedRemediationEntryIds).ToList();

        return string.Join(",", normalizedIds);
    }

    private void OpenBrowserSetup()
    {
        string extensionPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "chrome");
        if (!Directory.Exists(extensionPath))
        {
            extensionPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "BrowserExtension",
                "chrome"));
        }

        if (Directory.Exists(extensionPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = extensionPath,
                UseShellExecute = true
            });

            MessageBox.Show(
                $"1) Open Chrome/Edge extensions page.\n2) Enable Developer mode.\n3) Load unpacked and select:\n{extensionPath}\n\nCipher™ endpoint:\n{_browserCapture.Endpoint}",
                "Browser Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                "Browser extension folder not found.",
                "Browser Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}


