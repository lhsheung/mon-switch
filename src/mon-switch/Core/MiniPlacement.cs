namespace MonSwitch.Core;

/// <summary>
/// Where one copy of the mini switcher sits on one screen.
///
/// Kept as its own type rather than a pair of ints because the settings file stores one
/// placement per screen: <see cref="AppSettings.MiniPositions"/> is keyed by the screen's
/// device name, which is the only identifier that survives a reboot with the monitors still
/// plugged into the same ports. The index in <c>Screen.AllScreens</c> is not stable.
/// </summary>
internal sealed class MiniPlacement
{
    public int X { get; set; }

    public int Y { get; set; }

    public MiniPlacement Clone() => new() { X = X, Y = Y };
}
