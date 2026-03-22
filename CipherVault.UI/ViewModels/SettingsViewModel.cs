using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using CipherVault.UI.Services;
using System.IO;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace CipherVault.UI.ViewModels;


public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly VaultSessionService _session;
    private readonly WindowsHelloUnlockService _windowsHello;
    private readonly StartupRegistrationService _startupRegistrationService;
    private AppSettings _settings = new();
    private string _statusMessage = string.Empty;

    public AppSettings Settings { get => _settings; set => SetField(ref _settings, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public ICommand SaveCommand { get; }

    public event Action? OnSaved;

    public SettingsViewModel(
        ISettingsRepository settingsRepo,
        VaultSessionService session,
        WindowsHelloUnlockService windowsHello,
        StartupRegistrationService startupRegistrationService)
    {
        _settingsRepo = settingsRepo;
        _session = session;
        _windowsHello = windowsHello;
        _startupRegistrationService = startupRegistrationService;
        SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Settings = await _settingsRepo.GetSettingsAsync();
        Settings.Theme = NormalizeThemeMode(Settings.Theme);
        Settings.RecycleBinRetentionDays = NormalizeRecycleBinRetentionDays(Settings.RecycleBinRetentionDays);
    }

    private async Task SaveAsync()
    {
        try
        {
            Settings.Theme = NormalizeThemeMode(Settings.Theme);
            Settings.RecycleBinRetentionDays = NormalizeRecycleBinRetentionDays(Settings.RecycleBinRetentionDays);
            await _settingsRepo.SaveSettingsAsync(Settings);
            App.ApplyTheme(Settings.Theme);

            string startupStatus = string.Empty;
            try
            {
                _startupRegistrationService.Configure(Settings.StartWithWindows);
            }
            catch (Exception startupEx)
            {
                startupStatus = $" Startup launch update failed: {startupEx.Message}";
            }

            string helloStatus = string.Empty;
            if (Settings.WindowsHelloEnabled)
            {
                bool available = await _windowsHello.IsAvailableAsync();
                if (!available)
                {
                    Settings.WindowsHelloEnabled = false;
                    await _settingsRepo.SaveSettingsAsync(Settings);
                    await _windowsHello.DisableAsync();
                    helloStatus = " Windows Hello is not available on this device.";
                }
                else if (_session.IsUnlocked)
                {
                    bool enrolled = await _windowsHello.TryEnrollAsync(_session.GetKey());
                    helloStatus = enrolled
                        ? " Windows Hello quick unlock is ready."
                        : " Windows Hello enabled, but key enrollment failed.";
                }
                else
                {
                    helloStatus = " Unlock once with password to finish Windows Hello setup.";
                }
            }
            else
            {
                await _windowsHello.DisableAsync();
            }

            StatusMessage = $"Settings saved.{helloStatus}{startupStatus}";
            OnSaved?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private static int NormalizeRecycleBinRetentionDays(int days)
    {
        return Math.Clamp(days, 1, 3650);
    }

    private static string NormalizeThemeMode(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "system" => "System",
            _ => "Light"
        };
    }
}
