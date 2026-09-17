using System.Reflection;

namespace MonSwitch.UI;

/// <summary>
/// Loads the single multi-resolution .ico that is embedded in the assembly.
/// Both the tray glyph and the window icon come from here, so there is exactly one icon
/// resource to replace when branding changes.
/// </summary>
internal static class AppIcons
{
    private const string ResourceName = "MonSwitch.app.ico";

    /// <summary>Size the notification area asks for, which differs per DPI.</summary>
    public static Icon LoadTrayIcon() => Load(SystemInformation.SmallIconSize) ?? SystemIcons.Application;

    /// <summary>32x32 window icon used by the Settings and About dialogs.</summary>
    public static Icon LoadWindowIcon() => Load(new Size(32, 32)) ?? SystemIcons.Application;

    private static Icon? Load(Size size)
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                Services.AppLog.Warn("embedded icon resource is missing: " + ResourceName);
                return null;
            }

            // Icon(Stream, Size) reads the whole stream and picks the best matching frame,
            // so the glyph stays crisp from 100% to 200% scaling.
            return new Icon(stream, size);
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn("embedded icon could not be loaded: " + ex.Message);
            return null;
        }
    }
}
