using Microsoft.Data.Sqlite;

namespace CipherVault.Data;

/// <summary>Initializes the SQLite database schema for CipherVault.</summary>
public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Enable WAL for better concurrency and crash safety
        await ExecuteAsync(conn, "PRAGMA journal_mode=WAL;");
        await ExecuteAsync(conn, "PRAGMA foreign_keys=ON;");

        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS vault_meta (
    id INTEGER PRIMARY KEY,
    salt BLOB NOT NULL,
    argon_memory INTEGER NOT NULL,
    argon_iterations INTEGER NOT NULL,
    argon_parallelism INTEGER NOT NULL,
    title_encrypted INTEGER NOT NULL DEFAULT 0,
    canary_blob BLOB NOT NULL,
    created_at TEXT NOT NULL
);");

        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS folders (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    created_at TEXT NOT NULL
);");

        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS entries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    encrypted_blob BLOB NOT NULL,
    favorite INTEGER NOT NULL DEFAULT 0,
    folder_id INTEGER NULL REFERENCES folders(id) ON DELETE SET NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        await ExecuteAsync(conn, @"
CREATE TABLE IF NOT EXISTS app_settings (
    id INTEGER PRIMARY KEY CHECK (id=1),
    clipboard_clear_seconds INTEGER NOT NULL DEFAULT 25,
    auto_lock_minutes INTEGER NOT NULL DEFAULT 5,
    lock_on_minimize INTEGER NOT NULL DEFAULT 0,
    theme TEXT NOT NULL DEFAULT 'System',
    allow_breach_check INTEGER NOT NULL DEFAULT 0,
    last_backup_path TEXT NULL,
    onboarding_master_password_confirmed INTEGER NOT NULL DEFAULT 0,
    onboarding_backup_confirmed INTEGER NOT NULL DEFAULT 0,
    onboarding_transparency_confirmed INTEGER NOT NULL DEFAULT 0,
    browser_capture_silent_mode INTEGER NOT NULL DEFAULT 0,
    browser_capture_auto_save_domains TEXT NOT NULL DEFAULT '',
    recycle_bin_retention_days INTEGER NOT NULL DEFAULT 30,
    windows_hello_enabled INTEGER NOT NULL DEFAULT 0,
    last_backup_utc TEXT NULL,
    security_streak_days INTEGER NOT NULL DEFAULT 0,
    last_security_check_utc TEXT NULL,
    completed_challenge_count INTEGER NOT NULL DEFAULT 0,
    last_challenge_completed_utc TEXT NULL,
    remediation_dismissed_entry_ids TEXT NOT NULL DEFAULT '',
    remediation_queue_order_entry_ids TEXT NOT NULL DEFAULT ''
);");

        await EnsureColumnAsync(conn, "app_settings", "onboarding_master_password_confirmed", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "app_settings", "onboarding_backup_confirmed", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "app_settings", "onboarding_transparency_confirmed", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "app_settings", "browser_capture_silent_mode", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "app_settings", "browser_capture_auto_save_domains", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(conn, "app_settings", "recycle_bin_retention_days", "INTEGER NOT NULL DEFAULT 30");
        await EnsureColumnAsync(conn, "app_settings", "windows_hello_enabled", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "app_settings", "last_backup_utc", "TEXT NULL");
        await EnsureColumnAsync(conn, "app_settings", "security_streak_days", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "app_settings", "last_security_check_utc", "TEXT NULL");
        await EnsureColumnAsync(conn, "app_settings", "completed_challenge_count", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "entries", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "entries", "deleted_at", "TEXT NULL");
        await EnsureColumnAsync(conn, "app_settings", "last_challenge_completed_utc", "TEXT NULL");
        await EnsureColumnAsync(conn, "app_settings", "remediation_dismissed_entry_ids", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(conn, "app_settings", "remediation_queue_order_entry_ids", "TEXT NOT NULL DEFAULT ''");

        // Insert default settings if not present
        await ExecuteAsync(conn, @"
INSERT OR IGNORE INTO app_settings (
    id,
    clipboard_clear_seconds,
    auto_lock_minutes,
    lock_on_minimize,
    theme,
    allow_breach_check,
    onboarding_master_password_confirmed,
    onboarding_backup_confirmed,
    onboarding_transparency_confirmed,
    browser_capture_silent_mode,
    browser_capture_auto_save_domains,
    recycle_bin_retention_days,
    windows_hello_enabled,
    last_backup_utc,
    security_streak_days,
    last_security_check_utc,
    completed_challenge_count,
    last_challenge_completed_utc,
    remediation_dismissed_entry_ids,
    remediation_queue_order_entry_ids)
VALUES (1, 25, 5, 0, 'System', 0, 0, 0, 0, 0, '', 30, 0, NULL, 0, NULL, 0, NULL, '', '');");
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureColumnAsync(SqliteConnection conn, string tableName, string columnName, string columnDefinition)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await check.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        await ExecuteAsync(conn, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
    }
}

