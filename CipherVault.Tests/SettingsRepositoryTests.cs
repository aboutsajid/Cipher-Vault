using CipherVault.Core.Models;
using CipherVault.Data;
using CipherVault.Data.Repositories;
using Xunit;

namespace CipherVault.Tests;

public class SettingsRepositoryTests : IAsyncLifetime
{
    private string _tempPath = string.Empty;
    private string _connectionString = string.Empty;
    private SettingsRepository _repo = null!;
    private DatabaseInitializer _init = null!;

    public async Task InitializeAsync()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"cipherpw_settings_{Guid.NewGuid()}.cipherpw");
        _connectionString = $"Data Source={_tempPath}";

        _init = new DatabaseInitializer(_connectionString);
        await _init.InitializeAsync();
        _repo = new SettingsRepository(_connectionString);
    }

    public Task DisposeAsync() => TestFileCleanup.DeleteWithRetryAsync(_tempPath);

    [Fact]
    public async Task DefaultSettingsIncludeBrowserCaptureOptions()
    {
        AppSettings settings = await _repo.GetSettingsAsync();

        Assert.False(settings.BrowserCaptureSilentMode);
        Assert.True(settings.StartWithWindows);
        Assert.Equal(string.Empty, settings.BrowserCaptureAutoSaveDomains);
        Assert.Null(settings.LastBackupUtc);
        Assert.Equal(0, settings.SecurityStreakDays);
        Assert.Equal(0, settings.CompletedChallengeCount);
        Assert.Equal(30, settings.RecycleBinRetentionDays);
        Assert.Equal(string.Empty, settings.RemediationDismissedEntryIds);
        Assert.Equal(string.Empty, settings.RemediationQueueOrderEntryIds);
    }

    [Fact]
    public async Task SaveAndLoadBrowserCaptureOptionsRoundtrip()
    {
        AppSettings settings = await _repo.GetSettingsAsync();
        settings.BrowserCaptureSilentMode = true;
        settings.BrowserCaptureAutoSaveDomains = "example.com, github.com";
        settings.Theme = "Light";
        settings.AutoLockMinutes = 7;
        settings.StartWithWindows = false;
        settings.LastBackupUtc = DateTime.UtcNow;
        settings.SecurityStreakDays = 4;
        settings.CompletedChallengeCount = 2;
        settings.LastSecurityCheckUtc = DateTime.UtcNow;
        settings.LastChallengeCompletedUtc = DateTime.UtcNow;
        settings.RecycleBinRetentionDays = 45;
        settings.RemediationDismissedEntryIds = "1,5,7";
        settings.RemediationQueueOrderEntryIds = "5,1";

        await _repo.SaveSettingsAsync(settings);

        AppSettings loaded = await _repo.GetSettingsAsync();
        Assert.True(loaded.BrowserCaptureSilentMode);
        Assert.Equal("example.com, github.com", loaded.BrowserCaptureAutoSaveDomains);
        Assert.Equal("Light", loaded.Theme);
        Assert.Equal(7, loaded.AutoLockMinutes);
        Assert.False(loaded.StartWithWindows);
        Assert.True(loaded.LastBackupUtc.HasValue);
        Assert.Equal(4, loaded.SecurityStreakDays);
        Assert.Equal(2, loaded.CompletedChallengeCount);
        Assert.True(loaded.LastSecurityCheckUtc.HasValue);
        Assert.True(loaded.LastChallengeCompletedUtc.HasValue);
        Assert.Equal(45, loaded.RecycleBinRetentionDays);
        Assert.Equal("1,5,7", loaded.RemediationDismissedEntryIds);
        Assert.Equal("5,1", loaded.RemediationQueueOrderEntryIds);
    }
}
