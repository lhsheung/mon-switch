using MonSwitch.Core;
using MonSwitch.Native;
using MonSwitch.Services;

namespace MonSwitch.UI;

/// <summary>
/// Read-only text box that records a key combination.
///
/// Behaviour borrowed from SoundSwitch's HotKeyTextBox:
///   * click or tab into it and the next chord is captured;
///   * Backspace / Delete / Clear removes the assignment ("clear");
///   * Escape abandons recording without touching the current value;
///   * a bare key (no modifier) is refused unless it is an F-key, because otherwise the
///     hotkey would swallow that key for every application on the machine.
/// </summary>
internal sealed class HotkeyInputBox : TextBox
{
    private HotkeyBinding _binding = new();
    private bool _recording;

    public HotkeyInputBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        TextAlign = HorizontalAlignment.Left;
        Cursor = Cursors.Hand;
        BackColor = SystemColors.Window;
    }

    public event EventHandler? BindingChanged;

    public HotkeyBinding Binding
    {
        get => _binding;
        set
        {
            _binding = value ?? new HotkeyBinding();
            UpdateDisplay();
            BindingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Assigns without raising <see cref="BindingChanged"/> - used while filling the form.</summary>
    public void SetBindingSilently(HotkeyBinding? binding)
    {
        _binding = binding ?? new HotkeyBinding();
        UpdateDisplay();
    }

    public void Relocalize() => UpdateDisplay();

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        StartRecording();
    }

    protected override void OnLeave(EventArgs e)
    {
        StopRecording();
        base.OnLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!_recording)
        {
            Focus();
            StartRecording();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            StopRecording();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode is Keys.Back or Keys.Delete or Keys.Clear)
        {
            _binding.Clear();
            UpdateDisplay();
            BindingChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (IsModifierKey(e.KeyCode))
        {
            // Wait for the key that completes the chord.
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        uint virtualKey = (uint)(e.KeyCode & Keys.KeyCode);
        uint modifiers = CurrentModifiers();

        if (modifiers == 0 && !IsFunctionKey(virtualKey))
        {
            // Refused: see the class comment. The hint label explains this to the user.
            return;
        }

        _binding = new HotkeyBinding
        {
            Enabled = true,
            Modifiers = modifiers,
            VirtualKey = virtualKey,
        };

        StopRecording();
        UpdateDisplay();
        BindingChanged?.Invoke(this, EventArgs.Empty);

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    /// <summary>Also catches the case where the user only holds modifier keys.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_recording && IsModifierKey(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyUp(e);
    }

    private static bool IsModifierKey(Keys key) => key is
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;

    private static bool IsFunctionKey(uint virtualKey) => virtualKey is >= 0x70 and <= 0x87;

    /// <summary>
    /// WinForms' ModifierKeys does not report the Windows key, so the Win flag is polled
    /// from the async key state at the moment the chord completes.
    /// </summary>
    private static uint CurrentModifiers()
    {
        uint modifiers = 0;

        if ((NativeMethods.GetKeyState(NativeMethods.VkControl) & 0x8000) != 0)
        {
            modifiers |= HotkeyBinding.ModControl;
        }

        if ((NativeMethods.GetKeyState(NativeMethods.VkMenu) & 0x8000) != 0)
        {
            modifiers |= HotkeyBinding.ModAlt;
        }

        if ((NativeMethods.GetKeyState(NativeMethods.VkShift) & 0x8000) != 0)
        {
            modifiers |= HotkeyBinding.ModShift;
        }

        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VkLWin) & 0x8000) != 0 ||
            (NativeMethods.GetAsyncKeyState(NativeMethods.VkRWin) & 0x8000) != 0)
        {
            modifiers |= HotkeyBinding.ModWin;
        }

        return modifiers;
    }

    private void StartRecording()
    {
        if (_recording)
        {
            return;
        }

        _recording = true;
        BackColor = Color.FromArgb(255, 249, 219);
        Text = Loc.T("hotkey.recording");
    }

    private void StopRecording()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        BackColor = SystemColors.Window;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_recording)
        {
            Text = Loc.T("hotkey.recording");
            return;
        }

        ForeColor = _binding.IsActive ? SystemColors.ControlText : SystemColors.GrayText;
        Text = _binding.HasKey ? _binding.ToDisplayString() : Loc.T("hotkey.none");
    }
}
