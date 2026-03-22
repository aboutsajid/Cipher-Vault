using System.Security.Cryptography;
using System.Text;
using System.IO;
using Windows.Security.Credentials.UI;

namespace CipherVault.UI.Services;

public enum WindowsHelloUnlockOutcome
{
    Success,
    NotConfigured,
    NotAvailable,
    VerificationFailed,
    KeyUnavailable,
    Error
}

public sealed class WindowsHelloUnlockAttempt
{
    public WindowsHelloUnlockOutcome Outcome { get; init; }
    public string Message { get; init; } = string.Empty;
    public byte[]? Key { get; init; }
}

public sealed class WindowsHelloUnlockService
{
    private static readonly byte[] AdditionalEntropy = Encoding.UTF8.GetBytes("CipherVault.WindowsHello.v1");
    private readonly string _wrappedKeyPath;

    public WindowsHelloUnlockService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "CipherVault");
        Directory.CreateDirectory(dir);
        _wrappedKeyPath = Path.Combine(dir, "windows-hello.key");
    }

    public bool HasEnrollment => File.Exists(_wrappedKeyPath);

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            return availability == UserConsentVerifierAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryEnrollAsync(byte[] key)
    {
        if (key == null || key.Length == 0)
            return false;

        byte[] keyCopy = key.ToArray();
        try
        {
            byte[] protectedBlob = ProtectedData.Protect(keyCopy, AdditionalEntropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_wrappedKeyPath, protectedBlob);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Array.Clear(keyCopy, 0, keyCopy.Length);
        }
    }

    public Task DisableAsync()
    {
        try
        {
            if (File.Exists(_wrappedKeyPath))
                File.Delete(_wrappedKeyPath);
        }
        catch
        {
            // best-effort cleanup
        }

        return Task.CompletedTask;
    }

    public async Task<WindowsHelloUnlockAttempt> TryUnlockAsync()
    {
        if (!HasEnrollment)
        {
            return new WindowsHelloUnlockAttempt
            {
                Outcome = WindowsHelloUnlockOutcome.NotConfigured,
                Message = "Windows Hello is not configured for this vault yet."
            };
        }

        UserConsentVerifierAvailability availability;
        try
        {
            availability = await UserConsentVerifier.CheckAvailabilityAsync();
        }
        catch
        {
            return new WindowsHelloUnlockAttempt
            {
                Outcome = WindowsHelloUnlockOutcome.NotAvailable,
                Message = "Windows Hello is not available on this device."
            };
        }

        if (availability != UserConsentVerifierAvailability.Available)
        {
            return new WindowsHelloUnlockAttempt
            {
                Outcome = WindowsHelloUnlockOutcome.NotAvailable,
                Message = $"Windows Hello unavailable: {availability}."
            };
        }

        UserConsentVerificationResult verification;
        try
        {
            verification = await UserConsentVerifier.RequestVerificationAsync("Verify your identity to unlock Cipher™ Vault");
        }
        catch
        {
            return new WindowsHelloUnlockAttempt
            {
                Outcome = WindowsHelloUnlockOutcome.NotAvailable,
                Message = "Unable to show Windows Hello prompt."
            };
        }

        if (verification != UserConsentVerificationResult.Verified)
        {
            return new WindowsHelloUnlockAttempt
            {
                Outcome = WindowsHelloUnlockOutcome.VerificationFailed,
                Message = verification switch
                {
                    UserConsentVerificationResult.Canceled => "Windows Hello verification was canceled.",
                    UserConsentVerificationResult.RetriesExhausted => "Too many failed biometric attempts.",
                    _ => $"Windows Hello verification failed: {verification}."
                }
            };
        }

        try
        {
            byte[] wrapped = await File.ReadAllBytesAsync(_wrappedKeyPath);
            byte[] key = ProtectedData.Unprotect(wrapped, AdditionalEntropy, DataProtectionScope.CurrentUser);
            if (key.Length == 0)
            {
                return new WindowsHelloUnlockAttempt
                {
                    Outcome = WindowsHelloUnlockOutcome.KeyUnavailable,
                    Message = "Stored Windows Hello key was empty."
                };
            }

            return new WindowsHelloUnlockAttempt
            {
                Outcome = WindowsHelloUnlockOutcome.Success,
                Message = "Windows Hello verified.",
                Key = key
            };
        }
        catch
        {
            return new WindowsHelloUnlockAttempt
            {
                Outcome = WindowsHelloUnlockOutcome.KeyUnavailable,
                Message = "Stored Windows Hello key is invalid. Unlock once with password."
            };
        }
    }
}


