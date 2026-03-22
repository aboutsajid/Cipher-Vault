using System.Windows;
using Microsoft.Extensions.Logging;

namespace CipherVault.Core.Services;

/// <summary>
/// Copies text to clipboard and automatically clears it after a configurable delay.
/// Only clears if the clipboard still contains the app's last copied value.
/// </summary>
public class SecureClipboardService
{
    private readonly ILogger<SecureClipboardService> _logger;
    private string? _lastCopiedValue;
    private CancellationTokenSource? _clearCts;

    public SecureClipboardService(ILogger<SecureClipboardService> logger)
    {
        _logger = logger;
    }

    public void CopyAndScheduleClear(string value, int clearAfterSeconds)
    {
        _clearCts?.Cancel();
        _clearCts?.Dispose();

        Application.Current.Dispatcher.Invoke(() =>
        {
            Clipboard.SetText(value);
            _lastCopiedValue = value;
        });

        _logger.LogInformation("Copied to clipboard. Will clear after {Seconds}s.", clearAfterSeconds);

        _clearCts = new CancellationTokenSource();
        var token = _clearCts.Token;
        var lastVal = value;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(clearAfterSeconds), token);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Only clear if clipboard still contains our value
                    try
                    {
                        string current = Clipboard.GetText();
                        if (current == lastVal)
                        {
                            Clipboard.Clear();
                            _lastCopiedValue = null;
                            _logger.LogInformation("Clipboard auto-cleared.");
                        }
                    }
                    catch { /* Clipboard might be locked */ }
                });
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);
    }

    public void ClearNow()
    {
        _clearCts?.Cancel();
        Application.Current.Dispatcher.Invoke(() =>
        {
            try { Clipboard.Clear(); }
            catch { }
            _lastCopiedValue = null;
        });
    }
}

