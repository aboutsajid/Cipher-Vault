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


public class BreachScanResult
{
    public int EntryId { get; set; }
    public string EntryTitle { get; set; } = string.Empty;
    public int BreachCount { get; set; }
    public string StatusLabel => BreachCount > 0
        ? $"Found in breaches {BreachCount:N0} time(s)."
        : "No breach match.";
}

public class AuditViewModel : ViewModelBase
{
    private readonly PasswordAuditService _auditService;
    private readonly BreachCheckService _breachCheckService;
    private readonly DuplicateAccountService _duplicateAccountService;
    private readonly PasswordGeneratorService _passwordGeneratorService;
    private readonly RemediationQueueService _remediationQueueService;
    private readonly IVaultRepository _vaultRepo;
    private readonly IAuditDialogService _dialogService;
    private readonly IAuditWorkflowHost _mainVm;

    private ObservableCollection<AuditResult> _results = new();
    private ObservableCollection<BreachScanResult> _breachResults = new();
    private ObservableCollection<DuplicateAccountGroupResult> _duplicateGroups = new();
    private bool _isBusy;
    private string _summary = string.Empty;
    private string _breachSummary = string.Empty;
    private string _duplicateSummary = string.Empty;
    private int _breachCheckedCount;
    private int _breachTotalCount;
    private ObservableCollection<RemediationQueueItem> _remediationQueue = new();
    private RemediationQueueItem? _activeRemediationItem;
    private string _remediationQueueSummary = "Run Full Scan to build your fix queue.";
    private string _queueGuidance = "Run Full Scan to prioritize risky entries by impact.";
    private string _queueCompletionMessage = string.Empty;
    private string _scanStatusLabel = "Ready. Run Full Scan to refresh risk signals.";
    private bool _isBreachScanRunning;
    private CancellationTokenSource? _breachScanCts;
    private readonly HashSet<int> _dismissedRemediationEntryIds = new();
    private List<int> _preferredRemediationQueueOrderEntryIds = new();
    private ObservableCollection<RemediationQueueItem> _dismissedRemediationItems = new();
    private readonly SemaphoreSlim _remediationStatePersistLock = new(1, 1);
    private readonly List<QueueUndoSnapshot> _queueUndoHistory = new();
    private const int MaxPersistedRemediationEntryIds = 256;
    private const int MaxUndoHistory = 20;

    private sealed class QueueUndoSnapshot
    {
        public string ActionLabel { get; }
        public List<int> DismissedEntryIds { get; }
        public List<int> QueueOrderEntryIds { get; }
        public int? ActiveEntryId { get; }

        public QueueUndoSnapshot(string actionLabel, IEnumerable<int> dismissedEntryIds, IEnumerable<int> queueOrderEntryIds, int? activeEntryId)
        {
            ActionLabel = actionLabel;
            DismissedEntryIds = dismissedEntryIds.ToList();
            QueueOrderEntryIds = queueOrderEntryIds.ToList();
            ActiveEntryId = activeEntryId;
        }
    }

    public ObservableCollection<AuditResult> Results { get => _results; set => SetField(ref _results, value); }
    public ObservableCollection<BreachScanResult> BreachResults { get => _breachResults; set => SetField(ref _breachResults, value); }
    public ObservableCollection<DuplicateAccountGroupResult> DuplicateGroups { get => _duplicateGroups; set => SetField(ref _duplicateGroups, value); }
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public string Summary { get => _summary; set => SetField(ref _summary, value); }
    public string BreachSummary { get => _breachSummary; set => SetField(ref _breachSummary, value); }
    public string DuplicateSummary { get => _duplicateSummary; set => SetField(ref _duplicateSummary, value); }
    public ObservableCollection<RemediationQueueItem> RemediationQueue
    {
        get => _remediationQueue;
        set
        {
            if (SetField(ref _remediationQueue, value))
            {
                OnPropertyChanged(nameof(RemediationQueueCount));
                OnPropertyChanged(nameof(HasActiveRemediationItem));
            }
        }
    }
    public RemediationQueueItem? ActiveRemediationItem
    {
        get => _activeRemediationItem;
        set
        {
            if (SetField(ref _activeRemediationItem, value))
            {
                OnPropertyChanged(nameof(HasActiveRemediationItem));
                OnPropertyChanged(nameof(ActiveRemediationLabel));
            }
        }
    }
    public ObservableCollection<RemediationQueueItem> DismissedRemediationItems
    {
        get => _dismissedRemediationItems;
        private set
        {
            if (SetField(ref _dismissedRemediationItems, value))
            {
                OnPropertyChanged(nameof(DismissedRemediationCount));
                OnPropertyChanged(nameof(HasDismissedRemediationItems));
                OnPropertyChanged(nameof(DismissedRemediationSummary));
                OnPropertyChanged(nameof(CanResetRemediationQueue));
            }
        }
    }
    public int RemediationQueueCount => RemediationQueue.Count;
    public bool HasActiveRemediationItem => ActiveRemediationItem != null;
    public int DismissedRemediationCount => DismissedRemediationItems.Count;
    public bool HasDismissedRemediationItems => DismissedRemediationItems.Count > 0;
    public bool CanResetRemediationQueue => _dismissedRemediationEntryIds.Count > 0 || _preferredRemediationQueueOrderEntryIds.Count > 0;
    public bool CanUndoRemediationQueueAction => _queueUndoHistory.Count > 0;
    public string UndoRemediationQueueActionLabel => CanUndoRemediationQueueAction
        ? $"Undo {_queueUndoHistory[^1].ActionLabel}"
        : "Undo";
    public string DismissedRemediationSummary => HasDismissedRemediationItems
        ? $"{DismissedRemediationCount} dismissed item(s) hidden from Fix Queue."
        : string.Empty;
    public string RemediationQueueSummary { get => _remediationQueueSummary; set => SetField(ref _remediationQueueSummary, value); }
    public string ActiveRemediationLabel => ActiveRemediationItem == null
        ? "No pending fix target."
        : $"Next: {ActiveRemediationItem.EntryTitle} ({ActiveRemediationItem.RiskLabel})";
    public string QueueCompletionMessage
    {
        get => _queueCompletionMessage;
        private set
        {
            if (SetField(ref _queueCompletionMessage, value))
                OnPropertyChanged(nameof(HasQueueCompletionMessage));
        }
    }
    public bool HasQueueCompletionMessage => !string.IsNullOrWhiteSpace(QueueCompletionMessage);
    public string QueueGuidance { get => _queueGuidance; private set => SetField(ref _queueGuidance, value); }
    public string ScanStatusLabel { get => _scanStatusLabel; private set => SetField(ref _scanStatusLabel, value); }
    public bool IsBreachScanRunning
    {
        get => _isBreachScanRunning;
        private set
        {
            if (SetField(ref _isBreachScanRunning, value))
            {
                OnPropertyChanged(nameof(BreachProgressLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public int BreachCheckedCount
    {
        get => _breachCheckedCount;
        private set
        {
            if (SetField(ref _breachCheckedCount, value))
            {
                OnPropertyChanged(nameof(BreachProgressLabel));
                OnPropertyChanged(nameof(BreachProgressValue));
            }
        }
    }
    public int BreachTotalCount
    {
        get => _breachTotalCount;
        private set
        {
            if (SetField(ref _breachTotalCount, value))
            {
                OnPropertyChanged(nameof(BreachProgressLabel));
                OnPropertyChanged(nameof(BreachProgressMaximum));
            }
        }
    }
    public int BreachProgressValue => Math.Clamp(BreachCheckedCount, 0, BreachProgressMaximum);
    public int BreachProgressMaximum => Math.Max(1, BreachTotalCount);
    public string BreachProgressLabel => (IsBreachScanRunning || (BreachCheckedCount > 0 && BreachCheckedCount < BreachTotalCount)) && BreachTotalCount > 0
        ? $"Checked {BreachCheckedCount}/{BreachTotalCount} entries..."
        : string.Empty;

    public ICommand RunFullScanCommand { get; }
    public ICommand RunAuditCommand { get; }
    public ICommand RunBreachScanCommand { get; }
    public ICommand RunDuplicateScanCommand { get; }
    public ICommand CancelBreachScanCommand { get; }
    public ICommand MergeDuplicateGroupCommand { get; }
    public ICommand OpenEntryEditorCommand { get; }
    public ICommand FixNextWeakCommand { get; }
    public ICommand FixNextBreachedCommand { get; }
    public ICommand FixNextDuplicateCommand { get; }
    public ICommand OpenActiveRemediationItemCommand { get; }
    public ICommand MarkActiveRemediationDoneCommand { get; }
    public ICommand SkipActiveRemediationItemCommand { get; }
    public ICommand ResetRemediationQueueCommand { get; }
    public ICommand UndoRemediationQueueActionCommand { get; }
    public ICommand RestoreDismissedRemediationItemCommand { get; }
    public ICommand AutoSecureActiveRemediationItemCommand { get; }

    public AuditViewModel(
        PasswordAuditService auditService,
        BreachCheckService breachCheckService,
        DuplicateAccountService duplicateAccountService,
        PasswordGeneratorService passwordGeneratorService,
        RemediationQueueService remediationQueueService,
        IVaultRepository vaultRepo,
        IAuditDialogService dialogService,
        IAuditWorkflowHost mainVm)
    {
        _auditService = auditService;
        _breachCheckService = breachCheckService;
        _duplicateAccountService = duplicateAccountService;
        _passwordGeneratorService = passwordGeneratorService;
        _remediationQueueService = remediationQueueService;
        _vaultRepo = vaultRepo;
        _dialogService = dialogService;
        _mainVm = mainVm;
        RunFullScanCommand = new AsyncRelayCommand(async _ => await RunFullScanAsync(), _ => !IsBusy);
        RunAuditCommand = new AsyncRelayCommand(async _ => await RunAuditAsync(), _ => !IsBusy);
        RunBreachScanCommand = new AsyncRelayCommand(async _ => await RunBreachScanAsync(), _ => !IsBusy);
        RunDuplicateScanCommand = new AsyncRelayCommand(async _ => await RunDuplicateScanAsync(), _ => !IsBusy);
        CancelBreachScanCommand = new RelayCommand(_ => CancelBreachScan(), _ => IsBreachScanRunning);

        MergeDuplicateGroupCommand = new AsyncRelayCommand(
            MergeDuplicateGroupAsync,
            parameter => !IsBusy && parameter is DuplicateAccountGroupResult g && g.Entries.Count > 1);
        OpenEntryEditorCommand = new RelayCommand(
            OpenEntryEditor,
            parameter => !IsBusy && TryGetEntryId(parameter).HasValue);
        FixNextWeakCommand = new RelayCommand(
            _ => OpenFirstWeakIssue(),
            _ => !IsBusy && Results.Count > 0);
        FixNextBreachedCommand = new RelayCommand(
            _ => OpenFirstBreachedIssue(),
            _ => !IsBusy && BreachResults.Count > 0);
        FixNextDuplicateCommand = new RelayCommand(
            _ => OpenFirstDuplicateIssue(),
            _ => !IsBusy && DuplicateGroups.Any(g => g.Entries.Count > 0));
        OpenActiveRemediationItemCommand = new RelayCommand(
            _ => OpenActiveRemediationItem(),
            _ => !IsBusy && ActiveRemediationItem != null);
        MarkActiveRemediationDoneCommand = new AsyncRelayCommand(
            async _ => await MarkActiveRemediationDoneAsync(),
            _ => !IsBusy && ActiveRemediationItem != null);
        SkipActiveRemediationItemCommand = new AsyncRelayCommand(
            async _ => await SkipActiveRemediationItemAsync(),
            _ => !IsBusy && RemediationQueue.Count > 1);
        ResetRemediationQueueCommand = new AsyncRelayCommand(
            async _ => await ResetRemediationQueueStateAsync(),
            _ => !IsBusy && CanResetRemediationQueue);
        UndoRemediationQueueActionCommand = new AsyncRelayCommand(
            async _ => await UndoRemediationQueueActionAsync(),
            _ => !IsBusy && CanUndoRemediationQueueAction);
        RestoreDismissedRemediationItemCommand = new AsyncRelayCommand(
            RestoreDismissedRemediationItemAsync,
            parameter => !IsBusy && HasDismissedRemediationItems && TryGetEntryId(parameter).HasValue);
        AutoSecureActiveRemediationItemCommand = new AsyncRelayCommand(
            async _ => await AutoSecureActiveRemediationItemAsync(),
            _ => !IsBusy && ActiveRemediationItem != null);

        LoadPersistedRemediationState();
    }

    private async Task RunFullScanAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ScanStatusLabel = "Running full scan...";
        try
        {
            ScanStatusLabel = "Running local password audit...";
            await RunAuditCoreSafeAsync();

            ScanStatusLabel = "Running breach scan...";
            await RunBreachScanCoreSafeAsync();

            ScanStatusLabel = "Scanning duplicate accounts...";
            await RunDuplicateScanCoreSafeAsync();

            ScanStatusLabel = "Full scan complete.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(BreachProgressLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public Task RunFullScanForRemediationAsync()
        => RunFullScanAsync();

    private async Task RunAuditAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ScanStatusLabel = "Running local password audit...";
        try
        {
            await RunAuditCoreSafeAsync();
            if (!Summary.Contains("failed", StringComparison.OrdinalIgnoreCase))
                ScanStatusLabel = "Audit complete.";
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RunBreachScanAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ScanStatusLabel = "Running breach scan...";
        try
        {
            await RunBreachScanCoreSafeAsync();
            bool completed = BreachSummary.Contains("No breached passwords found", StringComparison.OrdinalIgnoreCase)
                || BreachSummary.Contains("matched breach datasets", StringComparison.OrdinalIgnoreCase);
            if (!IsBreachScanRunning && completed)
                ScanStatusLabel = "Breach scan complete.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(BreachProgressLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CancelBreachScan()
    {
        if (!IsBreachScanRunning)
            return;

        ScanStatusLabel = "Stopping breach scan...";
        _breachScanCts?.Cancel();
    }

    private async Task RunDuplicateScanAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ScanStatusLabel = "Scanning duplicate accounts...";
        try
        {
            await RunDuplicateScanCoreSafeAsync();
            if (!DuplicateSummary.Contains("failed", StringComparison.OrdinalIgnoreCase))
                ScanStatusLabel = "Duplicate scan complete.";
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RunAuditCoreSafeAsync()
    {
        try
        {
            await RunAuditCoreAsync();
        }
        catch (Exception ex)
        {
            Results = new ObservableCollection<AuditResult>();
            Summary = $"Audit failed: {ex.Message}";
            ScanStatusLabel = "Audit failed.";
            await BuildRemediationQueueFromCurrentDataAsync();
        }
    }

    private async Task RunAuditCoreAsync()
    {
        var entries = _mainVm.GetAllDecryptedEntries();
        var results = await Task.Run(() => _auditService.Audit(entries));
        Results = new ObservableCollection<AuditResult>(results);
        Summary = results.Count == 0
            ? "All passwords look good."
            : $"{results.Count} entries have issues.";
        await BuildRemediationQueueFromCurrentDataAsync();
    }

    private async Task RunBreachScanCoreSafeAsync()
    {
        _breachScanCts?.Dispose();
        _breachScanCts = new CancellationTokenSource();
        var cancellationToken = _breachScanCts.Token;
        IsBreachScanRunning = true;

        try
        {
            await RunBreachScanCoreAsync(cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            BreachResults = new ObservableCollection<BreachScanResult>();
            BreachSummary = "Breach scan paused by rate limits (HTTP 429). Wait about a minute and retry.";
            ScanStatusLabel = "Breach scan rate-limited.";
            await BuildRemediationQueueFromCurrentDataAsync();
        }
        catch (HttpRequestException ex)
        {
            BreachResults = new ObservableCollection<BreachScanResult>();
            BreachSummary = $"Breach scan failed due to network/server issues: {ex.Message}";
            ScanStatusLabel = "Breach scan network error.";
            await BuildRemediationQueueFromCurrentDataAsync();
        }
        catch (Exception ex)
        {
            BreachResults = new ObservableCollection<BreachScanResult>();
            BreachSummary = $"Breach scan failed: {ex.Message}";
            ScanStatusLabel = "Breach scan failed.";
            await BuildRemediationQueueFromCurrentDataAsync();
        }
        finally
        {
            IsBreachScanRunning = false;
            _breachScanCts?.Dispose();
            _breachScanCts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RunBreachScanCoreAsync(CancellationToken cancellationToken)
    {
        if (!_mainVm.Settings.AllowBreachCheck)
        {
            BreachResults = new ObservableCollection<BreachScanResult>();
            BreachSummary = "Breach check is disabled. Enable it in Settings first.";
            BreachCheckedCount = 0;
            BreachTotalCount = 0;
            ScanStatusLabel = "Breach scan skipped (disabled in settings).";
            await BuildRemediationQueueFromCurrentDataAsync();
            return;
        }

        var entries = _mainVm.GetAllDecryptedEntries()
            .Where(e => !string.IsNullOrWhiteSpace(e.Password))
            .ToList();

        if (entries.Count == 0)
        {
            BreachResults = new ObservableCollection<BreachScanResult>();
            BreachSummary = "No entries with passwords to scan.";
            BreachCheckedCount = 0;
            BreachTotalCount = 0;
            ScanStatusLabel = "Breach scan skipped (no passwords).";
            await BuildRemediationQueueFromCurrentDataAsync();
            return;
        }

        BreachSummary = "Running breach scan...";
        BreachCheckedCount = 0;
        BreachTotalCount = entries.Count;

        var passwordCache = new Dictionary<string, int>(StringComparer.Ordinal);
        var matches = new List<BreachScanResult>();
        bool canceled = false;

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            int breachCount;
            if (!passwordCache.TryGetValue(entry.Password, out breachCount))
            {
                try
                {
                    breachCount = await CheckPasswordWithRetryAsync(entry.Password, cancellationToken);
                    passwordCache[entry.Password] = breachCount;
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    break;
                }
            }

            if (breachCount > 0)
            {
                matches.Add(new BreachScanResult
                {
                    EntryId = entry.Id,
                    EntryTitle = entry.Title,
                    BreachCount = breachCount
                });
            }

            BreachCheckedCount += 1;
        }

        BreachResults = new ObservableCollection<BreachScanResult>(
            matches.OrderByDescending(x => x.BreachCount).ThenBy(x => x.EntryTitle));

        if (canceled)
        {
            BreachSummary = $"Breach scan canceled after checking {BreachCheckedCount}/{BreachTotalCount} entries.";
            ScanStatusLabel = "Breach scan canceled.";
        }
        else
        {
            BreachSummary = matches.Count == 0
                ? $"No breached passwords found across {entries.Count} entries."
                : $"{matches.Count}/{entries.Count} entries matched breach datasets.";
            ScanStatusLabel = "Breach scan complete.";
        }

        await BuildRemediationQueueFromCurrentDataAsync();
        _mainVm.RecordUserActivity();
    }

    private async Task<int> CheckPasswordWithRetryAsync(string password, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        TimeSpan delay = TimeSpan.FromMilliseconds(300);
        HttpRequestException? lastTransientFailure = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _breachCheckService.CheckPasswordAsync(password, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransientHttpFailure(ex))
            {
                lastTransientFailure = ex;
                ScanStatusLabel = $"Breach scan temporary network issue. Retrying ({attempt}/{maxAttempts - 1})...";
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1500));
            }
        }

        throw lastTransientFailure ?? new HttpRequestException("Breach scan failed after retries.");
    }

    private static bool IsTransientHttpFailure(HttpRequestException ex)
    {
        if (!ex.StatusCode.HasValue)
            return true;

        int code = (int)ex.StatusCode.Value;
        return code == 408 || code == 409 || code == 425 || code == 429 || code >= 500;
    }

    private async Task RunDuplicateScanCoreSafeAsync()
    {
        try
        {
            await RunDuplicateScanCoreAsync();
        }
        catch (Exception ex)
        {
            DuplicateGroups = new ObservableCollection<DuplicateAccountGroupResult>();
            DuplicateSummary = $"Duplicate scan failed: {ex.Message}";
            ScanStatusLabel = "Duplicate scan failed.";
            await BuildRemediationQueueFromCurrentDataAsync();
        }
    }

    private async Task RunDuplicateScanCoreAsync()
    {
        var groups = await Task.Run(() => _duplicateAccountService.FindDuplicateGroups(_mainVm.GetAllDecryptedEntries()));
        DuplicateGroups = new ObservableCollection<DuplicateAccountGroupResult>(groups);
        UpdateDuplicateSummary(groups);
        await BuildRemediationQueueFromCurrentDataAsync();
        _mainVm.RecordUserActivity();
    }

    private async Task MergeDuplicateGroupAsync(object? parameter)
    {
        if (parameter is not DuplicateAccountGroupResult group || group.Entries.Count < 2)
            return;

        if (!_dialogService.ConfirmDuplicateMerge(group.Username, group.Site, group.Entries.Count))
            return;

        IsBusy = true;
        try
        {
            var entryMap = _mainVm.GetAllDecryptedEntries().ToDictionary(e => e.Id);
            var selectedGroupEntries = group.Entries
                .Where(i => entryMap.ContainsKey(i.EntryId))
                .Select(i => entryMap[i.EntryId])
                .ToList();

            if (selectedGroupEntries.Count < 2)
            {
                DuplicateSummary = "Duplicate group changed before merge. Re-run duplicate scan.";
                return;
            }

            var mergeResult = _duplicateAccountService.MergeDuplicateGroup(selectedGroupEntries);

            await _mainVm.SaveEntryAsync(mergeResult.Keeper, refresh: false);
            foreach (int entryId in mergeResult.MergedEntryIds)
                await _vaultRepo.DeleteEntryAsync(entryId);

            await _mainVm.RefreshAsync();

            var refreshedGroups = _duplicateAccountService.FindDuplicateGroups(_mainVm.GetAllDecryptedEntries());
            DuplicateGroups = new ObservableCollection<DuplicateAccountGroupResult>(refreshedGroups);
            UpdateDuplicateSummary(refreshedGroups);
            await BuildRemediationQueueFromCurrentDataAsync();
            CommandManager.InvalidateRequerySuggested();

            string keeperTitle = string.IsNullOrWhiteSpace(mergeResult.Keeper.Title)
                ? "(Untitled)"
                : mergeResult.Keeper.Title;
            DuplicateSummary =
                $"Merged {selectedGroupEntries.Count} entries into '{keeperTitle}'. Extra entries moved to Recycle Bin.";

            _mainVm.RecordUserActivity();
        }
        catch (Exception ex)
        {
            DuplicateSummary = $"Duplicate merge failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BuildRemediationQueueFromCurrentDataAsync(int? preferredActiveId = null)
    {
        int previousCount = RemediationQueue.Count;
        int? previousActiveId = preferredActiveId ?? ActiveRemediationItem?.EntryId;

        var rawQueue = _remediationQueueService.BuildQueue(
            Results,
            BreachResults.Select(x => new RemediationBreachResult
            {
                EntryId = x.EntryId,
                EntryTitle = x.EntryTitle,
                BreachCount = x.BreachCount
            }),
            DuplicateGroups);

        bool persistedStateChanged = SynchronizePersistedRemediationState(rawQueue);
        UpdateDismissedRemediationItems(rawQueue);

        var queue = ApplyPersistedRemediationState(rawQueue);
        RemediationQueue = new ObservableCollection<RemediationQueueItem>(queue);
        ActiveRemediationItem = previousActiveId.HasValue
            ? RemediationQueue.FirstOrDefault(x => x.EntryId == previousActiveId.Value) ?? RemediationQueue.FirstOrDefault()
            : RemediationQueue.FirstOrDefault();

        UpdateRemediationQueueSummary();
        HandleQueueTransition(previousCount, RemediationQueue.Count);

        if (persistedStateChanged)
        {
            OnPersistedRemediationStateChanged();
            await PersistRemediationStateAsync();
        }
    }

    private void UpdateDismissedRemediationItems(IReadOnlyCollection<RemediationQueueItem> rawQueue)
    {
        var dismissedItems = rawQueue
            .Where(item => _dismissedRemediationEntryIds.Contains(item.EntryId))
            .ToList();

        DismissedRemediationItems = new ObservableCollection<RemediationQueueItem>(dismissedItems);
    }

    private void UpdateRemediationQueueSummary()
    {
        if (Results.Count == 0 && BreachResults.Count == 0 && DuplicateGroups.Count == 0)
        {
            RemediationQueueSummary = "Run Full Scan to build your fix queue.";
            QueueGuidance = "Run one full scan to generate prioritized fixes.";
            return;
        }

        if (RemediationQueue.Count == 0 || ActiveRemediationItem == null)
        {
            RemediationQueueSummary = "Fix Queue complete. No risky entries pending.";
            QueueGuidance = "Great job. Re-run scan after adding or editing credentials.";
            return;
        }

        int position = RemediationQueue.IndexOf(ActiveRemediationItem) + 1;
        RemediationQueueSummary =
            $"Fix Queue: {RemediationQueue.Count} pending. {position}/{RemediationQueue.Count} active.";
        QueueGuidance = ResolveQueueGuidance(ActiveRemediationItem);
    }

    private static string ResolveQueueGuidance(RemediationQueueItem activeItem)
    {
        if (activeItem.IsBreached)
            return "Highest priority: update breached passwords first.";

        if (activeItem.HasWeakIssues && activeItem.IsDuplicate)
            return "Strong impact: rotate this weak reused account and remove duplicates.";

        if (activeItem.HasWeakIssues)
            return "Rotate weak passwords using Auto Secure for a fast win.";

        if (activeItem.IsDuplicate)
            return "Merge or remove duplicate logins to reduce confusion and reuse risk.";

        return "Review this entry and resolve remaining risk signals.";
    }

    private void OpenActiveRemediationItem()
    {
        if (ActiveRemediationItem == null)
            return;

        OpenEntryEditor(ActiveRemediationItem.EntryId);
    }

    private async Task MarkActiveRemediationDoneAsync()
    {
        if (ActiveRemediationItem == null)
            return;

        PushQueueUndoSnapshot("Mark Done");

        int completedEntryId = ActiveRemediationItem.EntryId;
        _dismissedRemediationEntryIds.Add(completedEntryId);
        _preferredRemediationQueueOrderEntryIds.RemoveAll(id => id == completedEntryId);

        OnPersistedRemediationStateChanged();
        await PersistRemediationStateAsync();
        await BuildRemediationQueueFromCurrentDataAsync();
    }

    private async Task SkipActiveRemediationItemAsync()
    {
        if (RemediationQueue.Count <= 1)
            return;

        PushQueueUndoSnapshot("Skip");

        var reordered = _remediationQueueService.RotateQueue(RemediationQueue);
        _preferredRemediationQueueOrderEntryIds = reordered.Select(item => item.EntryId).ToList();

        OnPersistedRemediationStateChanged();
        await PersistRemediationStateAsync();

        int? preferredActiveId = _preferredRemediationQueueOrderEntryIds.FirstOrDefault();
        await BuildRemediationQueueFromCurrentDataAsync(preferredActiveId == 0 ? null : preferredActiveId);
    }

    private async Task ResetRemediationQueueStateAsync()
    {
        if (!CanResetRemediationQueue)
            return;

        _dismissedRemediationEntryIds.Clear();
        _preferredRemediationQueueOrderEntryIds.Clear();
        _queueUndoHistory.Clear();

        OnUndoHistoryChanged();
        OnPersistedRemediationStateChanged();

        await PersistRemediationStateAsync();
        await BuildRemediationQueueFromCurrentDataAsync();

        QueueCompletionMessage = "Fix Queue reset. Previously dismissed items are visible again.";
        _mainVm.StatusText = "Fix Queue reset.";
    }

    private async Task UndoRemediationQueueActionAsync()
    {
        if (!CanUndoRemediationQueueAction)
            return;

        QueueUndoSnapshot snapshot = _queueUndoHistory[^1];
        _queueUndoHistory.RemoveAt(_queueUndoHistory.Count - 1);

        _dismissedRemediationEntryIds.Clear();
        foreach (int entryId in snapshot.DismissedEntryIds)
            _dismissedRemediationEntryIds.Add(entryId);

        _preferredRemediationQueueOrderEntryIds = snapshot.QueueOrderEntryIds
            .Where(id => !_dismissedRemediationEntryIds.Contains(id))
            .Distinct()
            .ToList();

        OnUndoHistoryChanged();
        OnPersistedRemediationStateChanged();

        await PersistRemediationStateAsync();
        await BuildRemediationQueueFromCurrentDataAsync(snapshot.ActiveEntryId);

        QueueCompletionMessage = $"Undid {snapshot.ActionLabel}.";
        _mainVm.StatusText = $"Undid {snapshot.ActionLabel}.";
    }

    private async Task RestoreDismissedRemediationItemAsync(object? parameter)
    {
        int? entryId = TryGetEntryId(parameter);
        if (!entryId.HasValue)
            return;

        if (!_dismissedRemediationEntryIds.Remove(entryId.Value))
            return;

        string entryTitle = DismissedRemediationItems
            .FirstOrDefault(item => item.EntryId == entryId.Value)?.EntryTitle
            ?? "entry";

        OnPersistedRemediationStateChanged();

        await PersistRemediationStateAsync();
        await BuildRemediationQueueFromCurrentDataAsync(entryId.Value);

        QueueCompletionMessage = $"Restored '{entryTitle}' to Fix Queue.";
        _mainVm.StatusText = $"Restored: {entryTitle}";
    }

    private void PushQueueUndoSnapshot(string actionLabel)
    {
        var snapshot = new QueueUndoSnapshot(
            actionLabel,
            _dismissedRemediationEntryIds.OrderBy(id => id),
            _preferredRemediationQueueOrderEntryIds,
            ActiveRemediationItem?.EntryId);

        _queueUndoHistory.Add(snapshot);
        if (_queueUndoHistory.Count > MaxUndoHistory)
            _queueUndoHistory.RemoveAt(0);

        OnUndoHistoryChanged();
    }

    private void OnUndoHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndoRemediationQueueAction));
        OnPropertyChanged(nameof(UndoRemediationQueueActionLabel));
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadPersistedRemediationState()
    {
        _dismissedRemediationEntryIds.Clear();
        foreach (int entryId in ParsePersistedEntryIds(_mainVm.Settings.RemediationDismissedEntryIds))
            _dismissedRemediationEntryIds.Add(entryId);

        _preferredRemediationQueueOrderEntryIds = ParsePersistedEntryIds(_mainVm.Settings.RemediationQueueOrderEntryIds)
            .Where(id => !_dismissedRemediationEntryIds.Contains(id))
            .ToList();

        OnPersistedRemediationStateChanged();
        OnUndoHistoryChanged();
    }

    private List<RemediationQueueItem> ApplyPersistedRemediationState(List<RemediationQueueItem> rawQueue)
    {
        if (rawQueue.Count == 0)
            return rawQueue;

        var filteredQueue = rawQueue
            .Where(item => !_dismissedRemediationEntryIds.Contains(item.EntryId))
            .ToList();

        if (_preferredRemediationQueueOrderEntryIds.Count == 0 || filteredQueue.Count <= 1)
            return filteredQueue;

        var orderedQueue = new List<RemediationQueueItem>(filteredQueue.Count);
        var remainingById = filteredQueue.ToDictionary(item => item.EntryId);

        foreach (int entryId in _preferredRemediationQueueOrderEntryIds)
        {
            if (remainingById.Remove(entryId, out var prioritizedItem))
                orderedQueue.Add(prioritizedItem);
        }

        orderedQueue.AddRange(filteredQueue.Where(item => remainingById.ContainsKey(item.EntryId)));
        return orderedQueue;
    }

    private bool SynchronizePersistedRemediationState(IReadOnlyCollection<RemediationQueueItem> rawQueue)
    {
        var validEntryIds = rawQueue
            .Select(item => item.EntryId)
            .ToHashSet();

        bool changed = _dismissedRemediationEntryIds.RemoveWhere(id => !validEntryIds.Contains(id)) > 0;

        if (_dismissedRemediationEntryIds.Count > MaxPersistedRemediationEntryIds)
        {
            var keepDismissedIds = rawQueue
                .Select(item => item.EntryId)
                .Where(id => _dismissedRemediationEntryIds.Contains(id))
                .Take(MaxPersistedRemediationEntryIds)
                .ToHashSet();

            changed |= _dismissedRemediationEntryIds.RemoveWhere(id => !keepDismissedIds.Contains(id)) > 0;
        }

        var normalizedOrder = _preferredRemediationQueueOrderEntryIds
            .Where(id => validEntryIds.Contains(id) && !_dismissedRemediationEntryIds.Contains(id))
            .Distinct()
            .Take(MaxPersistedRemediationEntryIds)
            .ToList();

        if (!_preferredRemediationQueueOrderEntryIds.SequenceEqual(normalizedOrder))
        {
            _preferredRemediationQueueOrderEntryIds = normalizedOrder;
            changed = true;
        }

        return changed;
    }

    private void OnPersistedRemediationStateChanged()
    {
        OnPropertyChanged(nameof(CanResetRemediationQueue));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task PersistRemediationStateAsync()
    {
        await _remediationStatePersistLock.WaitAsync();
        try
        {
            await _mainVm.PersistRemediationQueueStateAsync(
                _dismissedRemediationEntryIds,
                _preferredRemediationQueueOrderEntryIds);
        }
        catch
        {
            // Remediation state persistence is best-effort.
        }
        finally
        {
            _remediationStatePersistLock.Release();
        }
    }

    private static List<int> ParsePersistedEntryIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<int>();

        var ids = new List<int>();
        var seen = new HashSet<int>();

        foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out int entryId) || entryId <= 0 || !seen.Add(entryId))
                continue;

            ids.Add(entryId);
            if (ids.Count >= MaxPersistedRemediationEntryIds)
                break;
        }

        return ids;
    }

    private async Task AutoSecureActiveRemediationItemAsync()
    {
        if (ActiveRemediationItem == null)
            return;

        int entryId = ActiveRemediationItem.EntryId;
        var entry = _mainVm.GetAllDecryptedEntries()
            .FirstOrDefault(e => e.Id == entryId && !e.IsDeleted);

        if (entry == null)
        {
            Summary = "Selected entry is no longer available. Run Full Scan to refresh.";
            await MarkActiveRemediationDoneAsync();
            return;
        }

        string entryTitle = string.IsNullOrWhiteSpace(entry.Title) ? "(Untitled)" : entry.Title;
        if (!_dialogService.ConfirmAutoSecure(entryTitle))
            return;

        IsBusy = true;
        ScanStatusLabel = "Applying Auto Secure...";
        try
        {
            string generatedPassword = GenerateAutoSecurePassword(entry.Id);
            var updatedEntry = CloneEntry(entry);
            updatedEntry.Password = generatedPassword;
            updatedEntry.UpdatedAt = DateTime.UtcNow;

            await _mainVm.SaveEntryAsync(updatedEntry, refresh: true);
            await RunAuditCoreSafeAsync();
            await RunBreachScanCoreSafeAsync();
            await RunDuplicateScanCoreSafeAsync();

            string successMessage = $"Auto-secured '{entryTitle}'. Update the password on the website/app now.";
            Summary = successMessage;
            QueueCompletionMessage = successMessage;
            ScanStatusLabel = "Auto Secure complete.";
            _mainVm.StatusText = $"Auto-secured: {entryTitle}";
            _mainVm.RecordUserActivity();
        }
        catch (Exception ex)
        {
            Summary = $"Auto-secure failed: {ex.Message}";
            ScanStatusLabel = "Auto Secure failed.";
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void HandleQueueTransition(int previousCount, int currentCount)
    {
        bool hasScanContext = Results.Count > 0 || BreachResults.Count > 0 || DuplicateGroups.Count > 0;

        if (!hasScanContext)
        {
            QueueCompletionMessage = string.Empty;
            return;
        }

        if (previousCount > 0 && currentCount == 0)
        {
            QueueCompletionMessage = $"Queue completed. Cleared {previousCount} item(s).";
            _ = _mainVm.RecordRemediationQueueCompletedAsync(previousCount);
            return;
        }

        if (currentCount > 0)
            QueueCompletionMessage = string.Empty;
    }

    private void OpenFirstWeakIssue()
    {
        if (Results.Count == 0)
        {
            Summary = "No weak entries left. Run Audit to refresh.";
            return;
        }

        OpenEntryEditor(Results[0].EntryId);
    }

    private void OpenFirstBreachedIssue()
    {
        if (BreachResults.Count == 0)
        {
            BreachSummary = "No breached entries left. Run Breach Scan to refresh.";
            return;
        }

        OpenEntryEditor(BreachResults[0].EntryId);
    }

    private void OpenFirstDuplicateIssue()
    {
        int? entryId = DuplicateGroups
            .SelectMany(g => g.Entries)
            .Select(e => (int?)e.EntryId)
            .FirstOrDefault();

        if (!entryId.HasValue)
        {
            DuplicateSummary = "No duplicate entries left. Run Duplicate Scan to refresh.";
            return;
        }

        OpenEntryEditor(entryId.Value);
    }
    private void OpenEntryEditor(object? parameter)
    {
        int? entryId = TryGetEntryId(parameter);
        if (!entryId.HasValue)
            return;

        bool opened = _mainVm.OpenEntryEditorById(entryId.Value, returnToAudit: true);
        if (!opened)
        {
            string missingMessage = "Selected entry is no longer available. Refresh and try again.";
            Summary = missingMessage;
            BreachSummary = missingMessage;
            DuplicateSummary = missingMessage;
        }
    }


    private string GenerateAutoSecurePassword(int currentEntryId)
    {
        var existingPasswords = _mainVm.GetAllDecryptedEntries()
            .Where(e => e.Id != currentEntryId)
            .Select(e => e.Password)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.Ordinal);

        var options = new PasswordGeneratorOptions
        {
            Length = 20,
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeNumbers = true,
            IncludeSymbols = true,
            ExcludeSimilarChars = false
        };

        string candidate = string.Empty;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            candidate = _passwordGeneratorService.Generate(options);
            if (!existingPasswords.Contains(candidate))
                return candidate;
        }

        return candidate;
    }

    private static VaultEntryPlain CloneEntry(VaultEntryPlain source)
    {
        return new VaultEntryPlain
        {
            Id = source.Id,
            Title = source.Title,
            Username = source.Username,
            Password = source.Password,
            Notes = source.Notes,
            Url = source.Url,
            Tags = source.Tags,
            TotpSecret = source.TotpSecret,
            FolderId = source.FolderId,
            Favorite = source.Favorite,
            PasswordHistory = source.PasswordHistory
                .Select(h => new PasswordHistoryItem
                {
                    Password = h.Password,
                    ChangedAtUtc = h.ChangedAtUtc
                })
                .ToList(),
            PasswordReminderDays = source.PasswordReminderDays,
            PasswordLastChangedUtc = source.PasswordLastChangedUtc,
            IsDeleted = source.IsDeleted,
            DeletedAtUtc = source.DeletedAtUtc,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
    private static int? TryGetEntryId(object? parameter)
    {
        return parameter switch
        {
            int value => value,
            string text when int.TryParse(text, out int parsed) => parsed,
            AuditResult audit => audit.EntryId,
            BreachScanResult breach => breach.EntryId,
            DuplicateAccountItem duplicate => duplicate.EntryId,
            RemediationQueueItem remediation => remediation.EntryId,
            _ => null
        };
    }

    private void UpdateDuplicateSummary(IReadOnlyCollection<DuplicateAccountGroupResult> groups)
    {
        if (groups.Count == 0)
        {
            DuplicateSummary = "No duplicate accounts found (same site + username).";
            return;
        }

        int duplicateEntries = groups.Sum(g => g.Entries.Count);
        DuplicateSummary =
            $"Found {duplicateEntries} entries across {groups.Count} duplicate account group(s).";
    }
}
