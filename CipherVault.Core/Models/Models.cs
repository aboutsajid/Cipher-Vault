namespace CipherVault.Core.Models;

/// <summary>Vault metadata stored in the database (never encrypted except canary).</summary>
public class VaultMeta
{
    public int Id { get; set; }
    public byte[] Salt { get; set; } = Array.Empty<byte>();
    public int ArgonMemoryMB { get; set; }
    public int ArgonIterations { get; set; }
    public int ArgonParallelism { get; set; }
    public bool TitleEncrypted { get; set; }
    public byte[] CanaryBlob { get; set; } = Array.Empty<byte>(); // Encrypted sentinel to verify unlock
    public DateTime CreatedAt { get; set; }
}

public class PasswordHistoryItem
{
    public string Password { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
}

/// <summary>Plain-text model used in memory only. Never persisted as-is.</summary>
public class VaultEntryPlain
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public int? FolderId { get; set; }
    public bool Favorite { get; set; }
    public string TotpSecret { get; set; } = string.Empty;
    public List<PasswordHistoryItem> PasswordHistory { get; set; } = new();
    public int PasswordReminderDays { get; set; } = 0;
    public DateTime? PasswordLastChangedUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Database storage model for an entry. All sensitive fields are encrypted.</summary>
public class VaultEntryRecord
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty; // May be plaintext or ciphertext depending on TitleEncrypted
    public byte[] EncryptedBlob { get; set; } = Array.Empty<byte>(); // AES-GCM: nonce+tag+ciphertext
    public bool Favorite { get; set; }
    public int? FolderId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A folder for organizing vault entries.</summary>
public class Folder
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Application settings persisted to the database.</summary>
public class AppSettings
{
    public int ClipboardClearSeconds { get; set; } = 25;
    public int AutoLockMinutes { get; set; } = 5;
    public bool LockOnMinimize { get; set; } = false;
    public string Theme { get; set; } = "System";
    public bool AllowBreachCheck { get; set; } = false;
    public string? LastBackupPath { get; set; }
    public bool OnboardingMasterPasswordConfirmed { get; set; } = false;
    public bool OnboardingBackupConfirmed { get; set; } = false;
    public bool OnboardingTransparencyConfirmed { get; set; } = false;
    public bool BrowserCaptureSilentMode { get; set; } = false;
    public string BrowserCaptureAutoSaveDomains { get; set; } = string.Empty;
    public int RecycleBinRetentionDays { get; set; } = 30;
    public bool WindowsHelloEnabled { get; set; } = false;
    public DateTime? LastBackupUtc { get; set; }
    public int SecurityStreakDays { get; set; } = 0;
    public DateTime? LastSecurityCheckUtc { get; set; }
    public int CompletedChallengeCount { get; set; } = 0;
    public DateTime? LastChallengeCompletedUtc { get; set; }
    public string RemediationDismissedEntryIds { get; set; } = string.Empty;
    public string RemediationQueueOrderEntryIds { get; set; } = string.Empty;
}

