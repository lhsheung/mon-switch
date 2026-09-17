using System.Text;
using MonSwitch.Native;

namespace MonSwitch.Services;

/// <summary>
/// Gives the WinExe a usable console for the three developer switches
/// (<c>--help</c>, <c>--version</c>, <c>--check-lang</c>) by attaching to the console that
/// launched it, or allocating one when double-clicked.
/// </summary>
internal static class ConsoleBridge
{
    private static bool _attached;

    public static void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;

        bool redirected = NativeMethods.IsStandardOutputRedirected();
        if (!redirected)
        {
            NativeMethods.AttachToParentConsole();
        }

        try
        {
            // Re-bind Console.Out: the default writer was captured before the handle existed.
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(stdout);
        }
        catch
        {
            // No usable console - Console.WriteLine becomes a no-op, which is fine.
        }

        if (!redirected)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Some hosts refuse the encoding change; the output is still readable.
            }
        }
    }

    public static void WriteLine(string text)
    {
        try
        {
            Console.WriteLine(text);
        }
        catch
        {
            // Ignore: a missing console must not fail a build-time check.
        }
    }

    public static void WriteLine()
    {
        try
        {
            Console.WriteLine();
        }
        catch
        {
            // Ignore, as above.
        }
    }
}
