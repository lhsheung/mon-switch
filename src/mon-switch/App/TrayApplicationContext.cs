using System.Reflection;
using Microsoft.Win32;
using MonSwitch.Core;
using MonSwitch.Services;
using MonSwitch.UI;

namespace MonSwitch.App;

/// <summary>
/// The whole application: one tray icon, one context menu, one set of global hotkeys.
/// There is no main window at all. Leaving the tray means quitting - by design, so an
/// "invisible" process can never linger.
///
/// Everything is event driven:
///   * WM_HOTKEY arrives through <see cref="HotkeyService"/>,
///   * NotifyIcon events come from the shell,
///   * the menu's Opening event refreshes state, so there is no refresh timer.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsService _settings;
    private readonly DisplayModeService _display = new();
    private readonly HotkeyService _hotkeys = new();

    private readonly Icon _trayIcon;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();

    private readonly ToolStripMenuItem _miCurrent = new();
    private readonly ToolStripMenuItem _miCycle = new();
    private readonly ToolStripMenuItem _miDirectRoot = new();
    private readonly ToolStripMenuItem _miNotifications = new();
    private readonly ToolStripMenuItem _miSettings = new();
    private readonly ToolStripMenuItem _miAutoStart = new();
    private readonly ToolStripMenuItem _miLanguageRoot = new();
    private readonly ToolStripMenuItem _miAbout = new();
    private readonly ToolStripMenuItem _miExit = new();

    private readonly Dictionary<DisplayMode, ToolStripMenuItem> _directItems = new();
    private readonly Dictionary<AppLanguage, ToolStripMenuItem> _languageItems = new();

    /// <summary>One mini switcher per screen, when the feature is on.</summary>
    private readonly List<MiniSwitcherForm> _miniWindows = new();

    private SettingsForm? _openSettingsForm;
    private bool _switchInProgress;
    private bool _exitRequested;

    public TrayApplicationContext(SettingsService settings, bool openSettingsOnStart)
    {
        _settings = settings;

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        AppLog.Configure(_settings.Current.EnableLog);

        _trayIcon = AppIcons.LoadTrayIcon();
        BuildMenu();

        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            ContextMenuStrip = _menu,
            Visible = true,
        };

        _tray.DoubleClick += (_, _) => CycleNext(interactive: true);
        _tray.BalloonTipClicked += (_, _) => OpenSettings();

        // Windows logging off or restarting: drop the tray glyph and the global hotkeys so
        // nothing stays registered while the session is torn down. ExitThread() is deliberately
        // not called - the OS is about to end the message loop anyway.
        _hotkeys.SessionEnding += (_, _) =>
        {
            AppLog.Info("session ending, releasing tray icon and hotkeys");
            try
            {
                _tray.Visible = false;
            }
            catch
            {
                // Already gone.
            }

            _hotkeys.UnregisterAll();
        };

        ApplyLocalization();
        RegisterAllHotkeys(notifyFailures: true);
        SynchroniseAutoStart();

        // Rebuilt on display change rather than polled: monitors can be plugged in, removed or
        // re-detected at any time, and SystemEvents gives us that as an event.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        RebuildMiniSwitchers();

        ReportRepairedSettings();

        AppLog.Info($"{AppInfo.Name} {AppInfo.VersionText} started, current mode = {_display.GetCurrentMode()}");
        LogDisplayInventory();

        if (openSettingsOnStart)
        {
            OpenSettings();
        }
    }

    /// <summary>
    /// Writes what the CCD database reports. Only produced when the diagnostic log is on,
    /// which makes "why did the switch fail on my machine" answerable from a log file.
    /// </summary>
    private void LogDisplayInventory()
    {
        if (!AppLog.IsEnabled)
        {
            return;
        }

        // An independent count straight from WinForms, so a CCD failure can be told apart
        // from "this machine genuinely has one screen".
        AppLog.Info(
            "WinForms screen list: " + Screen.AllScreens.Length + " -> " +
            string.Join(" | ", Screen.AllScreens.Select(s => $"{s.DeviceName} {s.Bounds} primary={s.Primary}")));

        IReadOnlyList<DisplayTarget> targets = _display.EnumerateTargets();
        AppLog.Info($"displays detected: {targets.Count} total, {targets.Count(t => t.Available)} available");

        // Only the attached outputs are worth logging; the CCD database also carries every
        // historically known output, which would drown the useful lines.
        foreach (DisplayTarget target in targets.Where(t => t.Available || t.Active))
        {
            AppLog.Info(
                $"  '{target.FriendlyName}' [{target.GdiDeviceName}] " +
                $"tech=0x{target.OutputTechnology:X8} internal={target.IsInternalPanel} " +
                $"available={target.Available} active={target.Active}");
        }
    }

    // ---------------------------------------------------------------- icon & menu

    private static Icon LoadTrayIcon()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("MonSwitch.app.ico");

            if (stream is not null)
            {
                // Icon(Stream, Size) picks the best matching size from the multi-resolution .ico,
                // so the tray glyph stays crisp at 100% - 200% DPI.
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("embedded tray icon could not be loaded: " + ex.Message);
        }

        return SystemIcons.Application;
    }

    private void BuildMenu()
    {
        _miCycle.Click += (_, _) => CycleNext(interactive: true);

        foreach (DisplayMode mode in DisplayModes.Selectable)
        {
            var item = new ToolStripMenuItem { Tag = mode };
            DisplayMode captured = mode;
            item.Click += (_, _) => SwitchTo(captured, interactive: true);
            _directItems[mode] = item;
            _miDirectRoot.DropDownItems.Add(item);
        }

        _miNotifications.Click += (_, _) => ToggleNotifications();
        _miSettings.Click += (_, _) => OpenSettings();
        _miAutoStart.Click += (_, _) => ToggleAutoStart();

        foreach (AppLanguage language in AppLanguages.All)
        {
            var item = new ToolStripMenuItem { Tag = language };
            AppLanguage captured = language;
            item.Click += (_, _) => ChangeLanguage(captured);
            _languageItems[language] = item;
            _miLanguageRoot.DropDownItems.Add(item);
        }

        _miAbout.Click += (_, _) => ShowAbout();
        _miExit.Click += (_, _) => ExitApplication();

        _menu.ShowImageMargin = false;
        _menu.Items.AddRange(
        [
            _miCurrent,
            new ToolStripSeparator(),
            _miCycle,
            _miDirectRoot,
            new ToolStripSeparator(),
            _miNotifications,
            _miSettings,
            _miAutoStart,
            _miLanguageRoot,
            new ToolStripSeparator(),
            _miAbout,
            _miExit,
        ]);

        // Refresh on open instead of on a timer.
        _menu.Opening += (_, _) => RefreshMenuState();
    }

    private void ApplyLocalization()
    {
        AppSettings current = _settings.Current;

        _tray.Text = Truncate(Loc.T("app.trayTooltip"), 62);

        _miCycle.Text = Loc.T("tray.menu.cycleNow");
        _miCycle.ShortcutKeyDisplayString = current.CycleHotkey.IsActive
            ? current.CycleHotkey.ToDisplayString()
            : null;

        _miDirectRoot.Text = Loc.T("tray.menu.directHeader");
        foreach (KeyValuePair<DisplayMode, ToolStripMenuItem> pair in _directItems)
        {
            pair.Value.Text = pair.Key.Label();
            HotkeyBinding binding = current.GetDirectHotkey(pair.Key);
            pair.Value.ShortcutKeyDisplayString = binding.IsActive ? binding.ToDisplayString() : null;
        }

        _miNotifications.Text = Loc.T("tray.menu.notifications");
        _miSettings.Text = Loc.T("tray.menu.settings");
        _miAutoStart.Text = Loc.T("tray.menu.autoStart");
        _miLanguageRoot.Text = Loc.T("tray.menu.language");

        foreach (KeyValuePair<AppLanguage, ToolStripMenuItem> pair in _languageItems)
        {
            pair.Value.Text = LocalizationService.DescribeLabel(pair.Key);
        }

        _miAbout.Text = Loc.T("tray.menu.about", AppInfo.Name);
        _miExit.Text = Loc.T("tray.menu.exit");

        _menu.Refresh();
        RefreshMenuState();

        // Runs last: the switcher text is built from the same lookups, so it has to be rebuilt
        // after the language has actually changed.
        RefreshMiniSwitchers();
    }

    private void RefreshMenuState()
    {
        DisplayMode mode = _display.GetCurrentMode();
        _miCurrent.Text = Loc.T(
            "tray.menu.currentMode",
            mode.IsSelectable() ? mode.Label() : Loc.T(DisplayMode.Unknown.LabelKey()));

        foreach (KeyValuePair<DisplayMode, ToolStripMenuItem> pair in _directItems)
        {
            pair.Value.Checked = pair.Key == mode;
        }

        _miNotifications.Checked = _settings.Current.ShowNotifications;
        _miAutoStart.Checked = AutoStartService.IsRegistered;

        foreach (KeyValuePair<AppLanguage, ToolStripMenuItem> pair in _languageItems)
        {
            pair.Value.Checked = pair.Key == LocalizationService.Instance.Configured;
        }
    }

    // ---------------------------------------------------------------- actions

    /// <summary>Walks the user-defined cycle. Used by the tray double-click and the cycle hotkey.</summary>
    private void CycleNext(bool interactive)
    {
        AppSettings current = _settings.Current;

        if (interactive && !current.DoubleClickCycles)
        {
            AppLog.Info("double-click ignored: cycling is switched off");
            return;
        }

        List<DisplayMode> order = current.GetCycleOrder();
        if (order.Count == 0)
        {
            Notify(
                Loc.T("notify.noCycleModesTitle"),
                Loc.T("notify.noCycleModes"),
                ToolTipIcon.Warning,
                force: true);
            return;
        }

        int index = order.IndexOf(_display.GetCurrentMode());
        DisplayMode next = order[(index + 1) % order.Count];
        SwitchTo(next, interactive);
    }

    private void SwitchTo(DisplayMode mode, bool interactive)
    {
        if (_switchInProgress)
        {
            AppLog.Info("switch ignored: another switch is still running");
            return;
        }

        if (mode == _display.GetCurrentMode())
        {
            if (interactive)
            {
                Notify(Loc.T("notify.alreadySwitchedTitle"), Loc.T("notify.alreadySwitched", mode.Label()));
            }

            return;
        }

        _switchInProgress = true;
        try
        {
            if (_display.TrySwitch(mode, out string error))
            {
                Notify(Loc.T("notify.switchedTitle"), Loc.T("notify.switched", mode.Label()));
            }
            else
            {
                // A failed switch is important enough to survive the "no notifications" setting.
                Notify(
                    Loc.T("notify.switchFailedTitle"),
                    Loc.T("notify.switchFailed", error),
                    ToolTipIcon.Warning,
                    force: true);
            }
        }
        finally
        {
            _switchInProgress = false;

            // Either way the mini switcher has to be brought up to date: a switch changes the
            // mode it shows, and a failed switch leaves the mode it shows correct but stale.
            RefreshMiniSwitchers();
        }
    }

    private void ToggleNotifications()
    {
        AppSettings current = _settings.Current;
        current.ShowNotifications = !current.ShowNotifications;
        _settings.Save(current, out _);
        ApplyLocalization();
    }

    private void ToggleAutoStart()
    {
        AppSettings current = _settings.Current;
        bool desired = !current.AutoStart;

        if (AutoStartService.TrySet(desired, out string? error))
        {
            current.AutoStart = desired;
            _settings.Save(current, out _);
            ApplyLocalization();
        }
        else
        {
            Notify(
                Loc.T("notify.autoStartTitle"),
                Loc.T("notify.autoStartFailed", error ?? string.Empty),
                ToolTipIcon.Warning,
                force: true);
        }
    }

    private void ChangeLanguage(AppLanguage language)
    {
        AppSettings current = _settings.Current;
        if (string.Equals(current.Language, language.ToCode(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        current.Language = language.ToCode();
        _settings.Save(current, out _);

        // Raises LanguageChanged, which rebuilds this menu and any open dialog.
        LocalizationService.Instance.Apply(language);
        AppLog.Info("language switched to " + language.ToCode());
    }

    private void ShowAbout()
    {
        using var form = new AboutForm();
        form.ShowDialog();
    }

    // ---------------------------------------------------------------- mini switcher

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        AppLog.Info("display settings changed, rebuilding the mini switcher windows");

        // Worst case the callback arrives off the UI thread, and touching a Form from there is
        // not allowed. Marshalling through the sink keeps every window on one thread.
        _hotkeys.RunOnUiThread(RebuildMiniSwitchers);
    }

    /// <summary>
    /// Creates, removes or repositions the mini switchers so they match the current setting and
    /// the current set of screens. Safe to call repeatedly.
    /// </summary>
    private void RebuildMiniSwitchers()
    {
        CloseMiniSwitchers();

        AppSettings current = _settings.Current;
        if (!current.MiniSwitcher)
        {
            return;
        }

        Screen[] screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            return;
        }

        IEnumerable<Screen> wanted = current.MiniOnEveryScreen
            ? screens
            : [screens.FirstOrDefault(s => s.Primary) ?? screens[0]];

        foreach (Screen screen in wanted)
        {
            MiniSwitcherForm form = CreateMiniSwitcher(screen);
            _miniWindows.Add(form);
        }

        AppLog.Info($"mini switcher: {_miniWindows.Count} window(s) shown");
        RefreshMiniSwitchers();
    }

    private MiniSwitcherForm CreateMiniSwitcher(Screen screen)
    {
        AppSettings current = _settings.Current;
        string key = screen.DeviceName;

        var form = new MiniSwitcherForm(
            key,
            Point.Empty,
            current.MiniAlwaysOnTop,
            current.MiniOpacity);

        form.SetContent(MiniSwitcherText());

        form.Location = PlacementFor(screen, form);

        form.RequestCycle += (_, _) => CycleNext(interactive: true);
        form.RequestMenu += (_, point) =>
        {
            // The right-click menu is the same one the tray uses, shown at the click point.
            RefreshMenuState();
            _menu.Show(form, point);
        };
        form.Moved += (_, _) =>
        {
            current.MiniPositions[key] = new MiniPlacement { X = form.Left, Y = form.Top };
            _settings.Save(current, out _);
            AppLog.Info($"mini switcher moved on {key} to ({form.Left}, {form.Top})");
        };

        form.Show();
        return form;
    }

    private Point PlacementFor(Screen screen, MiniSwitcherForm form)
    {
        AppSettings current = _settings.Current;

        if (current.MiniPositions.TryGetValue(screen.DeviceName, out MiniPlacement? saved) && saved is not null)
        {
            // A remembered position is honoured only while it still lands on a screen: after a
            // monitor is unplugged the old coordinates can point into dead space.
            if (Screen.AllScreens.Any(s => s.Bounds.Contains(new Point(saved.X, saved.Y))))
            {
                return new Point(saved.X, saved.Y);
            }
        }

        Rectangle area = screen.WorkingArea;
        int x = area.Right - form.Width - 12;
        int y = area.Bottom - form.Height - 12;
        return new Point(Math.Max(area.Left + 4, x), Math.Max(area.Top + 4, y));
    }

    /// <summary>Refreshes the text on every mini switcher, for example after a switch.</summary>
    private void RefreshMiniSwitchers()
    {
        if (_miniWindows.Count == 0)
        {
            return;
        }

        string text = MiniSwitcherText();
        foreach (MiniSwitcherForm form in _miniWindows)
        {
            form.SetContent(text);
        }
    }

    private string MiniSwitcherText()
    {
        DisplayMode mode = _display.GetCurrentMode();

        // Same guard the tray menu uses: an unrecognised topology must read as "unknown" rather
        // than as whichever enum value happens to match.
        string label = mode.IsSelectable() ? mode.Label() : Loc.T(DisplayMode.Unknown.LabelKey());
        return Loc.T("mini.label", label);
    }

    private void ApplyMiniLook()
    {
        AppSettings current = _settings.Current;
        foreach (MiniSwitcherForm form in _miniWindows)
        {
            form.ApplyLook(current.MiniAlwaysOnTop, current.MiniOpacity);
        }
    }

    private void CloseMiniSwitchers()
    {
        foreach (MiniSwitcherForm form in _miniWindows)
        {
            form.Hide();
            form.Dispose();
        }

        _miniWindows.Clear();
    }

    private void OpenSettings()
    {
        if (_openSettingsForm is not null)
        {
            _openSettingsForm.Activate();
            return;
        }

        // The dialog is created per opening and disposed in the finally block:
        // mon-switch never keeps a hidden window parked in memory.
        var form = new SettingsForm(_settings, _display, ApplyCurrentSettings);
        _openSettingsForm = form;

        try
        {
            form.ShowDialog();
        }
        catch (Exception ex)
        {
            // A defect inside the dialog must never take the tray process down with it - the
            // app is expected to keep switching modes even if its settings window is broken.
            AppLog.Always("settings dialog failed", ex);
            Notify(
                Loc.T("err.unhandledTitle"),
                Loc.T("err.unhandled", ex.Message),
                ToolTipIcon.Error,
                force: true);
        }
        finally
        {
            _openSettingsForm = null;
            form.Dispose();
        }
    }

    /// <summary>
    /// Called by the settings dialog right after it saves. Re-registers hotkeys under the new
    /// bindings and returns the localised failures so the dialog can show them.
    /// </summary>
    private IReadOnlyList<string> ApplyCurrentSettings()
    {
        AppLog.Configure(_settings.Current.EnableLog);

        // The dialog has already written settings.json at this point, so the Run entry has to be
        // brought in line here as well. Without this call the tick box only reached the JSON
        // file: nothing was written to the registry, the dialog then reported "disabled" from
        // its own state label, and auto-start could never be turned on.
        SynchroniseAutoStart(force: true);

        // Rebuilt rather than tweaked: the switcher may have been switched on or off, moved to
        // "every screen", or given a new opacity, and screens may have appeared or gone.
        RebuildMiniSwitchers();

        IReadOnlyList<string> failures = RegisterAllHotkeys(notifyFailures: false);

        // Runs after the sync above so the tray tick and the dialog's state label both read the
        // registry as it now stands.
        ApplyLocalization();
        return failures;
    }

    /// <summary>Re-registers every configured hotkey. Returns the localised failure list.</summary>
    private IReadOnlyList<string> RegisterAllHotkeys(bool notifyFailures)
    {
        _hotkeys.UnregisterAll();

        var failures = new List<string>();
        AppSettings current = _settings.Current;

        if (!_hotkeys.TryRegister(current.CycleHotkey, "cycle", () => CycleNext(interactive: false), out string? cycleError))
        {
            failures.Add($"{Loc.T("hotkey.cycle")}: {cycleError}");
        }

        foreach (DisplayMode mode in DisplayModes.Selectable)
        {
            DisplayMode captured = mode;
            HotkeyBinding binding = current.GetDirectHotkey(captured);

            if (!_hotkeys.TryRegister(binding, "direct." + captured.ToId(), () => SwitchTo(captured, interactive: true), out string? error))
            {
                failures.Add($"{Loc.T(captured.HotkeyLabelKey())}: {error}");
            }
        }

        if (failures.Count > 0 && notifyFailures)
        {
            Notify(
                Loc.T("notify.hotkeyFailedTitle"),
                string.Join(Environment.NewLine, failures),
                ToolTipIcon.Warning,
                force: true);
        }

        return failures;
    }

    /// <param name="force">
    /// True when the user just asked for this in the settings dialog, which should override a
    /// "disabled" flag sitting in Windows' own startup list.
    /// </param>
    private void SynchroniseAutoStart(bool force = false)
    {
        AppSettings current = _settings.Current;

        if (AutoStartService.TrySynchronise(current.AutoStart, force, out string? error))
        {
            return;
        }

        // The registry disagreed and we could not fix it - make the stored value honest.
        if (current.AutoStart)
        {
            current.AutoStart = false;
            _settings.Save(current, out _);
        }

        Notify(
            Loc.T("notify.autoStartTitle"),
            Loc.T("notify.autoStartFailed", error ?? string.Empty),
            ToolTipIcon.Warning,
            force: true);
    }

    private void ReportRepairedSettings()
    {
        if (!_settings.WasRepaired)
        {
            return;
        }

        string detail = _settings.RepairBackupPath is null
            ? Loc.T("notify.settingsResetNoBackup", _settings.LastError ?? string.Empty)
            : Loc.T("notify.settingsReset", _settings.RepairBackupPath);

        Notify(
            Loc.T("notify.settingsResetTitle"),
            detail,
            ToolTipIcon.Warning,
            force: true);

        // Rewrite a clean file immediately, so the next launch is quiet.
        _settings.Save(_settings.Current, out _);
    }

    /// <summary>Shows a tray balloon. Failures pass <c>force: true</c> to bypass the setting.</summary>
    private void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, bool force = false)
    {
        if (!force && !_settings.Current.ShowNotifications)
        {
            return;
        }

        try
        {
            _tray.ShowBalloonTip(5000, title, text, icon);
        }
        catch (Exception ex)
        {
            AppLog.Warn("tray balloon could not be shown: " + ex.Message);
        }
    }

    /// <summary>Lets the single-instance guard open Settings from a thread-pool callback.</summary>
    public void RequestOpenSettings() => _hotkeys.RunOnUiThread(OpenSettings);

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    private void ExitApplication()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        AppLog.Info("exit requested");

        // Remove the icon before the message loop stops, otherwise Windows can leave a
        // ghost glyph in the notification area until the user hovers over it.
        try
        {
            _tray.Visible = false;
        }
        catch (Exception ex)
        {
            AppLog.Warn("tray icon removal failed: " + ex.Message);
        }

        ExitThread();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            // The mini switchers are real top-level windows; leaving them alive would keep the
            // message loop from ever ending, which is exactly the "invisible process" this
            // application is designed not to have.
            CloseMiniSwitchers();

            try
            {
                _tray.Visible = false;
            }
            catch
            {
                // Already hidden - nothing to do.
            }

            _tray.Dispose();
            _hotkeys.Dispose();
            _menu.Dispose();

            if (!ReferenceEquals(_trayIcon, SystemIcons.Application))
            {
                _trayIcon.Dispose();
            }

            AppLog.Info("resources released");
        }

        base.Dispose(disposing);
    }
}
