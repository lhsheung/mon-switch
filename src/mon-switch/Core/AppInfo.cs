using System.Reflection;

namespace MonSwitch.Core;

internal static class AppInfo
{
    public const string Name = "mon-switch";

    /// <summary>Folder created under %AppData% for settings and logs.</summary>
    public const string DataFolderName = "mon-switch";

    /// <summary>Value name used in HKCU\...\Run.</summary>
    public const string RunValueName = "mon-switch";

    /// <summary>Mutex / event names. "Local\" keeps one instance per Windows session,
    /// which is what we want: display topology is a per-session concept.</summary>
    public const string MutexName = @"Local\mon-switch.single-instance.v1";
    public const string SignalName = @"Local\mon-switch.show-settings.v1";

    public static Version Version => typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public static string VersionText => Version.ToString(3);

    public static string FrameworkDescription =>
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    public static string OperatingSystemDescription => Environment.OSVersion.Version.ToString();
}
