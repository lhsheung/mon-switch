using MonSwitch.Services;

namespace MonSwitch.Core;

/// <summary>
/// Very small command line parser. mon-switch intentionally ships no CLI framework:
/// the whole surface is five switches and a tray app has no console by default.
/// </summary>
internal sealed class CommandLine
{
    /// <summary>Accepted and ignored: the auto-start entry passes it, "silent" is the default.</summary>
    public bool Silent { get; private set; }

    public bool OpenSettings { get; private set; }

    public bool CheckLanguages { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool ShowVersion { get; private set; }

    /// <summary>First unrecognised switch, if any.</summary>
    public string? UnknownOption { get; private set; }

    public static CommandLine Parse(string[]? args)
    {
        var result = new CommandLine();
        if (args is null)
        {
            return result;
        }

        foreach (string raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string argument = raw.Trim();
            switch (argument.ToLowerInvariant())
            {
                case "--silent":
                case "/silent":
                    result.Silent = true;
                    break;

                case "--settings":
                case "/settings":
                    result.OpenSettings = true;
                    break;

                case "--check-lang":
                case "/check-lang":
                    result.CheckLanguages = true;
                    break;

                case "--version":
                case "-v":
                    result.ShowVersion = true;
                    break;

                case "--help":
                case "-h":
                case "-?":
                case "/?":
                    result.ShowHelp = true;
                    break;

                default:
                    if (argument.StartsWith('-') || argument.StartsWith('/'))
                    {
                        result.UnknownOption ??= argument;
                    }

                    break;
            }
        }

        return result;
    }

    public static string BuildHelp() => Loc.T("cli.help", AppInfo.Name + ".exe");
}
