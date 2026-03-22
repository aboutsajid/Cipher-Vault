using CipherVault.Core.Models;
using CipherVault.Data;
using CipherVault.Data.Repositories;
using Xunit;

namespace CipherVault.Tests;

public class VaultRepositoryTests : IAsyncLifetime
{
    private string _tempPath = string.Empty;
    private string _connectionString = string.Empty;
    private VaultRepository _repo = null!;
    private DatabaseInitializer _init = null!;

    public async Task InitializeAsync()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"cipherpw_test_{Guid.NewGuid()}.cipherpw");
        _connectionString = $"Data Source={_tempPath}";

        _init = new DatabaseInitializer(_connectionString);
        await _init.InitializeAsync();
        _repo = new VaultRepository(_connectionString);
    }

    public Task DisposeAsync() => TestFileCleanup.DeleteWithRetryAsync(_tempPath);

    [Fact]
    public async Task InsertAndRetrieveEntry()
    {
        var record = new VaultEntryRecord
        {
            Title = "Test Entry",
            EncryptedBlob = new byte[] { 1, 2, 3, 4, 5 },
            Favorite = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        int id = await _repo.InsertEntryAsync(record);
        Assert.True(id > 0);

        VaultEntryRecord? loaded = await _repo.GetEntryByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Test Entry", loaded!.Title);
        Assert.Equal(record.EncryptedBlob, loaded.EncryptedBlob);
    }

    [Fact]
    public async Task UpdateEntry()
    {
        var record = new VaultEntryRecord
        {
            Title = "Original",
            EncryptedBlob = new byte[] { 1, 2, 3 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        record.Id = await _repo.InsertEntryAsync(record);
        record.Title = "Updated";
        record.EncryptedBlob = new byte[] { 9, 8, 7 };
        await _repo.UpdateEntryAsync(record);

        VaultEntryRecord? loaded = await _repo.GetEntryByIdAsync(record.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated", loaded!.Title);
        Assert.Equal(new byte[] { 9, 8, 7 }, loaded.EncryptedBlob);
    }

    [Fact]
    public async Task DeleteEntryMovesRecordToRecycleBin()
    {
        var record = new VaultEntryRecord
        {
            Title = "To Delete",
            EncryptedBlob = new byte[] { 1 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        int id = await _repo.InsertEntryAsync(record);
        await _repo.DeleteEntryAsync(id);

        VaultEntryRecord? loaded = await _repo.GetEntryByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsDeleted);
        Assert.NotNull(loaded.DeletedAtUtc);
        Assert.Empty(await _repo.GetAllEntriesAsync());
    }

    [Fact]
    public async Task RestoreEntryRemovesFromRecycleBin()
    {
        var record = new VaultEntryRecord
        {
            Title = "To Restore",
            EncryptedBlob = new byte[] { 9 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        int id = await _repo.InsertEntryAsync(record);
        await _repo.DeleteEntryAsync(id);
        await _repo.RestoreEntryAsync(id);

        VaultEntryRecord? loaded = await _repo.GetEntryByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.False(loaded!.IsDeleted);
        Assert.Null(loaded.DeletedAtUtc);
        Assert.Single(await _repo.GetAllEntriesAsync());
    }

    [Fact]
    public async Task DeleteEntryPermanentlyRemovesRecord()
    {
        var record = new VaultEntryRecord
        {
            Title = "To Purge",
            EncryptedBlob = new byte[] { 3 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        int id = await _repo.InsertEntryAsync(record);
        await _repo.DeleteEntryAsync(id);
        await _repo.DeleteEntryPermanentlyAsync(id);

        Assert.Null(await _repo.GetEntryByIdAsync(id));
        Assert.DoesNotContain(await _repo.GetAllEntriesAsync(includeDeleted: true), e => e.Id == id);
    }

    [Fact]
    public async Task GetAllEntriesIncludeDeletedReturnsAllRows()
    {
        int activeId = await _repo.InsertEntryAsync(new VaultEntryRecord
        {
            Title = "Active Entry",
            EncryptedBlob = new byte[] { 1, 2 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        int deletedId = await _repo.InsertEntryAsync(new VaultEntryRecord
        {
            Title = "Deleted Entry",
            EncryptedBlob = new byte[] { 3, 4 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _repo.DeleteEntryAsync(deletedId);

        List<VaultEntryRecord> activeOnly = await _repo.GetAllEntriesAsync();
        List<VaultEntryRecord> all = await _repo.GetAllEntriesAsync(includeDeleted: true);

        Assert.Single(activeOnly);
        Assert.Equal(activeId, activeOnly[0].Id);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.Id == deletedId && e.IsDeleted);
    }

    [Fact]
    public async Task GetAllEntriesReturnsList()
    {
        for (int i = 0; i < 5; i++)
        {
            await _repo.InsertEntryAsync(new VaultEntryRecord
            {
                Title = $"Entry {i}",
                EncryptedBlob = new[] { (byte)i },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        Assert.Equal(5, (await _repo.GetAllEntriesAsync()).Count);
    }

    [Fact]
    public async Task VaultMetaSaveAndRetrieve()
    {
        var meta = new VaultMeta
        {
            Salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            ArgonMemoryMB = 256,
            ArgonIterations = 3,
            ArgonParallelism = 4,
            CanaryBlob = new byte[] { 99, 88, 77 },
            CreatedAt = DateTime.UtcNow
        };

        await _repo.SaveVaultMetaAsync(meta);

        VaultMeta? loaded = await _repo.GetVaultMetaAsync();
        Assert.NotNull(loaded);
        Assert.Equal(256, loaded!.ArgonMemoryMB);
        Assert.Equal(meta.Salt, loaded.Salt);
    }
}


