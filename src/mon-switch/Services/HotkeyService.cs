using System.ComponentModel;
using System.Runtime.InteropServices;
using MonSwitch.Core;
using MonSwitch.Native;

namespace MonSwitch.Services;

/// <summary>
/// Registers global hotkeys with <c>RegisterHotKey</c> and dispatches WM_HOTKEY.
///
/// The message sink is an invisible 1x1 form that is never shown (SetVisibleCore is pinned
/// to false) and is disposed together with the service. It exists for exactly one reason:
/// a window handle is required to receive WM_HOTKEY, and a tray app has no main window.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private sealed class HotkeySinkForm : Form
    {
        public event Action<int>? HotkeyMessage;

        /// <summary>Raised on WM_ENDSESSION, i.e. Windows is logging off or restarting.</summary>
        public event EventHandler? SessionEnding;

        public HotkeySinkForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Text = AppInfo.Name + " message sink";
            Visible = false;
        }

        /// <summary>Guarantees the sink can never become visible, whatever calls Show().</summary>
        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WmHotkey:
                    HotkeyMessage?.Invoke(m.WParam.ToInt32());
                    return;

                case NativeMethods.WmQueryEndSession:
                    // Answer "yes, go ahead" so Windows never waits on a background tray app.
                    m.Result = new IntPtr(1);
                    return;

                case NativeMethods.WmEndSession:
                    SessionEnding?.Invoke(this, EventArgs.Empty);
                    break;
            }

            base.WndProc(ref m);
        }
    }

    private sealed record Registration(int Id, string OwnerKey, HotkeyBinding Binding, Action Callback);

    private readonly HotkeySinkForm _sink;
    private readonly Dictionary<int, Registration> _registrations = new();
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyService()
    {
        _sink = new HotkeySinkForm();
        _ = _sink.Handle;                       // create the handle on the UI thread, never show it
        _sink.HotkeyMessage += OnHotkeyMessage;
        _sink.SessionEnding += OnSinkSessionEnding;
    }

    private void OnSinkSessionEnding(object? sender, EventArgs e) =>
        SessionEnding?.Invoke(this, EventArgs.Empty);

    /// <summary>Number of hotkeys currently held. Used by the acceptance checklist.</summary>
    public int RegisteredCount => _registrations.Count;

    /// <summary>
    /// Raised when Windows is logging off or restarting. The subscriber should release shell
    /// resources (tray icon, hotkeys) but must not try to stop the message loop.
    /// </summary>
    public event EventHandler? SessionEnding;

    /// <summary>
    /// Registers one binding. A disabled or unassigned binding is a no-op success.
    /// On failure <paramref name="errorMessage"/> carries a localised reason
    /// (occupied vs. refused) instead of a raw Win32 code.
    /// </summary>
    public bool TryRegister(HotkeyBinding binding, string ownerKey, Action callback, out string? errorMessage)
    {
        errorMessage = null;

        if (!binding.IsActive)
        {
            return true;
        }

        if (_disposed)
        {
            errorMessage = Loc.T("notify.hotkeyFailed", binding.ToDisplayString(), "service disposed");
            return false;
        }

        int id = _nextId++;

        if (NativeMethods.RegisterHotKey(
                _sink.Handle,
                id,
                binding.Modifiers | HotkeyBinding.ModNoRepeat,
                binding.VirtualKey))
        {
            _registrations[id] = new Registration(id, ownerKey, binding, callback);
            AppLog.Info($"hotkey registered: {ownerKey} = {binding.ToDisplayString()} (#{id})");
            return true;
        }

        int code = Marshal.GetLastWin32Error();
        errorMessage = code == NativeMethods.ErrorHotkeyAlreadyRegistered
            ? Loc.T("notify.hotkeyConflict", binding.ToDisplayString())
            : Loc.T("notify.hotkeyFailed", binding.ToDisplayString(), SafeMessage(code));

        AppLog.Warn($"RegisterHotKey refused {ownerKey} = {binding.ToDisplayString()}: code={code}");
        return false;
    }

    /// <summary>Releases every hotkey. Called before each re-registration and on exit.</summary>
    public void UnregisterAll()
    {
        foreach (int id in _registrations.Keys)
        {
            NativeMethods.UnregisterHotKey(_sink.Handle, id);
        }

        if (_registrations.Count > 0)
        {
            AppLog.Info($"released {_registrations.Count} hotkey(s)");
        }

        _registrations.Clear();
        _nextId = 1;
    }

    /// <summary>Runs an action on the UI thread, using the sink as the marshalling anchor.</summary>
    public void RunOnUiThread(Action action)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_sink.IsHandleCreated && _sink.InvokeRequired)
            {
                _sink.BeginInvoke(action);
                return;
            }
        }
        catch (Exception ex)
        {
            // Marshalling is best-effort here; falling through to a direct call beats throwing
            // from a thread-pool callback, which would be fatal.
            AppLog.Warn("UI marshalling failed, running inline: " + ex.Message);
        }

        action();
    }

    private void OnHotkeyMessage(int id)
    {
        if (!_registrations.TryGetValue(id, out Registration? registration))
        {
            return;
        }

        try
        {
            registration.Callback();
        }
        catch (Exception ex)
        {
            // A misbehaving handler must not tear down the message loop.
            AppLog.Error($"hotkey handler '{registration.OwnerKey}' threw", ex);
        }
    }

    private static string SafeMessage(int code)
    {
        try
        {
            return new Win32Exception(code).Message;
        }
        catch
        {
            return "0x" + code.ToString("X8");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();

        try
        {
            _sink.HotkeyMessage -= OnHotkeyMessage;
            _sink.SessionEnding -= OnSinkSessionEnding;
            _sink.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("hotkey sink disposal failed: " + ex.Message);
        }
    }
}
