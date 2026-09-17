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
    /// Brings the registry in line with the setting at start-up. Also repairs the case where
    /// the app was moved to another folder and the stored path went stale.
    /// </summary>
    public static bool TrySynchronise(bool desired, out string? error)
    {
        error = null;

        if (desired && !PointsHere)
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
