using CipherVault.Core.Models;

namespace CipherVault.Core.Services;

public sealed class DuplicateAccountItem
{
    public int EntryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class DuplicateAccountGroupResult
{
    public string Site { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<DuplicateAccountItem> Entries { get; set; } = new();
}

public sealed class DuplicateMergeResult
{
    public VaultEntryPlain Keeper { get; set; } = new();
    public List<int> MergedEntryIds { get; set; } = new();
}

public sealed class DuplicateAccountService
{
    private const int DefaultHistoryLimit = 10;

    public List<DuplicateAccountGroupResult> FindDuplicateGroups(IEnumerable<VaultEntryPlain> entries)
    {
        if (entries == null) return new List<DuplicateAccountGroupResult>();

        var candidates = entries
            .Where(e => e != null && !e.IsDeleted)
            .Select(e => new
            {
                Entry = e,
                Site = NormalizeSite(e.Url),
                Username = NormalizeUsername(e.Username)
            })
            .Where(x => x.Site.Length > 0 && x.Username.Length > 0)
            .ToList();

        var groups = candidates
            .GroupBy(x => $"{x.Site}|{x.Username}", StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateAccountGroupResult
            {
                Site = g.First().Site,
                Username = g.First().Username,
                Entries = g
                    .Select(x => new DuplicateAccountItem
                    {
                        EntryId = x.Entry.Id,
                        Title = string.IsNullOrWhiteSpace(x.Entry.Title) ? "(Untitled)" : x.Entry.Title,
                        Site = x.Site,
                        Username = x.Entry.Username,
                        Url = x.Entry.Url,
                        Favorite = x.Entry.Favorite,
                        UpdatedAt = x.Entry.UpdatedAt
                    })
                    .OrderByDescending(x => x.Favorite)
                    .ThenByDescending(x => x.UpdatedAt)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderByDescending(g => g.Entries.Count)
            .ThenBy(g => g.Site, StringComparer.Ordinal)
            .ThenBy(g => g.Username, StringComparer.Ordinal)
            .ToList();

        return groups;
    }

    public DuplicateMergeResult MergeDuplicateGroup(IReadOnlyList<VaultEntryPlain> groupEntries, int historyLimit = DefaultHistoryLimit)
    {
        if (groupEntries == null || groupEntries.Count < 2)
            throw new ArgumentException("At least two entries are required to merge duplicates.", nameof(groupEntries));

        historyLimit = Math.Clamp(historyLimit, 1, 100);

        var ordered = groupEntries
            .Where(e => e != null)
            .OrderByDescending(e => e.Favorite)
            .ThenByDescending(e => e.UpdatedAt)
            .ThenByDescending(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();

        if (ordered.Count < 2)
            throw new ArgumentException("At least two valid entries are required to merge duplicates.", nameof(groupEntries));

        var keeper = CloneEntry(ordered[0]);
        var mergedIds = new List<int>();

        foreach (var duplicate in ordered.Skip(1))
        {
            mergedIds.Add(duplicate.Id);
            MergeIntoKeeper(keeper, duplicate);
        }

        keeper.PasswordHistory = NormalizeHistory(keeper.PasswordHistory, historyLimit);
        keeper.UpdatedAt = DateTime.UtcNow;

        return new DuplicateMergeResult
        {
            Keeper = keeper,
            MergedEntryIds = mergedIds
        };
    }

    private static void MergeIntoKeeper(VaultEntryPlain keeper, VaultEntryPlain duplicate)
    {
        if (string.IsNullOrWhiteSpace(keeper.Password) && !string.IsNullOrWhiteSpace(duplicate.Password))
        {
            keeper.Password = duplicate.Password;
            keeper.PasswordLastChangedUtc ??= duplicate.PasswordLastChangedUtc ?? ResolveFallbackTimestamp(duplicate);
        }
        else if (!string.IsNullOrWhiteSpace(duplicate.Password)
                 && !string.Equals(keeper.Password, duplicate.Password, StringComparison.Ordinal))
        {
            keeper.PasswordHistory.Add(new PasswordHistoryItem
            {
                Password = duplicate.Password,
                ChangedAtUtc = duplicate.PasswordLastChangedUtc ?? ResolveFallbackTimestamp(duplicate)
            });
        }

        foreach (var history in duplicate.PasswordHistory)
        {
            if (history == null || string.IsNullOrWhiteSpace(history.Password))
                continue;

            keeper.PasswordHistory.Add(new PasswordHistoryItem
            {
                Password = history.Password,
                ChangedAtUtc = history.ChangedAtUtc == default
                    ? ResolveFallbackTimestamp(duplicate)
                    : history.ChangedAtUtc
            });
        }

        if (string.IsNullOrWhiteSpace(keeper.Url) && !string.IsNullOrWhiteSpace(duplicate.Url))
            keeper.Url = duplicate.Url;

        if (string.IsNullOrWhiteSpace(keeper.TotpSecret) && !string.IsNullOrWhiteSpace(duplicate.TotpSecret))
            keeper.TotpSecret = duplicate.TotpSecret;

        if (!keeper.FolderId.HasValue && duplicate.FolderId.HasValue)
            keeper.FolderId = duplicate.FolderId;

        keeper.Favorite = keeper.Favorite || duplicate.Favorite;
        keeper.Tags = MergeTags(keeper.Tags, duplicate.Tags);
        keeper.Notes = MergeNotes(keeper.Notes, duplicate);

        if (keeper.PasswordReminderDays == 0 && duplicate.PasswordReminderDays > 0)
            keeper.PasswordReminderDays = duplicate.PasswordReminderDays;

        if (!keeper.PasswordLastChangedUtc.HasValue && duplicate.PasswordLastChangedUtc.HasValue)
            keeper.PasswordLastChangedUtc = duplicate.PasswordLastChangedUtc;
    }

    private static List<PasswordHistoryItem> NormalizeHistory(IEnumerable<PasswordHistoryItem> history, int limit)
    {
        return history
            .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Password))
            .GroupBy(h => h.Password, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(h => h.ChangedAtUtc).First())
            .OrderByDescending(h => h.ChangedAtUtc)
            .Take(limit)
            .Select(h => new PasswordHistoryItem
            {
                Password = h.Password,
                ChangedAtUtc = h.ChangedAtUtc == default ? DateTime.UtcNow : h.ChangedAtUtc
            })
            .ToList();
    }

    private static string MergeTags(string keeperTags, string duplicateTags)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in SplitTags(keeperTags))
            tags.Add(tag);
        foreach (var tag in SplitTags(duplicateTags))
            tags.Add(tag);

        return string.Join(", ", tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SplitTags(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
    }

    private static string MergeNotes(string keeperNotes, VaultEntryPlain duplicate)
    {
        if (string.IsNullOrWhiteSpace(duplicate.Notes))
            return keeperNotes;

        string duplicateNote = duplicate.Notes.Trim();
        if (!string.IsNullOrWhiteSpace(keeperNotes)
            && keeperNotes.Contains(duplicateNote, StringComparison.Ordinal))
        {
            return keeperNotes;
        }

        string sourceTitle = string.IsNullOrWhiteSpace(duplicate.Title) ? "Untitled" : duplicate.Title;
        string sourceUrl = string.IsNullOrWhiteSpace(duplicate.Url) ? "(no url)" : duplicate.Url;
        string appendedBlock = $"[Merged from: {sourceTitle} | {sourceUrl}]\n{duplicateNote}";

        if (string.IsNullOrWhiteSpace(keeperNotes))
            return appendedBlock;

        return keeperNotes.TrimEnd() + "\n\n" + appendedBlock;
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
            FolderId = source.FolderId,
            Favorite = source.Favorite,
            TotpSecret = source.TotpSecret,
            PasswordHistory = source.PasswordHistory
                .Select(h => new PasswordHistoryItem { Password = h.Password, ChangedAtUtc = h.ChangedAtUtc })
                .ToList(),
            PasswordReminderDays = source.PasswordReminderDays,
            PasswordLastChangedUtc = source.PasswordLastChangedUtc,
            IsDeleted = source.IsDeleted,
            DeletedAtUtc = source.DeletedAtUtc,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    private static DateTime ResolveFallbackTimestamp(VaultEntryPlain entry)
    {
        if (entry.UpdatedAt != default)
            return entry.UpdatedAt;

        if (entry.CreatedAt != default)
            return entry.CreatedAt;

        return DateTime.UtcNow;
    }

    private static string NormalizeUsername(string username)
        => string.IsNullOrWhiteSpace(username)
            ? string.Empty
            : username.Trim().ToLowerInvariant();

    private static string NormalizeSite(string? urlOrDomain)
    {
        if (string.IsNullOrWhiteSpace(urlOrDomain))
            return string.Empty;

        string value = urlOrDomain.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            return absolute.Host.Trim().Trim('.').ToLowerInvariant();

        if (Uri.TryCreate($"https://{value}", UriKind.Absolute, out var hostUri))
            return hostUri.Host.Trim().Trim('.').ToLowerInvariant();

        string firstSegment = value.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return firstSegment.Trim().Trim('.').ToLowerInvariant();
    }
}