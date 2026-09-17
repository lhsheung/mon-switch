namespace MonSwitch.Core;

/// <summary>
/// Everything mon-switch persists. Serialised to
/// <c>%AppData%\mon-switch\settings.json</c> with camelCase keys.
/// </summary>
internal sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>"system", "yue", "zh-Hant", "zh-Hans" or "en".</summary>
    public string Language { get; set; } = AppLanguages.SystemCode;

    /// <summary>Whether double-clicking the tray icon walks the cycle.</summary>
    public bool DoubleClickCycles { get; set; } = true;

    /// <summary>Ids of the modes that take part in cycling, in cycle order. Not localised.</summary>
    public List<string> CycleModes { get; set; } = DefaultCycleModes();

    public bool ShowNotifications { get; set; } = true;

    public bool AutoStart { get; set; }

    public bool EnableLog { get; set; }

    /// <summary>
    /// Show a small always-reachable switcher window.
    ///
    /// Windows only puts the notification area on one taskbar. With the taskbar set to appear
    /// on all displays, secondary monitors still get no tray, so after switching to "second
    /// screen only" there may be no icon anywhere the user can reach without a hotkey. The mini
    /// switcher is not a tray icon - it is mon-switch's own window, which is why it can be put
    /// on any screen without touching Explorer.
    /// </summary>
    public bool MiniSwitcher { get; set; }

    /// <summary>One copy per screen, rather than a single one the user has to drag around.</summary>
    public bool MiniOnEveryScreen { get; set; } = true;

    public bool MiniAlwaysOnTop { get; set; } = true;

    /// <summary>0.3 to 1.0. Below 1.0 the window stays legible but recedes.</summary>
    public double MiniOpacity { get; set; } = 0.92;

    /// <summary>Remembered position per screen, keyed by device name (\\.\DISPLAY1 and friends).</summary>
    public Dictionary<string, MiniPlacement> MiniPositions { get; set; } = new(StringComparer.Ordinal);

    public HotkeyBinding CycleHotkey { get; set; } = DefaultCycleHotkey();

    /// <summary>Keyed by <see cref="DisplayModes.ToId"/>.</summary>
    public Dictionary<string, HotkeyBinding> DirectHotkeys { get; set; } = DefaultDirectHotkeys();

    public AppSettings Clone()
    {
        var clone = new AppSettings
        {
            Version = Version,
            Language = Language,
            DoubleClickCycles = DoubleClickCycles,
            CycleModes = new List<string>(CycleModes),
            ShowNotifications = ShowNotifications,
            AutoStart = AutoStart,
            EnableLog = EnableLog,
            MiniSwitcher = MiniSwitcher,
            MiniOnEveryScreen = MiniOnEveryScreen,
            MiniAlwaysOnTop = MiniAlwaysOnTop,
            MiniOpacity = MiniOpacity,
            MiniPositions = new Dictionary<string, MiniPlacement>(StringComparer.Ordinal),
            CycleHotkey = CycleHotkey.Clone(),
            DirectHotkeys = new Dictionary<string, HotkeyBinding>(StringComparer.Ordinal),
        };

        foreach (KeyValuePair<string, HotkeyBinding> pair in DirectHotkeys)
        {
            clone.DirectHotkeys[pair.Key] = pair.Value.Clone();
        }

        foreach (KeyValuePair<string, MiniPlacement> pair in MiniPositions)
        {
            if (pair.Value is not null)
            {
                clone.MiniPositions[pair.Key] = pair.Value.Clone();
            }
        }

        return clone;
    }

    public static List<string> DefaultCycleModes() =>
    [
        DisplayModes.IdInternalOnly,
        DisplayModes.IdClone,
        DisplayModes.IdExtend,
        DisplayModes.IdExternalOnly,
    ];

    /// <summary>Ctrl+Alt+Shift+M. Deliberately avoids Win+… which Windows reserves (e.g. Win+P).</summary>
    public static HotkeyBinding DefaultCycleHotkey() =>
        HotkeyBinding.Create(HotkeyBinding.ModControl | HotkeyBinding.ModAlt | HotkeyBinding.ModShift, 0x4D);

    public static Dictionary<string, HotkeyBinding> DefaultDirectHotkeys() => new(StringComparer.Ordinal)
    {
        [DisplayModes.IdInternalOnly] = HotkeyBinding.Create(Mod3(), 0x31),   // ...+1
        [DisplayModes.IdClone] = HotkeyBinding.Create(Mod3(), 0x32),          // ...+2
        [DisplayModes.IdExtend] = HotkeyBinding.Create(Mod3(), 0x33),         // ...+3
        [DisplayModes.IdExternalOnly] = HotkeyBinding.Create(Mod3(), 0x34),   // ...+4
    };

    private static uint Mod3() => HotkeyBinding.ModControl | HotkeyBinding.ModAlt | HotkeyBinding.ModShift;

    /// <summary>
    /// Repairs anything a hand-edited or older settings file could have broken:
    /// unknown ids, duplicates, out-of-range virtual keys, unknown language codes.
    /// </summary>
    public void Normalize()
    {
        // A hand-edited file may contain explicit nulls; System.Text.Json writes them straight
        // into the property, so every collection is re-checked here rather than trusted.
        CycleModes ??= new List<string>();
        DirectHotkeys ??= new Dictionary<string, HotkeyBinding>(StringComparer.Ordinal);
        CycleHotkey ??= new HotkeyBinding();
        Language ??= AppLanguages.SystemCode;
        MiniPositions ??= new Dictionary<string, MiniPlacement>(StringComparer.Ordinal);

        // Drop entries a hand-edited file could have left without a value, otherwise the
        // mini switcher would read a null placement and throw while positioning itself.
        foreach (string key in MiniPositions.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            MiniPositions.Remove(key);
        }

        if (MiniOpacity < 0.3 || MiniOpacity > 1.0 || double.IsNaN(MiniOpacity))
        {
            MiniOpacity = 0.92;
        }

        if (Version < CurrentVersion)
        {
            Version = CurrentVersion;
        }

        if (!AppLanguages.TryParseCode(Language, out _))
        {
            Language = AppLanguages.SystemCode;
        }

        var cleaned = new List<string>(CycleModes.Count);
        foreach (string id in CycleModes)
        {
            if (DisplayModes.TryParseId(id, out DisplayMode parsed) && !cleaned.Contains(parsed.ToId()))
            {
                cleaned.Add(parsed.ToId());
            }
        }

        CycleModes = cleaned;
        CycleHotkey.Normalize();

        var repaired = new Dictionary<string, HotkeyBinding>(StringComparer.Ordinal);
        foreach (DisplayMode mode in DisplayModes.Selectable)
        {
            HotkeyBinding binding = DirectHotkeys.TryGetValue(mode.ToId(), out HotkeyBinding? existing) && existing is not null
                ? existing
                : new HotkeyBinding();
            binding.Normalize();
            repaired[mode.ToId()] = binding;
        }

        DirectHotkeys = repaired;
    }

    /// <summary>The cycle as concrete modes, in the user's chosen order.</summary>
    public List<DisplayMode> GetCycleOrder()
    {
        var order = new List<DisplayMode>(CycleModes.Count);
        foreach (string id in CycleModes)
        {
            if (DisplayModes.TryParseId(id, out DisplayMode mode) && !order.Contains(mode))
            {
                order.Add(mode);
            }
        }

        return order;
    }

    public HotkeyBinding GetDirectHotkey(DisplayMode mode)
    {
        if (DirectHotkeys.TryGetValue(mode.ToId(), out HotkeyBinding? binding) && binding is not null)
        {
            return binding;
        }

        var created = new HotkeyBinding();
        DirectHotkeys[mode.ToId()] = created;
        return created;
    }

    public void SetDirectHotkey(DisplayMode mode, HotkeyBinding binding) =>
        DirectHotkeys[mode.ToId()] = binding;
}
