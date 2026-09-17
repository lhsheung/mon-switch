using System.Runtime.InteropServices;
using MonSwitch.Core;

namespace MonSwitch.Native;

/// <summary>Thin P/Invoke surface for user32 / kernel32 beyond the display configuration API.</summary>
internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;

    /// <summary>Sent to every top-level window before Windows logs off or restarts.</summary>
    public const int WmQueryEndSession = 0x0011;

    /// <summary>Sent once the session really is ending; the reply no longer matters.</summary>
    public const int WmEndSession = 0x0016;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED.</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;

    private const int SmRemoteSession = 0x1000;
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const uint FileTypeChar = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr hFile);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    /// <summary>True inside an RDP / RemoteApp session, where topology switching is not supported.</summary>
    public static bool IsRemoteSession() => GetSystemMetrics(SmRemoteSession) != 0;

    /// <summary>
    /// Maps the Windows display language to one of our packs without touching ICU.
    /// This is what makes <c>InvariantGlobalization</c> safe to enable.
    /// LANGID layout: low 10 bits are the primary language, high bits the sub-language.
    /// </summary>
    public static AppLanguage GetSystemLanguage()
    {
        int langId = GetUserDefaultUILanguage();
        int primary = langId & 0x03FF;

        return langId switch
        {
            0x0C04 => AppLanguage.ZhHant, // zh-HK
            0x0404 => AppLanguage.ZhHant, // zh-TW
            0x1404 => AppLanguage.ZhHant, // zh-MO
            0x0804 => AppLanguage.ZhHans, // zh-CN
            0x1004 => AppLanguage.ZhHans, // zh-SG
            _ when primary == 0x04 => AppLanguage.ZhHant, // any other Chinese variant
            _ when primary == 0x09 => AppLanguage.English,
            _ => AppLanguage.English,
        };
    }

    public static void AttachToParentConsole()
    {
        try
        {
            if (!AttachConsole(AttachParentProcess))
            {
                AllocConsole();
            }
        }
        catch
        {
            // No console available (double-clicked from Explorer) - nothing to do.
        }
    }

    /// <summary>
    /// True when stdout is a pipe or a file rather than a console, i.e. the caller did
    /// something like "mon-switch.exe --check-lang &gt; report.txt". In that case we must not
    /// attach or allocate a console, or the output would go to the wrong place.
    /// </summary>
    public static bool IsStandardOutputRedirected()
    {
        try
        {
            IntPtr handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return false;
            }

            return GetFileType(handle) != FileTypeChar;
        }
        catch
        {
            return false;
        }
    }
}
