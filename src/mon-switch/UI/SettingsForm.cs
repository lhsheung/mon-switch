using System.Diagnostics;
using MonSwitch.Core;
using MonSwitch.Services;

namespace MonSwitch.UI;

/// <summary>
/// The one dialog mon-switch has. Built in code rather than with the designer so the whole
/// layout is reviewable in a diff and can be re-localised at runtime.
///
/// Lifecycle: created per opening by <c>TrayApplicationContext.OpenSettings</c> and disposed
/// in a finally block. Nothing here stays alive after the dialog closes.
/// </summary>
internal sealed class SettingsForm : Form
{
    private sealed record LanguageEntry(AppLanguage Language)
    {
        public override string ToString() => LocalizationService.DescribeLabel(Language);
    }

    private sealed record CycleEntry(DisplayMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record HotkeyRow(Label Label, CheckBox Toggle, HotkeyInputBox Box, Button Clear);

    private readonly SettingsService _settingsService;
    private readonly DisplayModeService _displayService;
    private readonly Func<IReadOnlyList<string>>? _applyAction;

    /// <summary>A working copy: nothing is written until Apply or OK.</summary>
    private readonly AppSettings _draft;

    private readonly TabControl _tabs = new();
    private readonly TabPage _tabGeneral = new();
    private readonly TabPage _tabCycle = new();
    private readonly TabPage _tabHotkeys = new();
    private readonly TabPage _tabAdvanced = new();

    private readonly CheckBox _cbDoubleClick = new();
    private readonly Label _lblDoubleClickHint = new();
    private readonly CheckBox _cbNotifications = new();
    private readonly Label _lblCurrentMode = new();

    private readonly GroupBox _gbStartup = new();
    private readonly CheckBox _cbAutoStart = new();
    private readonly Label _lblAutoStartState = new();

    private readonly GroupBox _gbMini = new();
    private readonly CheckBox _cbMiniEnable = new();
    private readonly CheckBox _cbMiniEveryScreen = new();
    private readonly CheckBox _cbMiniAlwaysOnTop = new();
    private readonly Label _lblMiniHint = new();

    private readonly Label _lblCycleHint = new();
    private readonly CheckedListBox _cycleList = new();
    private readonly Button _btnCycleUp = new();
    private readonly Button _btnCycleDown = new();
    private readonly Label _lblCycleState = new();

    private readonly Label _lblHotkeyHint = new();
    private readonly Label _lblHotkeyWarn = new();
    private readonly HotkeyRow _cycleRow;
    private readonly Dictionary<DisplayMode, HotkeyRow> _directRows = new();

    private readonly GroupBox _gbLanguage = new();
    private readonly Label _lblLanguage = new();
    private readonly ComboBox _cboLanguage = new();
    private readonly Label _lblLanguageHint = new();

    private readonly GroupBox _gbDiagnostics = new();
    private readonly CheckBox _cbEnableLog = new();
    private readonly LinkLabel _lnkSettingsFolder = new();
    private readonly Label _lblSettingsPath = new();
    private readonly LinkLabel _lnkLogFolder = new();
    private readonly Label _lblLogPath = new();

    private readonly Button _btnRestoreDefaults = new();
    private readonly Button _btnApply = new();
    private readonly Button _btnOk = new();
    private readonly Button _btnCancel = new();

    private bool _loading = true;
    private bool _suppressLanguageEvent;

    /// <summary>Set while the cycle list is rebuilt, so ItemCheck does not fire per row.</summary>
    private bool _suppressCycleEvents;

    public SettingsForm(
        SettingsService settingsService,
        DisplayModeService displayService,
        Func<IReadOnlyList<string>>? applyAction)
    {
        _settingsService = settingsService;
        _displayService = displayService;
        _applyAction = applyAction;
        _draft = settingsService.Current.Clone();

        BuildLayout();

        _cycleRow = BuildHotkeyRow(62);
        for (int i = 0; i < DisplayModes.Selectable.Length; i++)
        {
            _directRows[DisplayModes.Selectable[i]] = BuildHotkeyRow(62 + (34 * (i + 1)));
        }

        BuildLanguageCombo();
        LoadFromDraft();
        ApplyLocalization();

        _loading = false;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        ClientSize = new Size(676, 528);
        Icon = AppIcons.LoadWindowIcon();

        _tabs.Location = new Point(12, 12);
        _tabs.Size = new Size(652, 464);

        _tabGeneral.Text = Loc.T("settings.tab.general");
        _tabCycle.Text = Loc.T("settings.tab.cycle");
        _tabHotkeys.Text = Loc.T("settings.tab.hotkeys");
        _tabAdvanced.Text = Loc.T("settings.tab.advanced");
        _tabs.TabPages.AddRange([_tabGeneral, _tabCycle, _tabHotkeys, _tabAdvanced]);

        // --- General -------------------------------------------------------
        _cbDoubleClick.AutoSize = true;
        _cbDoubleClick.Location = new Point(18, 20);
        _cbDoubleClick.Size = new Size(600, 24);

        _lblDoubleClickHint.AutoSize = false;
        _lblDoubleClickHint.Location = new Point(36, 48);
        _lblDoubleClickHint.Size = new Size(590, 32);
        _lblDoubleClickHint.ForeColor = SystemColors.GrayText;

        _cbNotifications.AutoSize = true;
        _cbNotifications.Location = new Point(18, 86);
        _cbNotifications.Size = new Size(600, 24);

        _gbStartup.Location = new Point(18, 124);
        _gbStartup.Size = new Size(608, 104);

        _cbAutoStart.AutoSize = true;
        _cbAutoStart.Location = new Point(16, 28);
        _cbAutoStart.Size = new Size(576, 24);

        _lblAutoStartState.AutoSize = false;
        _lblAutoStartState.Location = new Point(18, 58);
        _lblAutoStartState.Size = new Size(574, 22);
        _lblAutoStartState.ForeColor = SystemColors.GrayText;

        _gbStartup.Controls.AddRange([_cbAutoStart, _lblAutoStartState]);

        // --- Mini switcher -------------------------------------------------
        // Placed after the startup group because it answers the question people actually hit:
        // "I am on the other monitor and cannot reach mon-switch".
        _gbMini.Location = new Point(18, 236);
        _gbMini.Size = new Size(608, 156);

        _cbMiniEnable.AutoSize = true;
        _cbMiniEnable.Location = new Point(16, 26);
        _cbMiniEnable.Size = new Size(576, 24);
        _cbMiniEnable.CheckedChanged += (_, _) =>
        {
            UpdateMiniEnabledState();
        };

        _cbMiniEveryScreen.AutoSize = true;
        _cbMiniEveryScreen.Location = new Point(16, 54);
        _cbMiniEveryScreen.Size = new Size(576, 24);

        _cbMiniAlwaysOnTop.AutoSize = true;
        _cbMiniAlwaysOnTop.Location = new Point(16, 82);
        _cbMiniAlwaysOnTop.Size = new Size(576, 24);

        _lblMiniHint.AutoSize = false;
        _lblMiniHint.Location = new Point(18, 110);
        _lblMiniHint.Size = new Size(572, 38);
        _lblMiniHint.ForeColor = SystemColors.GrayText;

        _gbMini.Controls.AddRange([_cbMiniEnable, _cbMiniEveryScreen, _cbMiniAlwaysOnTop, _lblMiniHint]);

        _lblCurrentMode.AutoSize = false;
        _lblCurrentMode.Location = new Point(18, 404);
        _lblCurrentMode.Size = new Size(608, 22);
        _lblCurrentMode.ForeColor = SystemColors.GrayText;

        _tabGeneral.Controls.AddRange(
        [
            _cbDoubleClick,
            _lblDoubleClickHint,
            _cbNotifications,
            _gbStartup,
            _gbMini,
            _lblCurrentMode,
        ]);

        // --- Cycle ---------------------------------------------------------
        _lblCycleHint.AutoSize = false;
        _lblCycleHint.Location = new Point(18, 16);
        _lblCycleHint.Size = new Size(608, 40);

        _cycleList.Location = new Point(18, 62);
        _cycleList.Size = new Size(468, 288);
        _cycleList.CheckOnClick = true;
        _cycleList.IntegralHeight = false;
        _cycleList.SelectionMode = SelectionMode.One;
        // ItemCheck fires *before* the new state is committed, so the summary label is refreshed
        // on the next message instead of inline. It must not run while the list is rebuilt from
        // the constructor: at that point the form has no window handle and BeginInvoke throws
        // InvalidOperationException. That crash only showed up when Settings was actually opened.
        _cycleList.ItemCheck += (_, _) => OnCycleItemCheck();

        _btnCycleUp.Location = new Point(498, 62);
        _btnCycleUp.Size = new Size(128, 34);
        _btnCycleUp.Click += (_, _) => MoveCycleItem(-1);

        _btnCycleDown.Location = new Point(498, 104);
        _btnCycleDown.Size = new Size(128, 34);
        _btnCycleDown.Click += (_, _) => MoveCycleItem(1);

        _lblCycleState.AutoSize = false;
        _lblCycleState.Location = new Point(18, 362);
        _lblCycleState.Size = new Size(608, 44);

        _tabCycle.Controls.AddRange(
        [
            _lblCycleHint,
            _cycleList,
            _btnCycleUp,
            _btnCycleDown,
            _lblCycleState,
        ]);

        // --- Hotkeys -------------------------------------------------------
        _lblHotkeyHint.AutoSize = false;
        _lblHotkeyHint.Location = new Point(18, 12);
        _lblHotkeyHint.Size = new Size(608, 42);

        _lblHotkeyWarn.AutoSize = false;
        _lblHotkeyWarn.Location = new Point(18, 268);
        _lblHotkeyWarn.Size = new Size(608, 60);
        _lblHotkeyWarn.ForeColor = SystemColors.GrayText;

        _tabHotkeys.Controls.AddRange([_lblHotkeyHint, _lblHotkeyWarn]);

        // --- Advanced ------------------------------------------------------
        _gbLanguage.Location = new Point(18, 16);
        _gbLanguage.Size = new Size(608, 142);

        _lblLanguage.AutoSize = true;
        _lblLanguage.Location = new Point(16, 26);

        _cboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboLanguage.Location = new Point(16, 50);
        _cboLanguage.Size = new Size(320, 26);
        _cboLanguage.SelectedIndexChanged += OnLanguageSelectionChanged;

        _lblLanguageHint.AutoSize = false;
        _lblLanguageHint.Location = new Point(16, 86);
        _lblLanguageHint.Size = new Size(574, 44);
        _lblLanguageHint.ForeColor = SystemColors.GrayText;

        _gbLanguage.Controls.AddRange([_lblLanguage, _cboLanguage, _lblLanguageHint]);

        _gbDiagnostics.Location = new Point(18, 170);
        _gbDiagnostics.Size = new Size(608, 208);

        _cbEnableLog.AutoSize = true;
        _cbEnableLog.Location = new Point(16, 26);
        _cbEnableLog.Size = new Size(576, 24);

        _lnkSettingsFolder.AutoSize = true;
        _lnkSettingsFolder.Location = new Point(14, 58);
        _lnkSettingsFolder.Click += (_, _) => OpenFolder(_settingsService.DirectoryPath);

        _lblSettingsPath.AutoSize = false;
        _lblSettingsPath.AutoEllipsis = true;
        _lblSettingsPath.Location = new Point(18, 82);
        _lblSettingsPath.Size = new Size(572, 20);
        _lblSettingsPath.ForeColor = SystemColors.GrayText;

        _lnkLogFolder.AutoSize = true;
        _lnkLogFolder.Location = new Point(14, 112);
        _lnkLogFolder.Click += (_, _) => OpenFolder(AppLog.DirectoryPath);

        _lblLogPath.AutoSize = false;
        _lblLogPath.AutoEllipsis = true;
        _lblLogPath.Location = new Point(18, 136);
        _lblLogPath.Size = new Size(572, 20);
        _lblLogPath.ForeColor = SystemColors.GrayText;

        _gbDiagnostics.Controls.AddRange(
        [
            _cbEnableLog,
            _lnkSettingsFolder,
            _lblSettingsPath,
            _lnkLogFolder,
            _lblLogPath,
        ]);

        _tabAdvanced.Controls.AddRange([_gbLanguage, _gbDiagnostics]);

        // --- Buttons -------------------------------------------------------
        _btnRestoreDefaults.Location = new Point(12, 486);
        _btnRestoreDefaults.Size = new Size(168, 32);
        _btnRestoreDefaults.Click += (_, _) => RestoreDefaults();

        _btnApply.Location = new Point(398, 486);
        _btnApply.Size = new Size(84, 32);
        _btnApply.Click += (_, _) => ApplyChanges();

        _btnOk.Location = new Point(488, 486);
        _btnOk.Size = new Size(84, 32);
        _btnOk.Click += (_, _) =>
        {
            if (ApplyChanges())
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        _btnCancel.Location = new Point(578, 486);
        _btnCancel.Size = new Size(86, 32);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnCancel.Click += (_, _) => Close();

        Controls.AddRange([_tabs, _btnRestoreDefaults, _btnApply, _btnOk, _btnCancel]);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        ResumeLayout(performLayout: true);
    }

    private HotkeyRow BuildHotkeyRow(int top)
    {
        var label = new Label
        {
            AutoSize = false,
            Location = new Point(18, top + 4),
            Size = new Size(226, 24),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var toggle = new CheckBox
        {
            AutoSize = true,
            Location = new Point(250, top + 5),
        };

        var box = new HotkeyInputBox
        {
            Location = new Point(276, top + 2),
            Size = new Size(192, 25),
        };

        var clear = new Button
        {
            Location = new Point(480, top + 1),
            Size = new Size(80, 27),
        };

        var row = new HotkeyRow(label, toggle, box, clear);

        toggle.CheckedChanged += (_, _) =>
        {
            box.Enabled = toggle.Checked;
            if (toggle.Checked && !_loading)
            {
                box.Focus();
            }
        };

        box.BindingChanged += (_, _) =>
        {
            if (box.Binding.HasKey)
            {
                toggle.Checked = true;
            }
        };

        clear.Click += (_, _) =>
        {
            box.Binding = new HotkeyBinding();
            toggle.Checked = false;
        };

        _tabHotkeys.Controls.AddRange([label, toggle, box, clear]);
        return row;
    }

    // ---------------------------------------------------------------- state

    private void LoadFromDraft()
    {
        _loading = true;

        _cbDoubleClick.Checked = _draft.DoubleClickCycles;
        _cbNotifications.Checked = _draft.ShowNotifications;
        _cbAutoStart.Checked = _draft.AutoStart;
        _cbEnableLog.Checked = _draft.EnableLog;
        _cbMiniEnable.Checked = _draft.MiniSwitcher;
        _cbMiniEveryScreen.Checked = _draft.MiniOnEveryScreen;
        _cbMiniAlwaysOnTop.Checked = _draft.MiniAlwaysOnTop;
        UpdateMiniEnabledState();

        _cycleRow.Box.SetBindingSilently(_draft.CycleHotkey.Clone());
        _cycleRow.Toggle.Checked = _draft.CycleHotkey.IsActive;
        _cycleRow.Box.Enabled = _cycleRow.Toggle.Checked;

        foreach (KeyValuePair<DisplayMode, HotkeyRow> pair in _directRows)
        {
            HotkeyBinding binding = _draft.GetDirectHotkey(pair.Key);
            pair.Value.Box.SetBindingSilently(binding.Clone());
            pair.Value.Toggle.Checked = binding.IsActive;
            pair.Value.Box.Enabled = pair.Value.Toggle.Checked;
        }

        RefreshCycleList();
        BuildLanguageCombo();

        _loading = false;
    }

    private void CollectFromDraft()
    {
        _draft.DoubleClickCycles = _cbDoubleClick.Checked;
        _draft.ShowNotifications = _cbNotifications.Checked;
        _draft.AutoStart = _cbAutoStart.Checked;
        _draft.EnableLog = _cbEnableLog.Checked;
        _draft.MiniSwitcher = _cbMiniEnable.Checked;
        _draft.MiniOnEveryScreen = _cbMiniEveryScreen.Checked;
        _draft.MiniAlwaysOnTop = _cbMiniAlwaysOnTop.Checked;

        if (_cboLanguage.SelectedItem is LanguageEntry entry)
        {
            _draft.Language = entry.Language.ToCode();
        }

        _draft.CycleModes = ReadCycleOrder().Select(mode => mode.ToId()).ToList();

        HotkeyBinding cycle = _cycleRow.Box.Binding.Clone();
        cycle.Enabled = _cycleRow.Toggle.Checked && cycle.HasKey;
        _draft.CycleHotkey = cycle;

        foreach (KeyValuePair<DisplayMode, HotkeyRow> pair in _directRows)
        {
            HotkeyBinding binding = pair.Value.Box.Binding.Clone();
            binding.Enabled = pair.Value.Toggle.Checked && binding.HasKey;
            _draft.SetDirectHotkey(pair.Key, binding);
        }
    }

    private void RefreshCycleList()
    {
        List<DisplayMode> order = _draft.GetCycleOrder();
        var included = new HashSet<DisplayMode>(order);

        var entries = new List<CycleEntry>(DisplayModes.Selectable.Length);
        foreach (DisplayMode mode in order)
        {
            entries.Add(new CycleEntry(mode, mode.Label()));
        }

        foreach (DisplayMode mode in DisplayModes.Selectable)
        {
            if (!included.Contains(mode))
            {
                entries.Add(new CycleEntry(mode, mode.Label()));
            }
        }

        ReplaceCycleItems(entries, included);
    }

    /// <summary>Re-renders the list in the new language without losing order or ticks.</summary>
    private void RelocalizeCycleList()
    {
        var modes = new DisplayMode[_cycleList.Items.Count];
        var states = new bool[_cycleList.Items.Count];

        for (int i = 0; i < _cycleList.Items.Count; i++)
        {
            modes[i] = ((CycleEntry)_cycleList.Items[i]!).Mode;
            states[i] = _cycleList.GetItemChecked(i);
        }

        var entries = new List<CycleEntry>(modes.Length);
        var included = new HashSet<DisplayMode>();
        foreach (DisplayMode mode in modes)
        {
            entries.Add(new CycleEntry(mode, mode.Label()));
            if (states[entries.Count - 1])
            {
                included.Add(mode);
            }
        }

        ReplaceCycleItems(entries, included);
    }

    private void ReplaceCycleItems(IReadOnlyList<CycleEntry> entries, HashSet<DisplayMode> included)
    {
        int previouslySelected = _cycleList.SelectedIndex;

        // Rebuilding raises ItemCheck once per item. Suppressing it keeps that from queueing a
        // refresh per row, and - more importantly - from touching BeginInvoke before the handle
        // exists (the constructor path). UpdateCycleState is called once at the end instead.
        _suppressCycleEvents = true;
        try
        {
            _cycleList.BeginUpdate();
            _cycleList.Items.Clear();
            foreach (CycleEntry entry in entries)
            {
                int index = _cycleList.Items.Add(entry);
                _cycleList.SetItemChecked(index, included.Contains(entry.Mode));
            }

            _cycleList.EndUpdate();
        }
        finally
        {
            _suppressCycleEvents = false;
        }

        if (previouslySelected >= 0 && previouslySelected < _cycleList.Items.Count)
        {
            _cycleList.SelectedIndex = previouslySelected;
        }

        UpdateCycleState();
    }

    /// <summary>
    /// Runs after a tick changed. The refresh has to be deferred by one message because
    /// ItemCheck fires before the new state is committed; the handle check covers the
    /// constructor path, where there is no window to post to yet.
    /// </summary>
    private void OnCycleItemCheck()
    {
        if (_suppressCycleEvents)
        {
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(UpdateCycleState);
        }
        else
        {
            UpdateCycleState();
        }
    }

    private List<DisplayMode> ReadCycleOrder()
    {
        var order = new List<DisplayMode>(_cycleList.Items.Count);
        for (int i = 0; i < _cycleList.Items.Count; i++)
        {
            if (_cycleList.GetItemChecked(i) && _cycleList.Items[i] is CycleEntry entry)
            {
                order.Add(entry.Mode);
            }
        }

        return order;
    }

    /// <summary>Moves the highlighted row up or down. The list order IS the cycle order.</summary>
    private void MoveCycleItem(int delta)
    {
        int from = _cycleList.SelectedIndex;
        int to = from + delta;

        if (from < 0 || to < 0 || to >= _cycleList.Items.Count)
        {
            return;
        }

        var entries = new List<CycleEntry>(_cycleList.Items.Count);
        var states = new List<bool>(_cycleList.Items.Count);

        for (int i = 0; i < _cycleList.Items.Count; i++)
        {
            entries.Add((CycleEntry)_cycleList.Items[i]!);
            states.Add(_cycleList.GetItemChecked(i));
        }

        (entries[from], entries[to]) = (entries[to], entries[from]);
        (states[from], states[to]) = (states[to], states[from]);

        _cycleList.BeginUpdate();
        _cycleList.Items.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            int index = _cycleList.Items.Add(entries[i]);
            _cycleList.SetItemChecked(index, states[i]);
        }

        _cycleList.EndUpdate();
        _cycleList.SelectedIndex = to;
        UpdateCycleState();
    }

    private void UpdateCycleState()
    {
        bool empty = ReadCycleOrder().Count == 0;
        _lblCycleState.ForeColor = empty ? Color.FromArgb(180, 83, 9) : SystemColors.GrayText;
        _lblCycleState.Text = Loc.T(empty ? "settings.cycleNeedOne" : "settings.cycleFine");
    }

    private void BuildLanguageCombo()
    {
        _suppressLanguageEvent = true;

        _cboLanguage.BeginUpdate();
        _cboLanguage.Items.Clear();
        foreach (AppLanguage language in AppLanguages.All)
        {
            _cboLanguage.Items.Add(new LanguageEntry(language));
        }

        int selected = 0;
        for (int i = 0; i < _cboLanguage.Items.Count; i++)
        {
            if (string.Equals(
                    ((LanguageEntry)_cboLanguage.Items[i]!).Language.ToCode(),
                    _draft.Language,
                    StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
                break;
            }
        }

        _cboLanguage.SelectedIndex = selected;
        _cboLanguage.EndUpdate();

        _suppressLanguageEvent = false;
    }

    private void UpdateAutoStartState()
    {
        bool registered = AutoStartService.IsRegistered;
        bool pointsHere = AutoStartService.PointsHere;

        // Three situations look similar in the registry but mean different things: no entry at
        // all, an entry pointing at a different copy, and an entry Windows has been told to skip.
        // Only the last one reads as "on" while still doing nothing at logon, so it gets its own
        // wording - otherwise the tick box appears to be lying to the user.
        string state;
        if (!registered)
        {
            state = Loc.T("settings.autoStartOff");
        }
        else if (AutoStartService.IsDisabledByWindows)
        {
            state = Loc.T("settings.autoStartBlocked");
        }
        else
        {
            state = pointsHere ? Loc.T("settings.autoStartOn") : Loc.T("settings.autoStartStale");
        }

        _lblAutoStartState.Text = Loc.T("settings.autoStartActual", state);
    }

    /// <summary>
    /// Greys out the two options that only mean anything once the switcher is switched on, so
    /// the group does not read as three independent settings.
    /// </summary>
    private void UpdateMiniEnabledState()
    {
        bool on = _cbMiniEnable.Checked;
        _cbMiniEveryScreen.Enabled = on;
        _cbMiniAlwaysOnTop.Enabled = on;
    }

    private void UpdateCurrentMode()
    {
        DisplayMode current = _displayService.GetCurrentMode();
        _lblCurrentMode.Text = Loc.T(
            "settings.currentMode",
            current.IsSelectable() ? current.Label() : Loc.T(DisplayMode.Unknown.LabelKey()));
    }

    // ---------------------------------------------------------------- localisation

    private void ApplyLocalization()
    {
        Text = Loc.T("settings.title");

        _tabGeneral.Text = Loc.T("settings.tab.general");
        _tabCycle.Text = Loc.T("settings.tab.cycle");
        _tabHotkeys.Text = Loc.T("settings.tab.hotkeys");
        _tabAdvanced.Text = Loc.T("settings.tab.advanced");

        _cbDoubleClick.Text = Loc.T("settings.doubleClick");
        _lblDoubleClickHint.Text = Loc.T("settings.doubleClickHint");
        _cbNotifications.Text = Loc.T("settings.showNotifications");

        _gbStartup.Text = Loc.T("settings.group.startup");
        _cbAutoStart.Text = Loc.T("settings.autoStart");
        UpdateAutoStartState();

        _gbMini.Text = Loc.T("settings.group.mini");
        _cbMiniEnable.Text = Loc.T("settings.miniEnable");
        _cbMiniEveryScreen.Text = Loc.T("settings.miniEveryScreen");
        _cbMiniAlwaysOnTop.Text = Loc.T("settings.miniAlwaysOnTop");
        _lblMiniHint.Text = Loc.T("settings.miniHint");
        UpdateCurrentMode();

        _lblCycleHint.Text = Loc.T("settings.cycleHint");
        _btnCycleUp.Text = Loc.T("settings.cycleUp");
        _btnCycleDown.Text = Loc.T("settings.cycleDown");
        RelocalizeCycleList();

        _lblHotkeyHint.Text = Loc.T("settings.hotkeys.hint");
        _lblHotkeyWarn.Text = Loc.T("settings.hotkeys.warn");
        _cycleRow.Label.Text = Loc.T("hotkey.cycle");
        _cycleRow.Clear.Text = Loc.T("settings.hotkeys.clear");
        _cycleRow.Box.Relocalize();

        foreach (KeyValuePair<DisplayMode, HotkeyRow> pair in _directRows)
        {
            pair.Value.Label.Text = Loc.T(pair.Key.HotkeyLabelKey());
            pair.Value.Clear.Text = Loc.T("settings.hotkeys.clear");
            pair.Value.Box.Relocalize();
        }

        _gbLanguage.Text = Loc.T("settings.group.language");
        _lblLanguage.Text = Loc.T("settings.languageLabel");
        _lblLanguageHint.Text = Loc.T("settings.languageHint");
        BuildLanguageCombo();

        _gbDiagnostics.Text = Loc.T("settings.group.log");
        _cbEnableLog.Text = Loc.T("settings.logEnabled");
        _lnkSettingsFolder.Text = Loc.T("settings.openSettingsFolder");
        _lblSettingsPath.Text = Loc.T("settings.configPath", _settingsService.FilePath);
        _lnkLogFolder.Text = Loc.T("settings.openLogFolder");
        _lblLogPath.Text = Loc.T("settings.logPath", AppLog.FilePath);

        _btnRestoreDefaults.Text = Loc.T("settings.button.restoreDefaults");
        _btnApply.Text = Loc.T("settings.button.apply");
        _btnOk.Text = Loc.T("settings.button.ok");
        _btnCancel.Text = Loc.T("settings.button.cancel");
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    /// <summary>
    /// Language is the one setting that takes effect immediately, and it is written straight
    /// away so a later Cancel cannot leave the UI in a language the file disagrees with.
    /// </summary>
    private void OnLanguageSelectionChanged(object? sender, EventArgs e)
    {
        if (_loading || _suppressLanguageEvent || _cboLanguage.SelectedItem is not LanguageEntry entry)
        {
            return;
        }

        _draft.Language = entry.Language.ToCode();
        _settingsService.Current.Language = _draft.Language;
        _settingsService.Save(_settingsService.Current, out _);

        LocalizationService.Instance.Apply(entry.Language);
        AppLog.Info("settings: language switched to " + _draft.Language);
    }

    // ---------------------------------------------------------------- apply

    private bool ApplyChanges()
    {
        CollectFromDraft();

        if (FindDuplicateHotkey(out string duplicateMessage))
        {
            MessageBox.Show(
                this,
                duplicateMessage,
                Loc.T("settings.invalidHotkeyTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        if (!_settingsService.Save(_draft, out string? error))
        {
            MessageBox.Show(
                this,
                Loc.T("err.settingsSave", error ?? string.Empty),
                Loc.T("settings.saveFailedTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        // Re-register hotkeys and rebuild the tray menu with the new values, then surface
        // any registration the shell refused (usually a conflict with another program).
        IReadOnlyList<string> failures = _applyAction?.Invoke() ?? [];
        if (failures.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, failures),
                Loc.T("notify.hotkeyFailedTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        LoadFromDraft();
        ApplyLocalization();
        return true;
    }

    private bool FindDuplicateHotkey(out string message)
    {
        message = string.Empty;
        var owners = new Dictionary<(uint Modifiers, uint VirtualKey), string>();

        var all = new List<(string Owner, HotkeyBinding Binding)>
        {
            (Loc.T("hotkey.cycle"), _draft.CycleHotkey),
        };

        foreach (DisplayMode mode in DisplayModes.Selectable)
        {
            all.Add((Loc.T(mode.HotkeyLabelKey()), _draft.GetDirectHotkey(mode)));
        }

        foreach ((string owner, HotkeyBinding binding) in all)
        {
            if (!binding.IsActive)
            {
                continue;
            }

            var signature = (binding.Modifiers, binding.VirtualKey);
            if (owners.TryGetValue(signature, out string? firstOwner))
            {
                message = Loc.T("settings.duplicateHotkey", binding.ToDisplayString(), firstOwner);
                return true;
            }

            owners[signature] = owner;
        }

        return false;
    }

    private void RestoreDefaults()
    {
        if (MessageBox.Show(
                this,
                Loc.T("settings.restoreConfirm"),
                Loc.T("settings.restoreConfirmTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var defaults = new AppSettings();
        defaults.Normalize();

        // The interface language and the log preference are deliberately kept: neither is
        // really part of "how the modes behave", and changing them mid-dialog is jarring.
        _draft.DoubleClickCycles = defaults.DoubleClickCycles;
        _draft.CycleModes = defaults.CycleModes;
        _draft.ShowNotifications = defaults.ShowNotifications;
        _draft.AutoStart = defaults.AutoStart;
        _draft.MiniSwitcher = defaults.MiniSwitcher;
        _draft.MiniOnEveryScreen = defaults.MiniOnEveryScreen;
        _draft.MiniAlwaysOnTop = defaults.MiniAlwaysOnTop;
        _draft.MiniOpacity = defaults.MiniOpacity;

        // Positions are dropped too: restoring defaults should put the mini switcher back where
        // it started rather than leave it wherever the user last dragged it.
        _draft.MiniPositions = new Dictionary<string, MiniPlacement>(StringComparer.Ordinal);

        _draft.CycleHotkey = defaults.CycleHotkey;
        _draft.DirectHotkeys = defaults.DirectHotkeys;

        LoadFromDraft();
        ApplyLocalization();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("folder could not be opened: " + ex.Message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        base.Dispose(disposing);
    }
}
