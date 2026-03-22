using CipherVault.Core.Interfaces;
using CipherVault.UI.Services;
using System.Windows;
using System.Windows.Input;

namespace CipherVault.UI.ViewModels;

public class UnlockViewModel : ViewModelBase
{
    private readonly VaultSessionService _session;
    private readonly IVaultRepository _vaultRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly WindowsHelloUnlockService _windowsHello;

    private string _statusMessage = string.Empty;
    private bool _isNewVault;
    private bool _isBusy;
    private bool _canUseWindowsHello;
    private string _confirmPassword = string.Empty;
    private string _vaultTitle = "Cipher™ Vault";

    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public bool IsNewVault { get => _isNewVault; set { SetField(ref _isNewVault, value); OnPropertyChanged(nameof(ShowConfirm)); } }
    public bool ShowConfirm => _isNewVault;
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public bool CanUseWindowsHello { get => _canUseWindowsHello; set => SetField(ref _canUseWindowsHello, value); }
    public string ConfirmPassword { get => _confirmPassword; set => SetField(ref _confirmPassword, value); }
    public string VaultTitle { get => _vaultTitle; set => SetField(ref _vaultTitle, value); }

    public ICommand UnlockCommand { get; }

    // Callback injected by parent to navigate after unlock
    public Func<Task>? OnUnlocked { get; set; }

    public UnlockViewModel(
        VaultSessionService session,
        IVaultRepository vaultRepo,
        ISettingsRepository settingsRepo,
        WindowsHelloUnlockService windowsHello)
    {
        _session = session;
        _vaultRepo = vaultRepo;
        _settingsRepo = settingsRepo;
        _windowsHello = windowsHello;

        UnlockCommand = new AsyncRelayCommand(async _ => await ExecuteUnlockAsync(), _ => !IsBusy);
        _ = DetectVaultStateAsync();
    }

    private async Task DetectVaultStateAsync()
    {
        var meta = await _vaultRepo.GetVaultMetaAsync();
        IsNewVault = meta == null;
        VaultTitle = meta == null ? "Create New Vault" : "Unlock Vault";

        if (meta == null)
        {
            CanUseWindowsHello = false;
            return;
        }

        var settings = await _settingsRepo.GetSettingsAsync();
        CanUseWindowsHello = settings.WindowsHelloEnabled
                             && _windowsHello.HasEnrollment
                             && await _windowsHello.IsAvailableAsync();
    }

    // Password is passed from code-behind via PasswordBox
    public async Task ExecuteUnlockWithPasswordAsync(string password, string confirm = "")
    {
        if (IsBusy) return;
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(password))
        {
            StatusMessage = "Master password cannot be empty.";
            return;
        }

        IsBusy = true;
        try
        {
            if (IsNewVault)
            {
                if (password != confirm)
                {
                    StatusMessage = "Passwords do not match.";
                    return;
                }
                if (password.Length < 8)
                {
                    StatusMessage = "Master password must be at least 8 characters.";
                    return;
                }
                await _session.CreateVaultAsync(password);
            }
            else
            {
                bool success = await _session.TryUnlockAsync(password);
                if (!success)
                {
                    StatusMessage = "Incorrect master password. Please try again.";
                    return;
                }
            }

            await EnrollWindowsHelloIfEnabledAsync();
            await DetectVaultStateAsync();

            if (OnUnlocked != null)
                await OnUnlocked();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteUnlockWithWindowsHelloAsync()
    {
        if (IsBusy || IsNewVault) return;
        StatusMessage = string.Empty;

        IsBusy = true;
        try
        {
            var helloAttempt = await _windowsHello.TryUnlockAsync();
            if (helloAttempt.Outcome != WindowsHelloUnlockOutcome.Success || helloAttempt.Key == null)
            {
                StatusMessage = helloAttempt.Message;
                await DetectVaultStateAsync();
                return;
            }

            try
            {
                bool unlocked = await _session.TryUnlockWithKeyAsync(helloAttempt.Key);
                if (!unlocked)
                {
                    StatusMessage = "Windows Hello key no longer matches vault. Unlock with password once.";
                    await _windowsHello.DisableAsync();
                    await DetectVaultStateAsync();
                    return;
                }
            }
            finally
            {
                Array.Clear(helloAttempt.Key, 0, helloAttempt.Key.Length);
            }

            if (OnUnlocked != null)
                await OnUnlocked();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteUnlockAsync()
    {
        // Handled from code-behind which passes PasswordBox.Password
        await Task.CompletedTask;
    }

    private async Task EnrollWindowsHelloIfEnabledAsync()
    {
        if (!_session.IsUnlocked) return;

        var settings = await _settingsRepo.GetSettingsAsync();
        if (!settings.WindowsHelloEnabled)
        {
            await _windowsHello.DisableAsync();
            return;
        }

        bool available = await _windowsHello.IsAvailableAsync();
        if (!available)
        {
            settings.WindowsHelloEnabled = false;
            await _settingsRepo.SaveSettingsAsync(settings);
            await _windowsHello.DisableAsync();
            return;
        }

        await _windowsHello.TryEnrollAsync(_session.GetKey());
    }
}


