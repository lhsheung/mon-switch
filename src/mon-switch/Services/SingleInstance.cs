using System.Threading;
using MonSwitch.Core;

namespace MonSwitch.Services;

/// <summary>
/// Keeps exactly one tray instance per Windows session.
///
/// A second launch does not pop up an error: it signals the running instance through a named
/// event, so "launch the exe again" behaves like "show my settings". The wait is registered
/// against the event handle, i.e. it costs one blocked thread-pool wait and zero polling.
///
/// The "Local\" prefix matters: display topology is per-session, so a second user signed in
/// over RDP gets their own instance and their own tray icon.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _wait;

    public SingleInstance()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, AppInfo.MutexName, out bool createdNew);
            IsFirstInstance = createdNew;

            if (createdNew)
            {
                _signal = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.SignalName);
                _wait = ThreadPool.RegisterWaitForSingleObject(
                    _signal,
                    (_, _) => ActivateRequested?.Invoke(this, EventArgs.Empty),
                    state: null,
                    millisecondsTimeOutInterval: Timeout.Infinite,
                    executeOnlyOnce: false);
            }
            else
            {
                NotifyRunningInstance();
            }
        }
        catch (Exception ex)
        {
            // A broken guard must never stop the app from starting.
            AppLog.Warn("single-instance guard failed, continuing anyway: " + ex.Message);
            IsFirstInstance = true;
        }
    }

    public bool IsFirstInstance { get; private set; }

    /// <summary>Raised on a thread-pool thread when a second launch asks for the settings window.</summary>
    public event EventHandler? ActivateRequested;

    private static void NotifyRunningInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(AppInfo.SignalName);
            signal.Set();
            AppLog.Info("another instance is running; asked it to open Settings");
        }
        catch (Exception ex)
        {
            AppLog.Warn("could not reach the running instance: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _wait = null;

        _signal?.Dispose();
        _signal = null;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread, or we never owned it - process exit releases it anyway.
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
