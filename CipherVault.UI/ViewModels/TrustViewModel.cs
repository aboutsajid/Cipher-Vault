using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using System.Windows.Input;

namespace CipherVault.UI.ViewModels;

public class TrustViewModel : ViewModelBase
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly MainViewModel _mainViewModel;
    private AppSettings _settings = new();
    private string _statusMessage = string.Empty;
    private bool _isMandatory;

    private bool _confirmMasterPassword;
    private bool _confirmBackup;
    private bool _confirmTransparency;

    public bool ConfirmMasterPassword
    {
        get => _confirmMasterPassword;
        set { SetField(ref _confirmMasterPassword, value); NotifyChecklistChanged(); }
    }

    public bool ConfirmBackup
    {
        get => _confirmBackup;
        set { SetField(ref _confirmBackup, value); NotifyChecklistChanged(); }
    }

    public bool ConfirmTransparency
    {
        get => _confirmTransparency;
        set { SetField(ref _confirmTransparency, value); NotifyChecklistChanged(); }
    }

    public bool IsMandatory
    {
        get => _isMandatory;
        set
        {
            if (SetField(ref _isMandatory, value))
            {
                OnPropertyChanged(nameof(CanGoBack));
            }
        }
    }

    public bool AutoLockSecure => _settings.AutoLockMinutes <= 10;
    public bool ClipboardSecure => _settings.ClipboardClearSeconds <= 30;

    public string AutoLockStatus => AutoLockSecure
        ? $"Auto-lock is secure ({_settings.AutoLockMinutes} min)."
        : $"Auto-lock is high ({_settings.AutoLockMinutes} min). Recommended <= 10.";

    public string ClipboardStatus => ClipboardSecure
        ? $"Clipboard auto-clear is secure ({_settings.ClipboardClearSeconds}s)."
        : $"Clipboard auto-clear is high ({_settings.ClipboardClearSeconds}s). Recommended <= 30s.";

    public int ChecklistCompletedCount =>
        (ConfirmMasterPassword ? 1 : 0) +
        (ConfirmBackup ? 1 : 0) +
        (ConfirmTransparency ? 1 : 0) +
        (AutoLockSecure ? 1 : 0) +
        (ClipboardSecure ? 1 : 0);

    public int ChecklistTotalCount => 5;
    public bool ChecklistComplete => ChecklistCompletedCount >= ChecklistTotalCount;
    public int TrustScorePercent => (int)Math.Round((ChecklistCompletedCount * 100.0) / ChecklistTotalCount, MidpointRounding.AwayFromZero);
    public string TrustGrade => TrustScorePercent switch
    {
        >= 100 => "A+",
        >= 90 => "A",
        >= 80 => "B",
        >= 60 => "C",
        _ => "D"
    };
    public string TrustScoreLabel => $"Trust Score: {TrustScorePercent}/100 ({TrustGrade})";
    public bool IsTenOutOfTenReady => TrustScorePercent == 100;
    public string ImprovementHint => BuildImprovementHint();
    public int SecurityStreakDays => _settings.SecurityStreakDays;
    public int CompletedChallengeCount => _settings.CompletedChallengeCount;
    public string GamifiedStatus => BuildGamifiedStatus();
    public bool CanGoBack => !IsMandatory;
    public string ChecklistSummary => $"Checklist: {ChecklistCompletedCount}/{ChecklistTotalCount} complete";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public ICommand ApplyRecommendedCommand { get; }
    public ICommand SaveCommand { get; }

    public event Action? OnSaved;

    public TrustViewModel(ISettingsRepository settingsRepo, MainViewModel mainViewModel)
    {
        _settingsRepo = settingsRepo;
        _mainViewModel = mainViewModel;

        ApplyRecommendedCommand = new RelayCommand(_ => ApplyRecommendedSecurity());
        SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _settings = await _settingsRepo.GetSettingsAsync();
        ConfirmMasterPassword = _settings.OnboardingMasterPasswordConfirmed;
        ConfirmBackup = _settings.OnboardingBackupConfirmed;
        ConfirmTransparency = _settings.OnboardingTransparencyConfirmed;
        NotifyChecklistChanged();
    }

    private void ApplyRecommendedSecurity()
    {
        _settings.AutoLockMinutes = Math.Min(_settings.AutoLockMinutes, 10);
        _settings.ClipboardClearSeconds = Math.Min(_settings.ClipboardClearSeconds, 30);
        StatusMessage = "Recommended values applied. Save to persist.";
        NotifyChecklistChanged();
    }

    private async Task SaveAsync()
    {
        try
        {
            _settings.OnboardingMasterPasswordConfirmed = ConfirmMasterPassword;
            _settings.OnboardingBackupConfirmed = ConfirmBackup;
            _settings.OnboardingTransparencyConfirmed = ConfirmTransparency;

            await _settingsRepo.SaveSettingsAsync(_settings);
            await _mainViewModel.RefreshSettingsAsync();

            if (ChecklistComplete)
            {
                StatusMessage = "Trust checklist complete and saved. Security score is 100/100.";
                OnSaved?.Invoke();
                return;
            }

            StatusMessage = IsMandatory
                ? "Mandatory setup: complete remaining checklist points before continuing."
                : "Saved. Complete all checklist points for maximum protection.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void NotifyChecklistChanged()
    {
        OnPropertyChanged(nameof(AutoLockSecure));
        OnPropertyChanged(nameof(ClipboardSecure));
        OnPropertyChanged(nameof(AutoLockStatus));
        OnPropertyChanged(nameof(ClipboardStatus));
        OnPropertyChanged(nameof(ChecklistCompletedCount));
        OnPropertyChanged(nameof(ChecklistSummary));
        OnPropertyChanged(nameof(ChecklistComplete));
        OnPropertyChanged(nameof(TrustScorePercent));
        OnPropertyChanged(nameof(TrustGrade));
        OnPropertyChanged(nameof(TrustScoreLabel));
        OnPropertyChanged(nameof(IsTenOutOfTenReady));
        OnPropertyChanged(nameof(ImprovementHint));
        OnPropertyChanged(nameof(SecurityStreakDays));
        OnPropertyChanged(nameof(CompletedChallengeCount));
        OnPropertyChanged(nameof(GamifiedStatus));
    }

    private string BuildImprovementHint()
    {
        if (ChecklistComplete)
            return "Perfect setup. Keep backups fresh and review settings monthly.";

        var gaps = new List<string>();
        if (!ConfirmMasterPassword) gaps.Add("confirm master password quality");
        if (!ConfirmBackup) gaps.Add("verify encrypted backup");
        if (!ConfirmTransparency) gaps.Add("acknowledge transparency items");
        if (!AutoLockSecure) gaps.Add("set auto-lock to 10 min or less");
        if (!ClipboardSecure) gaps.Add("set clipboard clear to 30s or less");
        return "Next best fixes: " + string.Join("; ", gaps) + ".";
    }

    private string BuildGamifiedStatus()
    {
        if (_settings.SecurityStreakDays >= 7 && _settings.CompletedChallengeCount >= 3)
            return "Badge momentum: Legendary";
        if (_settings.SecurityStreakDays >= 3 || _settings.CompletedChallengeCount >= 1)
            return "Badge momentum: Rising";
        return "Badge momentum: Starter";
    }
}
