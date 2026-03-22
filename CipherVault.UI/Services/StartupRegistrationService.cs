using Microsoft.Win32;

namespace CipherVault.UI.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CipherVault";

    public void Configure(bool enabled)
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (runKey == null)
            throw new InvalidOperationException("Unable to access Windows startup registry key.");

        if (!enabled)
        {
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("Unable to resolve application executable path.");

        string command = $"\"{processPath}\"";
        runKey.SetValue(ValueName, command, RegistryValueKind.String);
    }
}

