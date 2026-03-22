using Microsoft.Extensions.Logging;

namespace CipherVault.Core.Services;

/// <summary>
/// Detects idle time and fires a lock event after the configured timeout.
/// </summary>
public class AutoLockService : IDisposable
{
    public event EventHandler? LockRequested;

    private readonly ILogger<AutoLockService> _logger;
    private System.Timers.Timer? _timer;
    private DateTime _lastActivity = DateTime.UtcNow;
    private int _lockAfterMinutes = 5;
    private bool _disposed;

    public AutoLockService(ILogger<AutoLockService> logger)
    {
        _logger = logger;
    }

    public void Start(int lockAfterMinutes)
    {
        _lockAfterMinutes = lockAfterMinutes;
        _lastActivity = DateTime.UtcNow;

        _timer?.Stop();
        _timer?.Dispose();

        _timer = new System.Timers.Timer(30_000); // Check every 30s
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
    }

    public void RecordActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if ((DateTime.UtcNow - _lastActivity).TotalMinutes >= _lockAfterMinutes)
        {
            _logger.LogInformation("Auto-lock triggered after {Minutes} minutes of inactivity.", _lockAfterMinutes);
            Stop();
            LockRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _disposed = true;
        }
    }
}

