namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Threading;

/// <summary>
/// Single-instance guard using a named Mutex (more robust than the Java
/// FileLock approach). Prevents multiple instances of the dock from running.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\MobreadModernDock_SingleInstance";
    private Mutex? _mutex;
    private bool _owned;

    /// <summary>
    /// Attempts to acquire the single-instance lock.
    /// Returns true if this is the first instance, false if another is already running.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out _owned);
        if (!_owned)
        {
            // Another instance owns the mutex — release our reference.
            _mutex.Dispose();
            _mutex = null;
        }
        return _owned;
    }

    public void Dispose()
    {
        if (_mutex != null && _owned)
        {
            try { _mutex.ReleaseMutex(); } catch { /* best effort */ }
        }
        _mutex?.Dispose();
        _mutex = null;
        _owned = false;
    }
}
