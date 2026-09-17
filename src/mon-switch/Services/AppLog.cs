using System.Text;
using MonSwitch.Core;

namespace MonSwitch.Services;

/// <summary>
/// Optional rolling diagnostic log. Logging is OFF by default (see the Diagnostics tab):
/// a tray utility that runs for months should not silently grow a file on disk.
/// When enabled the file is capped and rotated once, so the ceiling is ~512 KB.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;

    private static readonly object Gate = new();
    private static bool _enabled;

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.DataFolderName,
        "logs");

    public static string FilePath => Path.Combine(DirectoryPath, AppInfo.Name + ".log");

    public static bool IsEnabled => _enabled;

    public static void Configure(bool enabled)
    {
        bool wasEnabled = _enabled;
        _enabled = enabled;

        if (enabled && !wasEnabled)
        {
            Info("---- logging enabled ----");
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    /// <summary>
    /// Writes even when logging is switched off. Reserved for unhandled exceptions and
    /// start-up failures - without a record on disk a crash report cannot be acted on.
    /// </summary>
    public static void Always(string message, Exception? exception = null) => WriteCore("FATAL", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_enabled)
        {
            WriteCore(level, message, exception);
        }
    }

    private static void WriteCore(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                RollIfNeeded();

                var builder = new StringBuilder(160);
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                builder.Append(" [").Append(level).Append("] ").Append(message);
                if (exception is not null)
                {
                    builder.AppendLine().Append(exception);
                }

                File.AppendAllText(FilePath, builder.AppendLine().ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never be the reason the app fails.
        }
    }

    private static void RollIfNeeded()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length <= MaxBytes)
        {
            return;
        }

        try
        {
            File.Move(FilePath, FilePath + ".1", overwrite: true);
        }
        catch
        {
            // If rotation fails we simply keep appending; the cap is best-effort.
        }
    }
}
