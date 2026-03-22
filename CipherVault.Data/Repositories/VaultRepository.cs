using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace CipherVault.Data.Repositories;

public class VaultRepository : IVaultRepository
{
    private readonly string _connectionString;

    public VaultRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync();

        return conn;
    }

    public async Task<VaultMeta?> GetVaultMetaAsync()
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, salt, argon_memory, argon_iterations, argon_parallelism, title_encrypted, canary_blob, created_at FROM vault_meta LIMIT 1;";
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new VaultMeta
        {
            Id = reader.GetInt32(0),
            Salt = (byte[])reader[1],
            ArgonMemoryMB = reader.GetInt32(2),
            ArgonIterations = reader.GetInt32(3),
            ArgonParallelism = reader.GetInt32(4),
            TitleEncrypted = reader.GetInt32(5) == 1,
            CanaryBlob = (byte[])reader[6],
            CreatedAt = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }

    public async Task SaveVaultMetaAsync(VaultMeta meta)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();

        if (meta.Id == 0)
        {
            cmd.CommandText = @"
INSERT INTO vault_meta (salt, argon_memory, argon_iterations, argon_parallelism, title_encrypted, canary_blob, created_at)
VALUES (@salt, @mem, @iter, @par, @titleEnc, @canary, @created);";
        }
        else
        {
            cmd.CommandText = @"
UPDATE vault_meta SET salt=@salt, argon_memory=@mem, argon_iterations=@iter,
argon_parallelism=@par, title_encrypted=@titleEnc, canary_blob=@canary WHERE id=@id;";
            cmd.Parameters.AddWithValue("@id", meta.Id);
        }

        cmd.Parameters.AddWithValue("@salt", meta.Salt);
        cmd.Parameters.AddWithValue("@mem", meta.ArgonMemoryMB);
        cmd.Parameters.AddWithValue("@iter", meta.ArgonIterations);
        cmd.Parameters.AddWithValue("@par", meta.ArgonParallelism);
        cmd.Parameters.AddWithValue("@titleEnc", meta.TitleEncrypted ? 1 : 0);
        cmd.Parameters.AddWithValue("@canary", meta.CanaryBlob);
        cmd.Parameters.AddWithValue("@created", meta.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<VaultEntryRecord>> GetAllEntriesAsync(bool includeDeleted = false)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT id, title, encrypted_blob, favorite, folder_id, is_deleted, deleted_at, created_at, updated_at FROM entries ORDER BY title;"
            : "SELECT id, title, encrypted_blob, favorite, folder_id, is_deleted, deleted_at, created_at, updated_at FROM entries WHERE is_deleted=0 ORDER BY title;";
        using var reader = await cmd.ExecuteReaderAsync();

        var list = new List<VaultEntryRecord>();
        while (await reader.ReadAsync())
        {
            list.Add(new VaultEntryRecord
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                EncryptedBlob = (byte[])reader[2],
                Favorite = reader.GetInt32(3) == 1,
                FolderId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                IsDeleted = reader.GetInt32(5) == 1,
                DeletedAtUtc = reader.IsDBNull(6)
                    ? null
                    : DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CreatedAt = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                UpdatedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return list;
    }

    public async Task<VaultEntryRecord?> GetEntryByIdAsync(int id)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, encrypted_blob, favorite, folder_id, is_deleted, deleted_at, created_at, updated_at FROM entries WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new VaultEntryRecord
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            EncryptedBlob = (byte[])reader[2],
            Favorite = reader.GetInt32(3) == 1,
            FolderId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            IsDeleted = reader.GetInt32(5) == 1,
            DeletedAtUtc = reader.IsDBNull(6)
                ? null
                : DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            CreatedAt = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }

    public async Task<int> InsertEntryAsync(VaultEntryRecord record)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO entries (title, encrypted_blob, favorite, folder_id, is_deleted, deleted_at, created_at, updated_at)
VALUES (@title, @blob, @fav, @folderId, @isDeleted, @deletedAt, @created, @updated);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@title", record.Title);
        cmd.Parameters.AddWithValue("@blob", record.EncryptedBlob);
        cmd.Parameters.AddWithValue("@fav", record.Favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@folderId", record.FolderId.HasValue ? record.FolderId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@isDeleted", record.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@deletedAt", record.DeletedAtUtc.HasValue ? record.DeletedAtUtc.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@created", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated", record.UpdatedAt.ToString("O"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task UpdateEntryAsync(VaultEntryRecord record)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE entries SET title=@title, encrypted_blob=@blob, favorite=@fav,
folder_id=@folderId, is_deleted=@isDeleted, deleted_at=@deletedAt, updated_at=@updated WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@title", record.Title);
        cmd.Parameters.AddWithValue("@blob", record.EncryptedBlob);
        cmd.Parameters.AddWithValue("@fav", record.Favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@folderId", record.FolderId.HasValue ? record.FolderId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@isDeleted", record.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@deletedAt", record.DeletedAtUtc.HasValue ? record.DeletedAtUtc.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@updated", record.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteEntryAsync(int id)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE entries
SET is_deleted=1, deleted_at=@deletedAt, updated_at=@updated
WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@deletedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RestoreEntryAsync(int id)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE entries
SET is_deleted=0, deleted_at=NULL, updated_at=@updated
WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteEntryPermanentlyAsync(int id)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}

