using CipherVault.Core.Models;

namespace CipherVault.UI.ViewModels;

public interface IAuditWorkflowHost
{
    AppSettings Settings { get; }
    string StatusText { get; set; }

    List<VaultEntryPlain> GetAllDecryptedEntries(bool includeDeleted = false);
    Task SaveEntryAsync(VaultEntryPlain entry, bool refresh = true);
    Task RefreshAsync();
    void RecordUserActivity();
    Task RecordRemediationQueueCompletedAsync(int clearedItems);
    Task PersistRemediationQueueStateAsync(IEnumerable<int> dismissedEntryIds, IEnumerable<int> queueOrderEntryIds);
    bool OpenEntryEditorById(int entryId, bool returnToAudit = false);
}
