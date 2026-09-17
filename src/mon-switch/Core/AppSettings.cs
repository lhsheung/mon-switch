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
            CycleHotkey = CycleHotkey.Clone(),
            DirectHotkeys = new Dictionary<string, HotkeyBinding>(StringComparer.Ordinal),
        };

        foreach (KeyValuePair<string, HotkeyBinding> pair in DirectHotkeys)
        {
            clone.DirectHotkeys[pair.Key] = pair.Value.Clone();
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
