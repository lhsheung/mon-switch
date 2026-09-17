using System.Reflection;
using Microsoft.Win32;
using MonSwitch.Core;

namespace MonSwitch.Services;

/// <summary>
/// "Start with Windows" through <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
///
/// Chosen over the Startup folder because:
///  * no COM / shell automation, just a registry value;
///  * HKCU means no elevation prompt, ever;
///  * the command line is visible and editable by the user in one place;
///  * removing it is a single DeleteValue, so uninstall leaves nothing behind.
/// The registered command always carries <c>--silent</c>, which is why the app starts
/// straight to the tray and never opens a window on logon.
/// </summary>
internal static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Windows keeps a second, independent switch for every Run entry under this key. The
    /// value is a REG_BINARY blob whose first byte says whether the entry is allowed to run.
    /// Task Manager and Settings &gt; Apps &gt; Startup write it, and when it says "off" the
    /// entry is skipped at logon even though the Run value itself is still present. Without
    /// reading this, mon-switch would report "enabled" while Windows quietly ignored it.
    /// </summary>
    private const string StartupApprovedPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const byte StartupApprovedEnabled = 0x02;
    private const byte StartupApprovedEnabledLegacy = 0x06;
    private const byte StartupApprovedDisabled = 0x03;

    /// <summary>The real .exe, also when the app is launched as "dotnet mon-switch.dll".</summary>
    public static string ExecutablePath
    {
        get
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && !IsDotnetHost(processPath))
            {
                return processPath;
            }

            string entry = EntryAssemblyPath();
            return !string.IsNullOrEmpty(entry) ? entry : processPath ?? string.Empty;
        }
    }

    /// <summary>Exactly what is written into the Run value.</summary>
    public static string CommandLine
    {
        get
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && IsDotnetHost(processPath))
            {
                return $"\"{processPath}\" \"{EntryAssemblyPath()}\" --silent";
            }

            return $"\"{ExecutablePath}\" --silent";
        }
    }

    /// <summary>
    /// The managed entry file, needed only for the "dotnet mon-switch.dll" launch shape.
    ///
    /// Assembly.Location is deliberately avoided: in a single-file publish it always returns an
    /// empty string (compiler warning IL3000), which would silently register a broken auto-start
    /// command. AppContext.BaseDirectory plus the assembly name gives the same answer in every
    /// publish shape. The single-file build never reaches here anyway, because there
    /// Environment.ProcessPath is the .exe itself.
    /// </summary>
    private static string EntryAssemblyPath()
    {
        string? name = Assembly.GetEntryAssembly()?.GetName().Name;
        return string.IsNullOrEmpty(name)
            ? string.Empty
            : Path.Combine(AppContext.BaseDirectory, name + ".dll");
    }

    private static bool IsDotnetHost(string path) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase);

    public static string? ReadRegisteredCommand()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppInfo.RunValueName) as string;
        }
        catch (Exception ex)
        {
            AppLog.Warn("auto-start value could not be read: " + ex.Message);
            return null;
        }
    }

    /// <summary>A Run entry exists, whatever it points at.</summary>
    public static bool IsRegistered => !string.IsNullOrWhiteSpace(ReadRegisteredCommand());

    /// <summary>The Run entry exists AND points at this copy of mon-switch.</summary>
    public static bool PointsHere
    {
        get
        {
            string? command = ReadRegisteredCommand();
            return !string.IsNullOrWhiteSpace(command)
                && !string.IsNullOrEmpty(ExecutablePath)
                && command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// True when the Run entry exists but Windows has been told to skip it. Task Manager, or
    /// Settings &gt; Apps &gt; Startup, writes that flag; the entry then stays visible in the
    /// registry while never actually running, which looks exactly like "auto-start is broken".
    /// </summary>
    public static bool IsDisabledByWindows
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, writable: false);
                if (key?.GetValue(AppInfo.RunValueName) is byte[] { Length: > 0 } blob)
                {
                    // 0x02 is the normal "allowed" byte and 0x06 is the older equivalent; the
                    // shell writes 0x03 when the entry is switched off. Anything else is
                    // treated as "not allowed", because assuming it runs would hide the very
                    // problem this property exists to detect.
                    return blob[0] != StartupApprovedEnabled && blob[0] != StartupApprovedEnabledLegacy;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("the StartupApproved flag could not be read: " + ex.Message);
            }

            return false;
        }
    }

    /// <summary>
    /// Removes the "skip this entry" flag so that enabling auto-start really takes effect at the
    /// next logon. Deleting the value is the documented way to put the entry back to allowed.
    /// </summary>
    private static void ClearWindowsDisableFlag()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedPath, writable: true);
            if (key is null)
            {
                return;
            }

            key.DeleteValue(AppInfo.RunValueName, throwOnMissingValue: false);
            AppLog.Info("StartupApproved flag cleared");
        }
        catch (Exception ex)
        {
            // Not fatal: the Run entry is written either way, and the settings dialog reports
            // the remaining flag through IsDisabledByWindows.
            AppLog.Warn("the StartupApproved flag could not be cleared: " + ex.Message);
        }
    }

    public static bool TrySet(bool enabled, out string? error)
    {
        error = null;

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("HKCU Run key is not writable");

            if (enabled)
            {
                key.SetValue(AppInfo.RunValueName, CommandLine, RegistryValueKind.String);
                AppLog.Info("auto-start entry written: " + CommandLine);

                // Writing the Run value is not enough on its own: if Windows still carries a
                // "disabled" marker for this name from an earlier trip through Task Manager, the
                // entry would never run. Clear it so the switch the user just flipped is real.
                ClearWindowsDisableFlag();
            }
            else
            {
                key.DeleteValue(AppInfo.RunValueName, throwOnMissingValue: false);
                AppLog.Info("auto-start entry removed");
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Warn("auto-start value could not be written: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Brings the registry in line with the setting. Also repairs the case where the app was
    /// moved to another folder and the stored path went stale.
    /// </summary>
    /// <param name="force">
    /// Rewrite the entry even when it already looks right. The settings dialog passes true, so
    /// that a "disabled" flag left behind by Task Manager is cleared when the user explicitly
    /// asks for auto-start. Start-up passes false, so a deliberate choice made elsewhere is
    /// respected rather than silently undone on every launch.
    /// </param>
    public static bool TrySynchronise(bool desired, bool force, out string? error)
    {
        error = null;

        if (desired && (force || !PointsHere))
        {
            return TrySet(true, out error);
        }

        if (!desired && IsRegistered)
        {
            return TrySet(false, out error);
        }

        return true;
    }
}
