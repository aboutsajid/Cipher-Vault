using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using CipherVault.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CipherVault.UI.ViewModels;

public partial class MainViewModel : ViewModelBase, IAuditWorkflowHost
{
    private readonly VaultSessionService _session;
    private readonly IVaultRepository _vaultRepo;
    private readonly IFolderRepository _folderRepo;
    private readonly SecureClipboardService _clipboard;
    private readonly AutoLockService _autoLock;
    private readonly BrowserCaptureService _browserCapture;
    private readonly PasswordAuditService _auditService;
    private readonly TotpCodeService _totpCodeService;
    private readonly BreachCheckService _breachCheckService;
    private readonly PasswordRiskAdvisorService _passwordRiskAdvisorService;
    private readonly BrowserDomainService _browserDomainService;
    private readonly SecurityInsightsService _securityInsightsService;

    private ObservableCollection<VaultEntryPlain> _entries = new();
    private ObservableCollection<Folder> _folders = new();
    private ObservableCollection<string> _smartSuggestions = new();
    private ObservableCollection<PasswordHistoryItem> _selectedEntryPasswordHistory = new();
    private VaultEntryPlain? _selectedEntry;
    private string _searchText = string.Empty;
    private int? _selectedFolderId;
    private bool _showFavoritesOnly;
    private bool _showRecycleBinOnly;
    private string _statusText = "Unlocked";
    private string _onboardingSummary = "Security checklist pending";
    private string _badgeTitle = "Starter";
    private string _badgeSubtitle = "Complete checklist + backup to unlock badges.";
    private string _weeklyChallengeTitle = "Weekly Challenge";
    private string _weeklyChallengeStatus = "Run one security pass to start challenge tracking.";
    private string _backupConfidenceLabel = "Backup confidence: Low (no backup yet).";
    private string _browserFlowLabel = "Browser flow: Approval mode.";
    private int _secureEntriesCount;
    private int _weakEntriesCount;
    private int _reusedEntriesCount;
    private int _securityHealthPercent = 100;
    private bool _isWeeklyChallengeComplete;
    private bool _isBusy;
    private AppSettings _settings = new();
    private string _totpCode = string.Empty;
    private string _totpStatusText = string.Empty;
    private int _totpSecondsRemaining;
    private bool _hasTotpForSelectedEntry;
    private string _selectedEntryBreachStatus = string.Empty;
    private string _selectedEntryPasswordReminderStatus = string.Empty;
    private bool _isSelectedEntryPasswordReminderWarning;
    private bool _isEntryBreachCheckRunning;
    private bool _returnToAuditAfterEditor;
    private readonly SemaphoreSlim _remediationStateSaveLock = new(1, 1);

    public ObservableCollection<VaultEntryPlain> Entries { get => _entries; set => SetField(ref _entries, value); }
    public ObservableCollection<Folder> Folders { get => _folders; set => SetField(ref _folders, value); }
    public VaultEntryPlain? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            SetField(ref _selectedEntry, value);
            SelectedEntryBreachStatus = string.Empty;
            UpdateTotpState();
            RefreshSelectedEntryPasswordHistory();
            UpdateSelectedEntryPasswordReminderStatus();
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public string SearchText { get => _searchText; set { SetField(ref _searchText, value); _ = ApplyFiltersAsync(); } }
    public int? SelectedFolderId { get => _selectedFolderId; set { SetField(ref _selectedFolderId, value); _ = ApplyFiltersAsync(); } }
    public bool ShowFavoritesOnly { get => _showFavoritesOnly; set { SetField(ref _showFavoritesOnly, value); _ = ApplyFiltersAsync(); } }
    public bool ShowRecycleBinOnly
    {
        get => _showRecycleBinOnly;
        set
        {
            if (SetField(ref _showRecycleBinOnly, value))
            {
                OnPropertyChanged(nameof(IsRecycleBinView));
                OnPropertyChanged(nameof(IsPrimaryEntriesView));
                OnPropertyChanged(nameof(DeleteActionLabel));
                _ = ApplyFiltersAsync();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public string OnboardingSummary { get => _onboardingSummary; set => SetField(ref _onboardingSummary, value); }
    public bool IsOnboardingComplete => _securityInsightsService.CalculateChecklistCompletedCount(Settings) >= 5;
    public int TotalEntriesCount => _allEntries.Count;
    public int FavoriteEntriesCount => _allEntries.Count(e => e.Favorite);
    public int RecycleBinCount => _deletedEntries.Count;
    public bool IsRecycleBinView => ShowRecycleBinOnly;
    public bool IsPrimaryEntriesView => !ShowRecycleBinOnly;
    public string DeleteActionLabel => ShowRecycleBinOnly ? "Delete Permanently" : "Delete";
    public int SecureEntriesCount { get => _secureEntriesCount; set => SetField(ref _secureEntriesCount, value); }
    public int WeakEntriesCount { get => _weakEntriesCount; set => SetField(ref _weakEntriesCount, value); }
    public int ReusedEntriesCount { get => _reusedEntriesCount; set => SetField(ref _reusedEntriesCount, value); }
    public int SecurityHealthPercent { get => _securityHealthPercent; set => SetField(ref _securityHealthPercent, value); }
    public string SecurityHealthLabel => $"Security health: {SecurityHealthPercent}/100";
    public bool HasTotpForSelectedEntry { get => _hasTotpForSelectedEntry; private set => SetField(ref _hasTotpForSelectedEntry, value); }
    public string TotpCode
    {
        get => _totpCode;
        private set
        {
            if (SetField(ref _totpCode, value))
            {
                OnPropertyChanged(nameof(HasTotpCode));
                OnPropertyChanged(nameof(TotpCountdownLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public bool HasTotpCode => !string.IsNullOrWhiteSpace(TotpCode);
    public int TotpSecondsRemaining
    {
        get => _totpSecondsRemaining;
        private set
        {
            if (SetField(ref _totpSecondsRemaining, value))
                OnPropertyChanged(nameof(TotpCountdownLabel));
        }
    }
    public string TotpStatusText
    {
        get => _totpStatusText;
        private set
        {
            if (SetField(ref _totpStatusText, value))
                OnPropertyChanged(nameof(TotpCountdownLabel));
        }
    }
    public string TotpCountdownLabel => HasTotpCode ? $"Code refreshes in {TotpSecondsRemaining}s" : TotpStatusText;
    public bool IsBreachCheckEnabled => Settings.AllowBreachCheck;
    public bool IsEntryBreachCheckRunning
    {
        get => _isEntryBreachCheckRunning;
        private set
        {
            if (SetField(ref _isEntryBreachCheckRunning, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public string SelectedEntryBreachStatus { get => _selectedEntryBreachStatus; private set => SetField(ref _selectedEntryBreachStatus, value); }
    public string SelectedEntryPasswordReminderStatus { get => _selectedEntryPasswordReminderStatus; private set => SetField(ref _selectedEntryPasswordReminderStatus, value); }
    public bool IsSelectedEntryPasswordReminderWarning { get => _isSelectedEntryPasswordReminderWarning; private set => SetField(ref _isSelectedEntryPasswordReminderWarning, value); }
    public int SecurityStreakDays => Settings.SecurityStreakDays;
    public int CompletedChallengeCount => Settings.CompletedChallengeCount;
    public string BadgeTitle { get => _badgeTitle; set => SetField(ref _badgeTitle, value); }
    public string BadgeSubtitle { get => _badgeSubtitle; set => SetField(ref _badgeSubtitle, value); }
    public string WeeklyChallengeTitle { get => _weeklyChallengeTitle; set => SetField(ref _weeklyChallengeTitle, value); }
    public string WeeklyChallengeStatus { get => _weeklyChallengeStatus; set => SetField(ref _weeklyChallengeStatus, value); }
    public bool IsWeeklyChallengeComplete { get => _isWeeklyChallengeComplete; set => SetField(ref _isWeeklyChallengeComplete, value); }
    public string BackupConfidenceLabel { get => _backupConfidenceLabel; set => SetField(ref _backupConfidenceLabel, value); }
    public string BrowserFlowLabel { get => _browserFlowLabel; set => SetField(ref _browserFlowLabel, value); }
    public ObservableCollection<string> SmartSuggestions { get => _smartSuggestions; set => SetField(ref _smartSuggestions, value); }
    public ObservableCollection<PasswordHistoryItem> SelectedEntryPasswordHistory
    {
        get => _selectedEntryPasswordHistory;
        private set
        {
            if (SetField(ref _selectedEntryPasswordHistory, value))
                OnPropertyChanged(nameof(HasSelectedEntryPasswordHistory));
        }
    }
    public bool HasSelectedEntryPasswordHistory => SelectedEntryPasswordHistory.Count > 0;
    public bool HasSuggestions => SmartSuggestions.Count > 0;
    public int TrustScorePercent => (int)Math.Round((_securityInsightsService.CalculateChecklistCompletedCount(Settings) * 100.0) / 5, MidpointRounding.AwayFromZero);
    public string TrustScoreLabel => $"Trust score: {TrustScorePercent}/100";
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public AppSettings Settings { get => _settings; set => SetField(ref _settings, value); }

    // Navigation events
    public event Action<VaultEntryPlain?>? RequestOpenEntryEditor;
    public event Action? RequestOpenSettings;
    public event Action? RequestOpenTrust;
    public event Action? RequestOpenGenerator;
    public event Action? RequestOpenAudit;
    public event Action? RequestOpenImportExport;
    public event Action? RequestLock;

    public ICommand NewEntryCommand { get; }
    public ICommand EditEntryCommand { get; }
    public ICommand DeleteEntryCommand { get; }
    public ICommand RestoreEntryCommand { get; }
    public ICommand DeleteEntryPermanentlyCommand { get; }
    public ICommand CopyUsernameCommand { get; }
    public ICommand CopyPasswordCommand { get; }
    public ICommand CopyHistoryPasswordCommand { get; }
    public ICommand RestoreHistoryPasswordCommand { get; }
    public ICommand CopyTotpCodeCommand { get; }
    public ICommand CheckSelectedEntryBreachCommand { get; }
    public ICommand LockCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenTrustCommand { get; }
    public ICommand OpenGeneratorCommand { get; }
    public ICommand OpenAuditCommand { get; }
    public ICommand OpenImportExportCommand { get; }
    public ICommand OpenBrowserSetupCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }

    private List<VaultEntryPlain> _allEntries = new();
    private List<VaultEntryPlain> _deletedEntries = new();
    private readonly Dictionary<string, DateTime> _recentCaptureFingerprints = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _totpRefreshTimer;
    private static readonly TimeSpan RecentCaptureWindow = TimeSpan.FromSeconds(12);
    private const int PasswordHistoryLimit = 10;
    private const int MaxPersistedRemediationEntryIds = 256;

    public MainViewModel(
        VaultSessionService session,
        IVaultRepository vaultRepo,
        IFolderRepository folderRepo,
        SecureClipboardService clipboard,
        AutoLockService autoLock,
        BrowserCaptureService browserCapture,
        PasswordAuditService auditService,
        TotpCodeService totpCodeService,
        BreachCheckService breachCheckService,
        PasswordRiskAdvisorService passwordRiskAdvisorService,
        BrowserDomainService browserDomainService,
        SecurityInsightsService securityInsightsService)
    {
        _session = session;
        _vaultRepo = vaultRepo;
        _folderRepo = folderRepo;
        _clipboard = clipboard;
        _autoLock = autoLock;
        _browserCapture = browserCapture;
        _auditService = auditService;
        _totpCodeService = totpCodeService;
        _breachCheckService = breachCheckService;
        _passwordRiskAdvisorService = passwordRiskAdvisorService;
        _browserDomainService = browserDomainService;
        _securityInsightsService = securityInsightsService;

        NewEntryCommand = new RelayCommand(_ => RequestOpenEntryEditor?.Invoke(null));
        EditEntryCommand = new RelayCommand(_ => RequestOpenEntryEditor?.Invoke(SelectedEntry), _ => SelectedEntry != null && !SelectedEntry.IsDeleted);
        DeleteEntryCommand = new AsyncRelayCommand(async _ => await DeleteEntryAsync(), _ => SelectedEntry != null && !SelectedEntry.IsDeleted);
        RestoreEntryCommand = new AsyncRelayCommand(async _ => await RestoreEntryAsync(), _ => SelectedEntry != null && SelectedEntry.IsDeleted);
        DeleteEntryPermanentlyCommand = new AsyncRelayCommand(async _ => await DeleteEntryPermanentlyAsync(), _ => SelectedEntry != null && SelectedEntry.IsDeleted);
        CopyUsernameCommand = new RelayCommand(_ => CopyField(SelectedEntry?.Username), _ => SelectedEntry != null);
        CopyPasswordCommand = new RelayCommand(_ => CopyField(SelectedEntry?.Password), _ => SelectedEntry != null);
        CopyHistoryPasswordCommand = new RelayCommand(
            CopyHistoryPassword,
            parameter => parameter is PasswordHistoryItem && SelectedEntry != null && !SelectedEntry.IsDeleted);
        RestoreHistoryPasswordCommand = new AsyncRelayCommand(
            RestoreHistoryPasswordAsync,
            parameter => parameter is PasswordHistoryItem && SelectedEntry != null && !SelectedEntry.IsDeleted);
        CopyTotpCodeCommand = new RelayCommand(_ => CopyField(TotpCode), _ => HasTotpCode);
        CheckSelectedEntryBreachCommand = new AsyncRelayCommand(async _ => await CheckSelectedEntryBreachAsync(), _ => SelectedEntry != null && !SelectedEntry.IsDeleted && !IsEntryBreachCheckRunning);
        LockCommand = new RelayCommand(_ =>
        {
            _autoLock.Stop();
            _browserCapture.Stop();
            _session.Lock();
            RequestLock?.Invoke();
        });
        OpenSettingsCommand = new RelayCommand(_ => RequestOpenSettings?.Invoke());
        OpenTrustCommand = new RelayCommand(_ => RequestOpenTrust?.Invoke());
        OpenGeneratorCommand = new RelayCommand(_ => RequestOpenGenerator?.Invoke());
        OpenAuditCommand = new RelayCommand(_ => RequestOpenAudit?.Invoke());
        OpenImportExportCommand = new RelayCommand(_ => RequestOpenImportExport?.Invoke());
        OpenBrowserSetupCommand = new RelayCommand(_ => OpenBrowserSetup());
        ToggleFavoriteCommand = new AsyncRelayCommand(async _ => await ToggleFavoriteAsync(), _ => SelectedEntry != null && !SelectedEntry.IsDeleted);

        _totpRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _totpRefreshTimer.Tick += (_, _) => UpdateTotpState();
        _totpRefreshTimer.Start();

        _browserCapture.CredentialCaptured += OnCredentialCaptured;
        _browserCapture.AutofillQueryHandler = ResolveAutofillQueryAsync;

        _session.Locked += (_, _) => Application.Current.Dispatcher.Invoke(ClearTotpState);
        _session.Unlocked += (_, _) => Application.Current.Dispatcher.Invoke(UpdateTotpState);

        _autoLock.LockRequested += (_, _) => Application.Current.Dispatcher.Invoke(() =>
        {
            _browserCapture.Stop();
            _session.Lock();
            RequestLock?.Invoke();
        });
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var records = await _vaultRepo.GetAllEntriesAsync(includeDeleted: true);
            var allEntries = records.Select(r => _session.DecryptEntry(r)).ToList();
            _allEntries = allEntries.Where(e => !e.IsDeleted).ToList();
            _deletedEntries = allEntries.Where(e => e.IsDeleted).ToList();

            OnPropertyChanged(nameof(TotalEntriesCount));
            OnPropertyChanged(nameof(FavoriteEntriesCount));
            OnPropertyChanged(nameof(RecycleBinCount));

            var folders = await _folderRepo.GetAllFoldersAsync();
            Folders = new ObservableCollection<Folder>(folders);

            await ApplyFiltersAsync();

            await RefreshSettingsAsync();
            EnsureBrowserCaptureRunning();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync() => await LoadAsync();

    public async Task RefreshSettingsAsync()
    {
        var settingsRepo = App.Services!.GetRequiredService<ISettingsRepository>();
        Settings = await settingsRepo.GetSettingsAsync();
        Settings.RecycleBinRetentionDays = Math.Clamp(Settings.RecycleBinRetentionDays, 1, 3650);

        int purgedCount = await PurgeExpiredRecycleBinEntriesAsync();
        if (purgedCount > 0)
            await ApplyFiltersAsync();

        _autoLock.Start(Settings.AutoLockMinutes);
        UpdateOnboardingSummary();
        UpdateBackupConfidenceLabel();
        UpdateBrowserFlowLabel();
        OnPropertyChanged(nameof(SecurityStreakDays));
        OnPropertyChanged(nameof(CompletedChallengeCount));
        OnPropertyChanged(nameof(IsBreachCheckEnabled));
        if (!IsBreachCheckEnabled)
            SelectedEntryBreachStatus = string.Empty;
        await UpdateSecurityInsightsAsync(persistProgress: true);
    }
    private async Task ApplyFiltersAsync()
    {
        var source = ShowRecycleBinOnly ? _deletedEntries : _allEntries;
        var filtered = source.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(e => e.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || e.Username.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || e.Url.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (SelectedFolderId.HasValue)
            filtered = filtered.Where(e => e.FolderId == SelectedFolderId);

        if (ShowFavoritesOnly && !ShowRecycleBinOnly)
            filtered = filtered.Where(e => e.Favorite);

        var ordered = filtered.OrderBy(e => e.Title).ToList();
        Entries = new ObservableCollection<VaultEntryPlain>(ordered);

        if (SelectedEntry != null && ordered.All(e => e.Id != SelectedEntry.Id))
            SelectedEntry = null;

        UpdateTotpState();
        await Task.CompletedTask;
    }

    public async Task SaveEntryAsync(VaultEntryPlain entry, bool refresh = true)
    {
        entry.PasswordReminderDays = Math.Clamp(entry.PasswordReminderDays, 0, 3650);

        if (entry.Id != 0)
        {
            var existingRecord = await _vaultRepo.GetEntryByIdAsync(entry.Id);
            if (existingRecord != null)
            {
                var existingEntry = _session.DecryptEntry(existingRecord);
                entry.IsDeleted = existingEntry.IsDeleted;
                entry.DeletedAtUtc = existingEntry.DeletedAtUtc;
                if (entry.PasswordHistory.Count == 0 && existingEntry.PasswordHistory.Count > 0)
                    entry.PasswordHistory = ClonePasswordHistory(existingEntry.PasswordHistory);

                bool passwordChanged = !string.Equals(existingEntry.Password, entry.Password, StringComparison.Ordinal);
                if (passwordChanged && !string.IsNullOrWhiteSpace(existingEntry.Password))
                {
                    bool alreadyTracked = entry.PasswordHistory.Any(h =>
                        string.Equals(h.Password, existingEntry.Password, StringComparison.Ordinal));

                    if (!alreadyTracked)
                    {
                        entry.PasswordHistory.Insert(0, new PasswordHistoryItem
                        {
                            Password = existingEntry.Password,
                            ChangedAtUtc = DateTime.UtcNow
                        });
                    }
                }

                if (passwordChanged)
                    entry.PasswordLastChangedUtc = DateTime.UtcNow;
                else if (!entry.PasswordLastChangedUtc.HasValue)
                    entry.PasswordLastChangedUtc = existingEntry.PasswordLastChangedUtc;

                entry.PasswordHistory = entry.PasswordHistory
                    .Where(x => !string.IsNullOrWhiteSpace(x.Password))
                    .OrderByDescending(x => x.ChangedAtUtc)
                    .Take(PasswordHistoryLimit)
                    .Select(x => new PasswordHistoryItem
                    {
                        Password = x.Password,
                        ChangedAtUtc = x.ChangedAtUtc == default ? DateTime.UtcNow : x.ChangedAtUtc
                    })
                    .ToList();
            }
        }
        else if (!entry.PasswordLastChangedUtc.HasValue && !string.IsNullOrWhiteSpace(entry.Password))
        {
            entry.PasswordLastChangedUtc = DateTime.UtcNow;
        }

        var record = _session.EncryptEntry(entry);
        record.IsDeleted = entry.IsDeleted;
        record.DeletedAtUtc = entry.DeletedAtUtc;
        if (entry.Id == 0)
        {
            entry.Id = await _vaultRepo.InsertEntryAsync(record);
        }
        else
        {
            await _vaultRepo.UpdateEntryAsync(record);
        }

        if (refresh)
            await LoadAsync();
    }

    private static List<PasswordHistoryItem> ClonePasswordHistory(IEnumerable<PasswordHistoryItem> source)
    {
        return source.Select(x => new PasswordHistoryItem
        {
            Password = x.Password,
            ChangedAtUtc = x.ChangedAtUtc
        }).ToList();
    }


    private void RefreshSelectedEntryPasswordHistory()
    {
        if (SelectedEntry == null)
        {
            SelectedEntryPasswordHistory = new ObservableCollection<PasswordHistoryItem>();
            return;
        }

        var history = SelectedEntry.PasswordHistory
            .Where(item => !string.IsNullOrWhiteSpace(item.Password))
            .Where(item => !string.Equals(item.Password, SelectedEntry.Password, StringComparison.Ordinal))
            .OrderByDescending(item => item.ChangedAtUtc)
            .Select(item => new PasswordHistoryItem
            {
                Password = item.Password,
                ChangedAtUtc = item.ChangedAtUtc
            })
            .ToList();

        SelectedEntryPasswordHistory = new ObservableCollection<PasswordHistoryItem>(history);
    }

    private void CopyHistoryPassword(object? parameter)
    {
        if (SelectedEntry == null || SelectedEntry.IsDeleted) return;
        if (parameter is not PasswordHistoryItem item) return;
        if (string.IsNullOrWhiteSpace(item.Password)) return;

        CopyField(item.Password);
        StatusText = $"Copied a previous password for {SelectedEntry.Title}.";
    }

    private async Task RestoreHistoryPasswordAsync(object? parameter)
    {
        if (SelectedEntry == null || SelectedEntry.IsDeleted) return;
        if (parameter is not PasswordHistoryItem item) return;
        if (string.IsNullOrWhiteSpace(item.Password)) return;

        if (string.Equals(item.Password, SelectedEntry.Password, StringComparison.Ordinal))
        {
            StatusText = "That password is already active for this entry.";
            return;
        }

        var warnings = BuildRestoreRiskWarnings(item.Password, SelectedEntry.Id);
        string confirmation = warnings.Count == 0
            ? $"Restore this previous password for '{SelectedEntry.Title}'?"
            : $"This password has risk indicators:\n\n- {string.Join("\n- ", warnings)}\n\nRestore anyway?";

        var result = MessageBox.Show(
            confirmation,
            "Restore Password",
            MessageBoxButton.YesNo,
            warnings.Count == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        int entryId = SelectedEntry.Id;
        string entryTitle = SelectedEntry.Title;

        SelectedEntry.Password = item.Password;
        SelectedEntry.UpdatedAt = DateTime.UtcNow;

        await SaveEntryAsync(SelectedEntry, refresh: true);

        var reloaded = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (reloaded != null)
            SelectedEntry = reloaded;
        else
            RefreshSelectedEntryPasswordHistory();

        StatusText = $"Password restored from history for {entryTitle}.";
        _autoLock.RecordActivity();
    }

    private List<string> BuildRestoreRiskWarnings(string candidatePassword, int currentEntryId)
    {
        var otherPasswords = _allEntries
            .Where(e => e.Id != currentEntryId)
            .Where(e => !string.IsNullOrWhiteSpace(e.Password))
            .Select(e => e.Password);

        return _passwordRiskAdvisorService.EvaluateWarnings(candidatePassword, otherPasswords);
    }

    private void UpdateSelectedEntryPasswordReminderStatus()
    {
        if (SelectedEntry == null || SelectedEntry.IsDeleted)
        {
            SelectedEntryPasswordReminderStatus = string.Empty;
            IsSelectedEntryPasswordReminderWarning = false;
            return;
        }

        int reminderDays = Math.Clamp(SelectedEntry.PasswordReminderDays, 0, 3650);
        if (reminderDays == 0)
        {
            SelectedEntryPasswordReminderStatus = "Rotation reminder is off.";
            IsSelectedEntryPasswordReminderWarning = false;
            return;
        }

        DateTime lastChangedUtc = ResolvePasswordLastChangedUtc(SelectedEntry);
        int daysSinceChange = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - lastChangedUtc).TotalDays));
        int daysRemaining = reminderDays - daysSinceChange;

        if (daysRemaining < 0)
        {
            SelectedEntryPasswordReminderStatus = $"Rotation overdue by {-daysRemaining} day(s).";
            IsSelectedEntryPasswordReminderWarning = true;
        }
        else if (daysRemaining <= 7)
        {
            SelectedEntryPasswordReminderStatus = $"Rotation due in {daysRemaining} day(s).";
            IsSelectedEntryPasswordReminderWarning = true;
        }
        else
        {
            SelectedEntryPasswordReminderStatus = $"Rotation due in {daysRemaining} day(s).";
            IsSelectedEntryPasswordReminderWarning = false;
        }
    }

    private static DateTime ResolvePasswordLastChangedUtc(VaultEntryPlain entry)
    {
        if (entry.PasswordLastChangedUtc.HasValue && entry.PasswordLastChangedUtc.Value != default)
            return EnsureUtc(entry.PasswordLastChangedUtc.Value);

        DateTime fallback = entry.UpdatedAt == default ? DateTime.UtcNow : entry.UpdatedAt;
        return EnsureUtc(fallback);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private int CountPasswordsDueForRotation(int withinDays)
    {
        return _allEntries.Count(entry =>
        {
            if (string.IsNullOrWhiteSpace(entry.Password))
                return false;

            int reminderDays = Math.Clamp(entry.PasswordReminderDays, 0, 3650);
            if (reminderDays == 0)
                return false;

            DateTime lastChangedUtc = ResolvePasswordLastChangedUtc(entry);
            int daysSinceChange = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - lastChangedUtc).TotalDays));
            int daysRemaining = reminderDays - daysSinceChange;
            return daysRemaining <= withinDays;
        });
    }

    private async Task<int> PurgeExpiredRecycleBinEntriesAsync()
    {
        if (_deletedEntries.Count == 0)
            return 0;

        int retentionDays = Math.Clamp(Settings.RecycleBinRetentionDays, 1, 3650);
        var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);

        var expiredIds = _deletedEntries
            .Where(e => e.DeletedAtUtc.HasValue && e.DeletedAtUtc.Value <= cutoffUtc)
            .Select(e => e.Id)
            .ToList();

        if (expiredIds.Count == 0)
            return 0;

        foreach (int id in expiredIds)
            await _vaultRepo.DeleteEntryPermanentlyAsync(id);

        var expiredSet = expiredIds.ToHashSet();
        _deletedEntries = _deletedEntries.Where(e => !expiredSet.Contains(e.Id)).ToList();

        OnPropertyChanged(nameof(RecycleBinCount));
        if (SelectedEntry != null && expiredSet.Contains(SelectedEntry.Id))
            SelectedEntry = null;

        StatusText = $"Auto-purged {expiredIds.Count} recycle-bin entr{(expiredIds.Count == 1 ? "y" : "ies")} older than {retentionDays} day(s).";
        return expiredIds.Count;
    }
    private async Task DeleteEntryAsync()
    {
        if (SelectedEntry == null || SelectedEntry.IsDeleted) return;
        await _vaultRepo.DeleteEntryAsync(SelectedEntry.Id);
        await LoadAsync();
    }

    private async Task RestoreEntryAsync()
    {
        if (SelectedEntry == null || !SelectedEntry.IsDeleted) return;
        await _vaultRepo.RestoreEntryAsync(SelectedEntry.Id);
        await LoadAsync();
    }

    private async Task DeleteEntryPermanentlyAsync()
    {
        if (SelectedEntry == null || !SelectedEntry.IsDeleted) return;
        await _vaultRepo.DeleteEntryPermanentlyAsync(SelectedEntry.Id);
        await LoadAsync();
    }

    private async Task ToggleFavoriteAsync()
    {
        if (SelectedEntry == null || SelectedEntry.IsDeleted) return;
        SelectedEntry.Favorite = !SelectedEntry.Favorite;
        var record = _session.EncryptEntry(SelectedEntry);
        await _vaultRepo.UpdateEntryAsync(record);
        await LoadAsync();
    }

    private void CopyField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _clipboard.CopyAndScheduleClear(value, Settings.ClipboardClearSeconds);
        _autoLock.RecordActivity();
    }

    private async Task CheckSelectedEntryBreachAsync()
    {
        if (SelectedEntry == null || SelectedEntry.IsDeleted)
            return;

        if (!IsBreachCheckEnabled)
        {
            SelectedEntryBreachStatus = "Breach check is disabled. Enable it in Settings first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedEntry.Password))
        {
            SelectedEntryBreachStatus = "No password stored for this entry.";
            return;
        }

        IsEntryBreachCheckRunning = true;
        SelectedEntryBreachStatus = "Checking breach datasets...";

        try
        {
            int count = await _breachCheckService.CheckPasswordAsync(SelectedEntry.Password);
            SelectedEntryBreachStatus = count > 0
                ? $"Compromised: found {count:N0} time(s) in breach datasets."
                : "No breach match found for this password.";
            _autoLock.RecordActivity();
        }
        catch (Exception ex)
        {
            SelectedEntryBreachStatus = $"Breach check failed: {ex.Message}";
        }
        finally
        {
            IsEntryBreachCheckRunning = false;
        }
    }
    private void UpdateTotpState()
    {
        string? secret = SelectedEntry?.TotpSecret;
        HasTotpForSelectedEntry = !string.IsNullOrWhiteSpace(secret);

        if (!HasTotpForSelectedEntry || !_session.IsUnlocked)
        {
            ClearTotpState();
            return;
        }

        var result = _totpCodeService.GetCurrentCode(secret);
        if (!result.IsValid)
        {
            TotpCode = string.Empty;
            TotpSecondsRemaining = 0;
            TotpStatusText = result.ErrorMessage;
            return;
        }

        TotpCode = result.Code;
        TotpSecondsRemaining = result.SecondsRemaining;
        TotpStatusText = string.Empty;
    }

    private void ClearTotpState()
    {
        TotpCode = string.Empty;
        TotpSecondsRemaining = 0;
        TotpStatusText = string.Empty;
    }
    public void RecordUserActivity() => _autoLock.RecordActivity();

    public async Task RecordRemediationQueueCompletedAsync(int clearedItems)
    {
        if (clearedItems <= 0)
            return;

        StatusText = $"Queue completed. Cleared {clearedItems} item(s).";

        try
        {
            var today = DateTime.UtcNow.Date;
            if (Settings.LastChallengeCompletedUtc?.Date != today)
            {
                Settings.CompletedChallengeCount += 1;
                Settings.LastChallengeCompletedUtc = DateTime.UtcNow;

                var settingsRepo = App.Services!.GetRequiredService<ISettingsRepository>();
                await settingsRepo.SaveSettingsAsync(Settings);

                OnPropertyChanged(nameof(CompletedChallengeCount));
                UpdateBadge();
            }
        }
        catch
        {
            // Queue completion badge update is best-effort.
        }

        _autoLock.RecordActivity();
    }

    public async Task PersistRemediationQueueStateAsync(IEnumerable<int> dismissedEntryIds, IEnumerable<int> queueOrderEntryIds)
    {
        await _remediationStateSaveLock.WaitAsync();
        try
        {
            string dismissedIds = SerializeEntryIds(dismissedEntryIds, preserveOrder: false);
            string queueOrderIds = SerializeEntryIds(queueOrderEntryIds, preserveOrder: true);

            if (string.Equals(Settings.RemediationDismissedEntryIds, dismissedIds, StringComparison.Ordinal)
                && string.Equals(Settings.RemediationQueueOrderEntryIds, queueOrderIds, StringComparison.Ordinal))
            {
                return;
            }

            Settings.RemediationDismissedEntryIds = dismissedIds;
            Settings.RemediationQueueOrderEntryIds = queueOrderIds;

            try
            {
                var settingsRepo = App.Services!.GetRequiredService<ISettingsRepository>();
                await settingsRepo.SaveSettingsAsync(Settings);
            }
            catch
            {
                // Queue decision persistence is best-effort.
            }
        }
        finally
        {
            _remediationStateSaveLock.Release();
        }
    }

    public List<VaultEntryPlain> GetAllDecryptedEntries(bool includeDeleted = false)
    {
        if (!includeDeleted)
            return _allEntries;

        return _allEntries.Concat(_deletedEntries).ToList();
    }

    public bool OpenEntryEditorById(int entryId, bool returnToAudit = false)
    {
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId && !e.IsDeleted);
        if (entry == null)
            return false;

        _returnToAuditAfterEditor = returnToAudit;
        SelectedEntry = entry;
        RequestOpenEntryEditor?.Invoke(entry);
        return true;
    }

    public bool ConsumeEntryEditorReturnToAudit()
    {
        bool shouldReturn = _returnToAuditAfterEditor;
        _returnToAuditAfterEditor = false;
        return shouldReturn;
    }

    public void TriggerQuickSave()
    {
        if (!_session.IsUnlocked) return;
        _returnToAuditAfterEditor = false;
        StatusText = "Quick Save opened. Add your credential and save.";
        RequestOpenEntryEditor?.Invoke(null);
    }
}






















