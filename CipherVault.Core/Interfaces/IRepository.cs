using CipherVault.Core.Models;

namespace CipherVault.Core.Interfaces;

public interface IVaultRepository
{
    Task<VaultMeta?> GetVaultMetaAsync();
    Task SaveVaultMetaAsync(VaultMeta meta);
    Task<List<VaultEntryRecord>> GetAllEntriesAsync(bool includeDeleted = false);
    Task<VaultEntryRecord?> GetEntryByIdAsync(int id);
    Task<int> InsertEntryAsync(VaultEntryRecord record);
    Task UpdateEntryAsync(VaultEntryRecord record);
    Task DeleteEntryAsync(int id);
    Task RestoreEntryAsync(int id);
    Task DeleteEntryPermanentlyAsync(int id);
}

public interface IFolderRepository
{
    Task<List<Folder>> GetAllFoldersAsync();
    Task<int> InsertFolderAsync(Folder folder);
    Task UpdateFolderAsync(Folder folder);
    Task DeleteFolderAsync(int id);
}

public interface ISettingsRepository
{
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
}


