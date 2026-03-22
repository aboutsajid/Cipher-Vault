using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace CipherVault.Data.Repositories;

public class FolderRepository : IFolderRepository
{
    private readonly string _connectionString;
    public FolderRepository(string connectionString) => _connectionString = connectionString;

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync();

        return conn;
    }

    public async Task<List<Folder>> GetAllFoldersAsync()
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, created_at FROM folders ORDER BY name;";
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Folder>();
        while (await reader.ReadAsync())
            list.Add(new Folder
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        return list;
    }

    public async Task<int> InsertFolderAsync(Folder folder)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO folders (name, created_at) VALUES (@name, @created); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", folder.Name);
        cmd.Parameters.AddWithValue("@created", folder.CreatedAt.ToString("O"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task UpdateFolderAsync(Folder folder)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE folders SET name=@name WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", folder.Id);
        cmd.Parameters.AddWithValue("@name", folder.Name);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteFolderAsync(int id)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM folders WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}

public class SettingsRepository : ISettingsRepository
{
    private readonly string _connectionString;
    public SettingsRepository(string connectionString) => _connectionString = connectionString;

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync();

        return conn;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    clipboard_clear_seconds,
    auto_lock_minutes,
    lock_on_minimize,
    theme,
    allow_breach_check,
    last_backup_path,
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
    remediation_queue_order_entry_ids
FROM app_settings
WHERE id=1;";
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new AppSettings();

        int retentionDays = reader.GetInt32(11);
        retentionDays = Math.Clamp(retentionDays, 1, 3650);

        return new AppSettings
        {
            ClipboardClearSeconds = reader.GetInt32(0),
            AutoLockMinutes = reader.GetInt32(1),
            LockOnMinimize = reader.GetInt32(2) == 1,
            Theme = reader.GetString(3),
            AllowBreachCheck = reader.GetInt32(4) == 1,
            LastBackupPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            OnboardingMasterPasswordConfirmed = reader.GetInt32(6) == 1,
            OnboardingBackupConfirmed = reader.GetInt32(7) == 1,
            OnboardingTransparencyConfirmed = reader.GetInt32(8) == 1,
            BrowserCaptureSilentMode = reader.GetInt32(9) == 1,
            BrowserCaptureAutoSaveDomains = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            RecycleBinRetentionDays = retentionDays,
            WindowsHelloEnabled = reader.GetInt32(12) == 1,
            LastBackupUtc = ParseNullableDate(reader, 13),
            SecurityStreakDays = reader.GetInt32(14),
            LastSecurityCheckUtc = ParseNullableDate(reader, 15),
            CompletedChallengeCount = reader.GetInt32(16),
            LastChallengeCompletedUtc = ParseNullableDate(reader, 17),
            RemediationDismissedEntryIds = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
            RemediationQueueOrderEntryIds = reader.IsDBNull(19) ? string.Empty : reader.GetString(19)
        };
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO app_settings (
    id,
    clipboard_clear_seconds,
    auto_lock_minutes,
    lock_on_minimize,
    theme,
    allow_breach_check,
    last_backup_path,
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
VALUES (1, @cls, @alm, @lom, @theme, @breach, @backup, @masterConfirm, @backupConfirm, @privacyConfirm, @captureSilent, @captureDomains, @recycleRetention, @helloEnabled, @lastBackupUtc, @streakDays, @lastStreakCheckUtc, @challengeCount, @lastChallengeUtc, @remediationDismissed, @remediationOrder)
ON CONFLICT(id) DO UPDATE SET
    clipboard_clear_seconds=excluded.clipboard_clear_seconds,
    auto_lock_minutes=excluded.auto_lock_minutes,
    lock_on_minimize=excluded.lock_on_minimize,
    theme=excluded.theme,
    allow_breach_check=excluded.allow_breach_check,
    last_backup_path=excluded.last_backup_path,
    onboarding_master_password_confirmed=excluded.onboarding_master_password_confirmed,
    onboarding_backup_confirmed=excluded.onboarding_backup_confirmed,
    onboarding_transparency_confirmed=excluded.onboarding_transparency_confirmed,
    browser_capture_silent_mode=excluded.browser_capture_silent_mode,
    browser_capture_auto_save_domains=excluded.browser_capture_auto_save_domains,
    recycle_bin_retention_days=excluded.recycle_bin_retention_days,
    windows_hello_enabled=excluded.windows_hello_enabled,
    last_backup_utc=excluded.last_backup_utc,
    security_streak_days=excluded.security_streak_days,
    last_security_check_utc=excluded.last_security_check_utc,
    completed_challenge_count=excluded.completed_challenge_count,
    last_challenge_completed_utc=excluded.last_challenge_completed_utc,
    remediation_dismissed_entry_ids=excluded.remediation_dismissed_entry_ids,
    remediation_queue_order_entry_ids=excluded.remediation_queue_order_entry_ids;";
        cmd.Parameters.AddWithValue("@cls", settings.ClipboardClearSeconds);
        cmd.Parameters.AddWithValue("@alm", settings.AutoLockMinutes);
        cmd.Parameters.AddWithValue("@lom", settings.LockOnMinimize ? 1 : 0);
        cmd.Parameters.AddWithValue("@theme", settings.Theme);
        cmd.Parameters.AddWithValue("@breach", settings.AllowBreachCheck ? 1 : 0);
        cmd.Parameters.AddWithValue("@backup", settings.LastBackupPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@masterConfirm", settings.OnboardingMasterPasswordConfirmed ? 1 : 0);
        cmd.Parameters.AddWithValue("@backupConfirm", settings.OnboardingBackupConfirmed ? 1 : 0);
        cmd.Parameters.AddWithValue("@privacyConfirm", settings.OnboardingTransparencyConfirmed ? 1 : 0);
        cmd.Parameters.AddWithValue("@captureSilent", settings.BrowserCaptureSilentMode ? 1 : 0);
        cmd.Parameters.AddWithValue("@captureDomains", settings.BrowserCaptureAutoSaveDomains ?? string.Empty);
        cmd.Parameters.AddWithValue("@recycleRetention", Math.Clamp(settings.RecycleBinRetentionDays, 1, 3650));
        cmd.Parameters.AddWithValue("@helloEnabled", settings.WindowsHelloEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@lastBackupUtc", settings.LastBackupUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@streakDays", Math.Max(0, settings.SecurityStreakDays));
        cmd.Parameters.AddWithValue("@lastStreakCheckUtc", settings.LastSecurityCheckUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@challengeCount", Math.Max(0, settings.CompletedChallengeCount));
        cmd.Parameters.AddWithValue("@lastChallengeUtc", settings.LastChallengeCompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@remediationDismissed", settings.RemediationDismissedEntryIds ?? string.Empty);
        cmd.Parameters.AddWithValue("@remediationOrder", settings.RemediationQueueOrderEntryIds ?? string.Empty);
        await cmd.ExecuteNonQueryAsync();
    }

    private static DateTime? ParseNullableDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var raw = reader.GetString(ordinal);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        return null;
    }
}
