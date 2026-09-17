using System.ComponentModel;
using System.Runtime.InteropServices;
using MonSwitch.Core;
using MonSwitch.Native;

namespace MonSwitch.Services;

/// <summary>A physical output as reported by the display configuration database.</summary>
internal sealed record DisplayTarget(
    string FriendlyName,
    string GdiDeviceName,
    uint OutputTechnology,
    bool Available,
    bool Active)
{
    public bool IsInternalPanel => DisplayConfigInterop.IsInternalTechnology(OutputTechnology);
}

/// <summary>
/// All display-topology work lives here: enumerating outputs, detecting the current mode
/// and switching. Switching uses SDC_TOPOLOGY_* only, i.e. no path/mode arrays, exactly like
/// the Windows projection fly-out. No admin rights, no external process, no polling.
/// </summary>
internal sealed class DisplayModeService
{
    private readonly bool _apiUsable;

    public DisplayModeService()
    {
        _apiUsable = DisplayConfigInterop.LayoutsAreValid(out string detail);
        if (_apiUsable)
        {
            AppLog.Info("display config interop validated: " + detail);
        }
        else
        {
            AppLog.Error("display config interop layout mismatch, switching disabled: " + detail);
        }
    }

    public bool IsRemoteSession => NativeMethods.IsRemoteSession();

    /// <summary>Reads the CCD database. Returns an empty list when the API is unavailable.</summary>
    public IReadOnlyList<DisplayTarget> EnumerateTargets()
    {
        if (!_apiUsable)
        {
            return [];
        }

        // One physical output appears once per possible source/mode combination in
        // QDC_ALL_PATHS - on the test machine a single monitor produced 97 path entries.
        // Collapse them onto the target id, which is what actually identifies an output.
        var byTarget = new Dictionary<(uint Low, int High, uint Id), DisplayTarget>(8);

        try
        {
            foreach (DisplayConfigInterop.PathInfo path in QueryPaths(DisplayConfigInterop.QdcAllPaths))
            {
                var key = (path.TargetInfo.AdapterId.LowPart, path.TargetInfo.AdapterId.HighPart, path.TargetInfo.Id);
                bool active = (path.Flags & DisplayConfigInterop.PathActive) != 0;

                if (byTarget.TryGetValue(key, out DisplayTarget? existing))
                {
                    // Prefer the active path: that is the one Windows is actually driving.
                    if (active && !existing.Active)
                    {
                        byTarget[key] = existing with { Active = true };
                    }

                    continue;
                }

                DisplayConfigInterop.TargetDeviceName target = GetTargetName(
                    path.TargetInfo.AdapterId,
                    path.TargetInfo.Id);

                string gdiName = GetSourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id);
                string friendly = string.IsNullOrWhiteSpace(target.MonitorFriendlyDeviceName)
                    ? gdiName
                    : target.MonitorFriendlyDeviceName;

                byTarget[key] = new DisplayTarget(
                    FriendlyName: friendly,
                    GdiDeviceName: gdiName,
                    OutputTechnology: path.TargetInfo.OutputTechnology,
                    Available: path.TargetInfo.TargetAvailable != 0,
                    Active: active);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("display enumeration failed: " + ex.Message);
        }

        return byTarget.Values
            .OrderByDescending(target => target.Active)
            .ThenByDescending(target => target.Available)
            .ThenBy(target => target.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Infers the mode currently in force. A clone shows up as several active targets that
    /// share one source, which is exactly how the CCD database represents it.
    /// </summary>
    public DisplayMode GetCurrentMode()
    {
        if (!_apiUsable || IsRemoteSession)
        {
            return DisplayMode.Unknown;
        }

        try
        {
            var active = new List<DisplayConfigInterop.PathInfo>(4);
            foreach (DisplayConfigInterop.PathInfo path in QueryPaths(DisplayConfigInterop.QdcOnlyActivePaths))
            {
                if ((path.Flags & DisplayConfigInterop.PathActive) != 0)
                {
                    active.Add(path);
                }
            }

            if (active.Count == 0)
            {
                return DisplayMode.Unknown;
            }

            int distinctSources = active
                .Select(p => (p.SourceInfo.AdapterId.LowPart, p.SourceInfo.AdapterId.HighPart, p.SourceInfo.Id))
                .Distinct()
                .Count();

            if (distinctSources < active.Count)
            {
                return DisplayMode.Clone;
            }

            if (active.Count == 1)
            {
                // Same internal-panel test as DisplayTarget.IsInternalPanel: DISPLAYPORT_EMBEDDED
                // also counts, otherwise a laptop panel is reported as "second screen only".
                return DisplayConfigInterop.IsInternalTechnology(active[0].TargetInfo.OutputTechnology)
                    ? DisplayMode.InternalOnly
                    : DisplayMode.ExternalOnly;
            }

            return DisplayMode.Extend;
        }
        catch (Exception ex)
        {
            AppLog.Warn("current mode detection failed: " + ex.Message);
            return DisplayMode.Unknown;
        }
    }

    /// <summary>
    /// Applies a topology. Never throws: every failure comes back as a localised reason
    /// string so the tray can show "why" instead of a bare error code.
    /// </summary>
    public bool TrySwitch(DisplayMode mode, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!mode.IsSelectable())
        {
            errorMessage = Loc.T("err.unknownMode");
            return false;
        }

        if (!_apiUsable)
        {
            errorMessage = Loc.T("err.apiUnavailable");
            return false;
        }

        if (IsRemoteSession)
        {
            errorMessage = Loc.T("err.remoteSession");
            return false;
        }

        uint flags = DisplayConfigInterop.SdcApply | mode.TopologyFlag();

        int result = DisplayConfigInterop.SetDisplayConfig(0, null, 0, null, flags);
        if (result == DisplayConfigInterop.ErrorSuccess)
        {
            AppLog.Info($"topology applied: {mode}");
            return true;
        }

        int firstResult = result;

        // A handful of drivers reject the topology flags unless changes are permitted; retry once.
        result = DisplayConfigInterop.SetDisplayConfig(0, null, 0, null, flags | DisplayConfigInterop.SdcAllowChanges);
        if (result == DisplayConfigInterop.ErrorSuccess)
        {
            AppLog.Info($"topology applied on retry: {mode}");
            return true;
        }

        AppLog.Warn($"SetDisplayConfig({mode}) failed: first={firstResult} retry={result}");
        errorMessage = ExplainFailure(mode, result);
        return false;
    }

    /// <summary>Turns a CCD error code into a reason the user can act on.</summary>
    public string ExplainFailure(DisplayMode mode, int code)
    {
        if (IsRemoteSession)
        {
            return Loc.T("err.remoteSession");
        }

        if (code == 5)
        {
            return Loc.T("err.accessDenied");
        }

        if (code == 50)
        {
            return Loc.T("err.notSupported");
        }

        IReadOnlyList<DisplayTarget> targets = EnumerateTargets();
        int available = targets.Count(t => t.Available);
        bool hasInternal = targets.Any(t => t.Available && t.IsInternalPanel);
        bool hasExternal = targets.Any(t => t.Available && !t.IsInternalPanel);

        if (mode == DisplayMode.InternalOnly && !hasInternal)
        {
            return Loc.T("err.noInternalDisplay");
        }

        if (mode == DisplayMode.ExternalOnly && !hasExternal)
        {
            return Loc.T("err.noExternalDisplay");
        }

        if (mode is DisplayMode.Clone or DisplayMode.Extend)
        {
            return available < 2 ? Loc.T("err.noSecondDisplay") : Loc.T("err.notSupported");
        }

        if (code == 87)
        {
            return Loc.T("err.invalidParameter");
        }

        if (code == 1168)
        {
            return Loc.T("err.notFound");
        }

        return Loc.T("err.generic", code, SystemMessageFor(code));
    }

    private static string SystemMessageFor(int code)
    {
        try
        {
            // FormatMessage text comes from the OS, so it is already in the user's language
            // regardless of which pack mon-switch is showing.
            return new Win32Exception(code).Message;
        }
        catch
        {
            return "0x" + code.ToString("X8");
        }
    }

    /// <summary>
    /// Queries the CCD database, retrying a couple of times if it changed between the size
    /// probe and the read. This is a bounded retry, not a polling loop.
    /// </summary>
    private static DisplayConfigInterop.PathInfo[] QueryPaths(uint flags)
    {
        const int maxAttempts = 3;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int result = DisplayConfigInterop.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);

            if (AppLog.IsEnabled)
            {
                AppLog.Info($"GetDisplayConfigBufferSizes(0x{flags:X2}) -> {result}, paths={pathCount}, modes={modeCount}");
            }

            if (result != DisplayConfigInterop.ErrorSuccess)
            {
                throw new Win32Exception(result);
            }

            if (pathCount == 0)
            {
                return [];
            }

            var paths = new DisplayConfigInterop.PathInfo[pathCount];
            var modes = new DisplayConfigInterop.ModeInfo[modeCount == 0 ? 1u : modeCount];
            uint requestedPaths = pathCount;
            uint requestedModes = (uint)modes.Length;

            result = DisplayConfigInterop.QueryDisplayConfig(
                flags,
                ref requestedPaths,
                paths,
                ref requestedModes,
                modes,
                IntPtr.Zero);   // must be NULL unless flags is QDC_DATABASE_CURRENT

            if (AppLog.IsEnabled)
            {
                AppLog.Info($"QueryDisplayConfig(0x{flags:X2}) -> {result}, paths={requestedPaths}/{paths.Length}, modes={requestedModes}/{modes.Length}");
            }

            if (result == DisplayConfigInterop.ErrorSuccess)
            {
                if (requestedPaths >= paths.Length)
                {
                    return paths;
                }

                var trimmed = new DisplayConfigInterop.PathInfo[requestedPaths];
                Array.Copy(paths, trimmed, (int)requestedPaths);
                return trimmed;
            }

            if (result != DisplayConfigInterop.ErrorInsufficientBuffer)
            {
                throw new Win32Exception(result);
            }
        }

        AppLog.Warn("display configuration database kept changing; giving up this read");
        return [];
    }

    private static DisplayConfigInterop.TargetDeviceName GetTargetName(DisplayConfigInterop.Luid adapterId, uint targetId)
    {
        var request = new DisplayConfigInterop.TargetDeviceName
        {
            Header = new DisplayConfigInterop.DeviceInfoHeader
            {
                Type = DisplayConfigInterop.DeviceInfoGetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigInterop.TargetDeviceName>(),
                AdapterId = adapterId,
                Id = targetId,
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty,
        };

        return DisplayConfigInterop.GetTargetDeviceInfo(ref request) == DisplayConfigInterop.ErrorSuccess
            ? request
            : default;
    }

    private static string GetSourceName(DisplayConfigInterop.Luid adapterId, uint sourceId)
    {
        var request = new DisplayConfigInterop.SourceDeviceName
        {
            Header = new DisplayConfigInterop.DeviceInfoHeader
            {
                Type = DisplayConfigInterop.DeviceInfoGetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigInterop.SourceDeviceName>(),
                AdapterId = adapterId,
                Id = sourceId,
            },
            ViewGdiDeviceName = string.Empty,
        };

        return DisplayConfigInterop.GetSourceDeviceInfo(ref request) == DisplayConfigInterop.ErrorSuccess
            ? request.ViewGdiDeviceName
            : string.Empty;
    }
}
