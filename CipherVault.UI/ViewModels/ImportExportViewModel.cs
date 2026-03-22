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


public class ImportExportViewModel : ViewModelBase
{
    private readonly BackupExportImportService _backupService;
    private readonly MainViewModel _mainVm;
    private readonly IFolderRepository _folderRepo;
    private readonly ISettingsRepository _settingsRepo;

    private string _exportPassword = string.Empty;
    private string _importPassword = string.Empty;
    private string _exportFilePath = string.Empty;
    private string _importFilePath = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _showCsvWarning;

    public string ExportPassword { get => _exportPassword; set => SetField(ref _exportPassword, value); }
    public string ImportPassword { get => _importPassword; set => SetField(ref _importPassword, value); }
    public string ExportFilePath { get => _exportFilePath; set => SetField(ref _exportFilePath, value); }
    public string ImportFilePath { get => _importFilePath; set { SetField(ref _importFilePath, value); ShowCsvWarning = value.EndsWith(".csv", StringComparison.OrdinalIgnoreCase); } }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public bool ShowCsvWarning { get => _showCsvWarning; set => SetField(ref _showCsvWarning, value); }

    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand BrowseExportCommand { get; }
    public ICommand BrowseImportCommand { get; }

    public ImportExportViewModel(
        BackupExportImportService backupService,
        MainViewModel mainVm,
        IFolderRepository folderRepo,
        ISettingsRepository settingsRepo)
    {
        _backupService = backupService;
        _mainVm = mainVm;
        _folderRepo = folderRepo;
        _settingsRepo = settingsRepo;

        ExportCommand = new AsyncRelayCommand(async _ => await ExportAsync(), _ => !IsBusy);
        ImportCommand = new AsyncRelayCommand(async _ => await ImportAsync(), _ => !IsBusy);
        BrowseExportCommand = new RelayCommand(_ => BrowseExport());
        BrowseImportCommand = new RelayCommand(_ => BrowseImport());
    }

    private void BrowseExport()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Cipher™ Vault Backup|*.cipherpw-backup",
            DefaultExt = ".cipherpw-backup"
        };
        if (dialog.ShowDialog() == true)
            ExportFilePath = dialog.FileName;
    }

    private void BrowseImport()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Cipher™ Vault Backup|*.cipherpw-backup|CSV Files|*.csv|All Files|*.*"
        };
        if (dialog.ShowDialog() == true)
            ImportFilePath = dialog.FileName;
    }

    private async Task ExportAsync()
    {
        if (string.IsNullOrEmpty(ExportFilePath) || string.IsNullOrEmpty(ExportPassword))
        {
            StatusMessage = "Please specify export file path and password.";
            return;
        }
        IsBusy = true;
        try
        {
            var entries = _mainVm.GetAllDecryptedEntries();
            var folders = (await _folderRepo.GetAllFoldersAsync());
            await _backupService.ExportAsync(ExportFilePath, ExportPassword, entries, folders);

            var settings = await _settingsRepo.GetSettingsAsync();
            settings.LastBackupPath = ExportFilePath;
            settings.LastBackupUtc = DateTime.UtcNow;
            await _settingsRepo.SaveSettingsAsync(settings);
            await _mainVm.RefreshSettingsAsync();

            StatusMessage = $"Export successful: {ExportFilePath}";
        }
        catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task ImportAsync()
    {
        if (string.IsNullOrEmpty(ImportFilePath))
        {
            StatusMessage = "Please choose a file to import.";
            return;
        }

        IsBusy = true;
        try
        {
            List<VaultEntryPlain> entries;
            var folderIdMap = new Dictionary<int, int>();

            if (ImportFilePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                string csv = await File.ReadAllTextAsync(ImportFilePath);
                entries = _backupService.ImportCsv(csv);
                StatusMessage = $"WARNING: Imported {entries.Count} entries from CSV. Please delete the CSV file now.";
            }
            else
            {
                if (string.IsNullOrEmpty(ImportPassword))
                {
                    StatusMessage = "Please enter the backup file password.";
                    return;
                }

                var payload = await _backupService.ImportAsync(ImportFilePath, ImportPassword);
                entries = payload.Entries;

                var existingFolders = await _folderRepo.GetAllFoldersAsync();
                foreach (var folder in payload.Folders)
                {
                    int targetFolderId;
                    var existing = existingFolders.FirstOrDefault(f =>
                        string.Equals(f.Name, folder.Name, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        targetFolderId = existing.Id;
                    }
                    else
                    {
                        targetFolderId = await _folderRepo.InsertFolderAsync(new Folder
                        {
                            Name = folder.Name,
                            CreatedAt = folder.CreatedAt == default ? DateTime.UtcNow : folder.CreatedAt
                        });

                        existingFolders.Add(new Folder
                        {
                            Id = targetFolderId,
                            Name = folder.Name,
                            CreatedAt = folder.CreatedAt == default ? DateTime.UtcNow : folder.CreatedAt
                        });
                    }

                    if (folder.Id > 0)
                        folderIdMap[folder.Id] = targetFolderId;
                }

                StatusMessage = $"Imported {entries.Count} entries and {folderIdMap.Count} folders.";
            }

            foreach (var entry in entries)
            {
                entry.Id = 0;
                entry.CreatedAt = DateTime.UtcNow;
                entry.UpdatedAt = DateTime.UtcNow;

                if (entry.FolderId.HasValue && folderIdMap.TryGetValue(entry.FolderId.Value, out int mappedFolderId))
                    entry.FolderId = mappedFolderId;
                else if (entry.FolderId.HasValue)
                    entry.FolderId = null;

                await _mainVm.SaveEntryAsync(entry, refresh: false);
            }

            await _mainVm.RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = $"Import failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}


