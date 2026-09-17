using System.Runtime.InteropServices;

namespace MonSwitch.Native;

/// <summary>
/// Interop for the Windows Display Configuration API (CCD). This is the same API the
/// Windows projection fly-out (Win+P) and <c>DisplaySwitch.exe</c> use, which is why
/// mon-switch does not need to shell out to an external executable.
///
/// Struct layouts mirror the Windows SDK headers exactly. <c>LayoutsAreValid()</c> asserts
/// the sizes so a future edit cannot silently corrupt the marshalling.
/// </summary>
internal static class DisplayConfigInterop
{
    // QueryDisplayConfig flags
    public const uint QdcAllPaths = 0x00000001;
    public const uint QdcOnlyActivePaths = 0x00000002;
    public const uint QdcDatabaseCurrent = 0x00000004;

    // SDC_TOPOLOGY_* - exactly the four modes the Windows projection fly-out offers.
    public const uint SdcTopologyInternal = 0x00000001;
    public const uint SdcTopologyClone = 0x00000002;
    public const uint SdcTopologyExtend = 0x00000004;
    public const uint SdcTopologyExternal = 0x00000008;

    // SetDisplayConfig flags
    public const uint SdcApply = 0x00000080;
    public const uint SdcNoOptimization = 0x00000100;
    public const uint SdcSaveToDatabase = 0x00000200;
    public const uint SdcAllowChanges = 0x00000400;

    public const uint PathActive = 0x00000001;

    // DISPLAYCONFIG_DEVICE_INFO_TYPE
    public const uint DeviceInfoGetSourceName = 1;
    public const uint DeviceInfoGetTargetName = 2;

    /// <summary>DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL - a built-in panel (laptop / tablet).</summary>
    public const uint OutputTechnologyInternal = 0x80000000;

    private const uint OutputTechnologyLvds = 6;
    private const uint OutputTechnologyDisplayPortEmbedded = 11;
    private const uint OutputTechnologyUdiEmbedded = 12;

    /// <summary>
    /// True when the output is a panel wired directly into the machine, i.e. what
    /// "PC screen only" means.
    ///
    /// Testing on real hardware showed that DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
    /// (0x80000000) is not the only value a built-in panel reports: an eDP laptop panel was
    /// reported as DISPLAYPORT_EMBEDDED (0x0B), which would otherwise be mistaken for an
    /// external monitor and make "PC screen only" look unsupported.
    /// </summary>
    public static bool IsInternalTechnology(uint outputTechnology) =>
        outputTechnology is OutputTechnologyInternal
            or OutputTechnologyLvds
            or OutputTechnologyDisplayPortEmbedded
            or OutputTechnologyUdiEmbedded;

    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public override string ToString() => $"({LowPart},{HighPart})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;

        // union { UINT32 modeInfoIdx; struct { cloneGroupId:16; sourceModeInfoIdx:16; }; }
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    /// <summary>
    /// DISPLAYCONFIG_MODE_INFO is a 64-byte struct whose tail is a union we never read.
    /// Declaring the prefix plus an explicit Size keeps the array stride correct.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SourceDeviceName
    {
        public DeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeArrayElements);

    /// <summary>
    /// Reads the display configuration database.
    ///
    /// TRAP: <c>currentTopologyId</c> must be NULL. Passing a non-NULL pointer while the flags
    /// are anything other than <see cref="QdcDatabaseCurrent"/> makes this call fail with
    /// ERROR_INVALID_PARAMETER (87) - verified on Windows 11 build 26200 with a probe that
    /// tried the NULL form and the <c>out uint</c> form back to back. mon-switch therefore
    /// always passes <see cref="IntPtr.Zero"/>; it does not need the topology id, because the
    /// current mode is derived from the active paths instead.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] PathInfo[] pathInfoArray,
        ref uint numModeArrayElements,
        [Out] ModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] PathInfo[]? pathArray,
        uint numModeArrayElements,
        [In] ModeInfo[]? modeArray,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetTargetDeviceInfo(ref TargetDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int GetSourceDeviceInfo(ref SourceDeviceName requestPacket);

    /// <summary>
    /// Guard against accidental struct edits. Called once at start-up; a mismatch is logged
    /// and the display service disables itself instead of mis-marshalling native memory.
    /// </summary>
    public static bool LayoutsAreValid(out string detail)
    {
        int path = Marshal.SizeOf<PathInfo>();
        int mode = Marshal.SizeOf<ModeInfo>();
        int targetName = Marshal.SizeOf<TargetDeviceName>();
        int sourceName = Marshal.SizeOf<SourceDeviceName>();

        detail = $"PathInfo={path} (72), ModeInfo={mode} (64), TargetDeviceName={targetName} (420), SourceDeviceName={sourceName} (84)";

        return path == 72 && mode == 64 && targetName == 420 && sourceName == 84;
    }
}
