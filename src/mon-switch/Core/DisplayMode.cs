using MonSwitch.Native;
using MonSwitch.Services;

namespace MonSwitch.Core;

/// <summary>
/// The four display / projection modes that Windows itself offers.
/// The enum values are an internal ABI - never persist the numeric value,
/// persist <see cref="DisplayModes.ToId"/> instead.
/// </summary>
public enum DisplayMode
{
    Unknown = 0,
    InternalOnly = 1,
    Clone = 2,
    Extend = 3,
    ExternalOnly = 4,
}

public static class DisplayModes
{
    /// <summary>Stable ids used inside settings.json. Never translate these.</summary>
    public const string IdInternalOnly = "InternalOnly";
    public const string IdClone = "Clone";
    public const string IdExtend = "Extend";
    public const string IdExternalOnly = "ExternalOnly";

    /// <summary>All four modes, in the order Windows lists them.</summary>
    public static readonly DisplayMode[] Selectable =
    [
        DisplayMode.InternalOnly,
        DisplayMode.Clone,
        DisplayMode.Extend,
        DisplayMode.ExternalOnly,
    ];

    public static string ToId(this DisplayMode mode) => mode switch
    {
        DisplayMode.InternalOnly => IdInternalOnly,
        DisplayMode.Clone => IdClone,
        DisplayMode.Extend => IdExtend,
        DisplayMode.ExternalOnly => IdExternalOnly,
        _ => "Unknown",
    };

    public static bool TryParseId(string? id, out DisplayMode mode)
    {
        mode = DisplayMode.Unknown;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        foreach (DisplayMode candidate in Selectable)
        {
            if (string.Equals(candidate.ToId(), id, StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool IsSelectable(this DisplayMode mode) =>
        mode is >= DisplayMode.InternalOnly and <= DisplayMode.ExternalOnly;

    /// <summary>Resource key for the localised name, e.g. "mode.Extend".</summary>
    public static string LabelKey(this DisplayMode mode) => "mode." + mode.ToId();

    /// <summary>Localised name, e.g. "延伸" / "Extend".</summary>
    public static string Label(this DisplayMode mode) => Loc.T(mode.LabelKey());

    /// <summary>Resource key for the "switch directly to X" hotkey row label.</summary>
    public static string HotkeyLabelKey(this DisplayMode mode) => "hotkey.direct." + mode.ToId();

    /// <summary>
    /// Maps a mode to the SDC_TOPOLOGY_* flag. These are exactly the four
    /// topologies the Windows projection fly-out (Win+P) offers.
    /// </summary>
    public static uint TopologyFlag(this DisplayMode mode) => mode switch
    {
        DisplayMode.InternalOnly => DisplayConfigInterop.SdcTopologyInternal,
        DisplayMode.Clone => DisplayConfigInterop.SdcTopologyClone,
        DisplayMode.Extend => DisplayConfigInterop.SdcTopologyExtend,
        DisplayMode.ExternalOnly => DisplayConfigInterop.SdcTopologyExternal,
        _ => 0u,
    };
}
