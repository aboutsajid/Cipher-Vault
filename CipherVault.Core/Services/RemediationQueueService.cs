using CipherVault.Core.Models;

namespace CipherVault.Core.Services;

public sealed class RemediationBreachResult
{
    public int EntryId { get; set; }
    public string EntryTitle { get; set; } = string.Empty;
    public int BreachCount { get; set; }
}

public sealed class RemediationQueueItem
{
    public int EntryId { get; set; }
    public string EntryTitle { get; set; } = string.Empty;
    public bool HasWeakIssues { get; set; }
    public bool IsBreached { get; set; }
    public bool IsDuplicate { get; set; }
    public int Score { get; set; }

    public string RiskLabel
    {
        get
        {
            var labels = new List<string>();
            if (IsBreached) labels.Add("breached");
            if (HasWeakIssues) labels.Add("weak");
            if (IsDuplicate) labels.Add("duplicate");
            return labels.Count == 0 ? "review" : string.Join(" + ", labels);
        }
    }
}

public sealed class RemediationQueueService
{
    public List<RemediationQueueItem> BuildQueue(
        IEnumerable<AuditResult>? auditResults,
        IEnumerable<RemediationBreachResult>? breachResults,
        IEnumerable<DuplicateAccountGroupResult>? duplicateGroups)
    {
        var byEntryId = new Dictionary<int, RemediationQueueItem>();

        void AddOrUpdate(int entryId, string title, int scoreDelta, bool weak = false, bool breached = false, bool duplicate = false)
        {
            if (entryId <= 0)
                return;

            if (!byEntryId.TryGetValue(entryId, out var item))
            {
                item = new RemediationQueueItem
                {
                    EntryId = entryId,
                    EntryTitle = NormalizeTitle(title)
                };
                byEntryId[entryId] = item;
            }
            else if (item.EntryTitle == "(Untitled)" && !string.IsNullOrWhiteSpace(title))
            {
                item.EntryTitle = NormalizeTitle(title);
            }

            item.Score += Math.Max(1, scoreDelta);
            item.HasWeakIssues |= weak;
            item.IsBreached |= breached;
            item.IsDuplicate |= duplicate;
        }

        foreach (var result in auditResults ?? Enumerable.Empty<AuditResult>())
            AddOrUpdate(result.EntryId, result.EntryTitle, result.Issues.Count, weak: true);

        foreach (var breach in breachResults ?? Enumerable.Empty<RemediationBreachResult>())
            AddOrUpdate(breach.EntryId, breach.EntryTitle, ResolveBreachScore(breach.BreachCount), breached: true);

        foreach (var group in duplicateGroups ?? Enumerable.Empty<DuplicateAccountGroupResult>())
        {
            foreach (var duplicate in group.Entries
                .GroupBy(x => x.EntryId)
                .Select(x => x.First()))
            {
                AddOrUpdate(duplicate.EntryId, duplicate.Title, 2, duplicate: true);
            }
        }

        return byEntryId.Values
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.IsBreached)
            .ThenByDescending(x => x.HasWeakIssues)
            .ThenByDescending(x => x.IsDuplicate)
            .ThenBy(x => x.EntryTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<RemediationQueueItem> RemoveEntry(IEnumerable<RemediationQueueItem>? queue, int entryId)
    {
        return (queue ?? Enumerable.Empty<RemediationQueueItem>())
            .Where(item => item.EntryId != entryId)
            .ToList();
    }

    public List<RemediationQueueItem> RotateQueue(IEnumerable<RemediationQueueItem>? queue)
    {
        var list = (queue ?? Enumerable.Empty<RemediationQueueItem>()).ToList();
        if (list.Count <= 1)
            return list;

        return list.Skip(1).Concat(new[] { list[0] }).ToList();
    }

    private static int ResolveBreachScore(int breachCount)
    {
        if (breachCount >= 1_000_000) return 10;
        if (breachCount >= 10_000) return 8;
        return 6;
    }

    private static string NormalizeTitle(string title)
        => string.IsNullOrWhiteSpace(title) ? "(Untitled)" : title.Trim();
}



