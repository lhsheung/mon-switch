using System.Text.Json.Serialization;
using MonSwitch.Services;

namespace MonSwitch.Core;

/// <summary>
/// One global hotkey: an optional Win32 modifier mask plus a virtual-key code.
/// A binding is only "active" when the user has both enabled it and assigned a key.
/// </summary>
internal sealed class HotkeyBinding
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    /// <summary>All modifier bits a user may pick. MOD_NOREPEAT is added at registration time only.</summary>
    public const uint ModAll = ModAlt | ModControl | ModShift | ModWin;

    public bool Enabled { get; set; }

    public uint Modifiers { get; set; }

    public uint VirtualKey { get; set; }

    /// <summary>Derived state, not a setting - kept out of settings.json.</summary>
    [JsonIgnore]
    public bool HasKey => VirtualKey != 0;

    /// <summary>True when the hotkey should actually be registered with Windows. Derived state.</summary>
    [JsonIgnore]
    public bool IsActive => Enabled && HasKey;

    public static HotkeyBinding Create(uint modifiers, uint virtualKey) => new()
    {
        Enabled = true,
        Modifiers = modifiers & ModAll,
        VirtualKey = virtualKey,
    };

    public HotkeyBinding Clone() => new()
    {
        Enabled = Enabled,
        Modifiers = Modifiers,
        VirtualKey = VirtualKey,
    };

    public void Clear()
    {
        Enabled = false;
        Modifiers = 0;
        VirtualKey = 0;
    }

    /// <summary>Drops bits that Windows would reject and keeps the object self-consistent.</summary>
    public void Normalize()
    {
        uint key = VirtualKey & 0xFFu;
        VirtualKey = key;
        Modifiers &= ModAll;

        if (VirtualKey == 0)
        {
            Modifiers = 0;
            Enabled = false;
        }
    }

    public bool HasSameCombination(HotkeyBinding? other) =>
        other is not null && HasKey && other.HasKey &&
        Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;

    /// <summary>Human readable form, e.g. "Ctrl+Alt+Shift+M". Localised modifier names.</summary>
    public string ToDisplayString()
    {
        if (!HasKey)
        {
            return Loc.T("hotkey.none");
        }

        var parts = new List<string>(5);
        if ((Modifiers & ModControl) != 0)
        {
            parts.Add(Loc.T("hotkey.ctrl"));
        }

        if ((Modifiers & ModAlt) != 0)
        {
            parts.Add(Loc.T("hotkey.alt"));
        }

        if ((Modifiers & ModShift) != 0)
        {
            parts.Add(Loc.T("hotkey.shift"));
        }

        if ((Modifiers & ModWin) != 0)
        {
            parts.Add(Loc.T("hotkey.win"));
        }

        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    /// <summary>Readable name for a virtual-key code. Key names stay Latin on purpose.</summary>
    public static string KeyName(uint virtualKey)
    {
        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return "F" + (virtualKey - 0x70 + 1);          // F1..F24
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();          // 0..9
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();          // A..Z
        }

        return virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => "0x" + virtualKey.ToString("X2"),
        };
    }
}
