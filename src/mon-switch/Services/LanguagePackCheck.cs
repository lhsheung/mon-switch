using MonSwitch.Core;

namespace MonSwitch.Services;

/// <summary>
/// <c>mon-switch.exe --check-lang</c>: proves that all four packs carry exactly the same key
/// set and that no value is empty. Wired into build.ps1 so a half-translated pack cannot ship.
/// Exit code 0 = all packs complete, 1 = problems found, 2 = English pack itself is missing.
/// </summary>
internal static class LanguagePackCheck
{
    public static int Run()
    {
        ConsoleBridge.Attach();

        var packs = new Dictionary<AppLanguage, Dictionary<string, string>>();
        foreach (AppLanguage language in LocalizationService.ShippedLanguages)
        {
            packs[language] = LocalizationService.LoadPack(language);
        }

        Dictionary<string, string> reference = packs[AppLanguage.English];
        if (reference.Count == 0)
        {
            ConsoleBridge.WriteLine(Loc.T("cli.langCheckNoReference"));
            return 2;
        }

        ConsoleBridge.WriteLine(Loc.T("cli.langCheckHeader", reference.Count));
        int problems = 0;

        foreach (AppLanguage language in LocalizationService.ShippedLanguages)
        {
            Dictionary<string, string> pack = packs[language];

            List<string> missing = reference.Keys
                .Where(key => !pack.ContainsKey(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            List<string> extra = pack.Keys
                .Where(key => !reference.ContainsKey(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            List<string> empty = pack
                .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => pair.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            if (missing.Count == 0 && extra.Count == 0 && empty.Count == 0)
            {
                ConsoleBridge.WriteLine(Loc.T("cli.langCheckOk", language.ToCode(), pack.Count));
                continue;
            }

            problems += missing.Count + extra.Count + empty.Count;

            if (missing.Count > 0)
            {
                ConsoleBridge.WriteLine(Loc.T("cli.langCheckMissing", language.ToCode(), missing.Count, string.Join(", ", missing)));
            }

            if (extra.Count > 0)
            {
                ConsoleBridge.WriteLine(Loc.T("cli.langCheckExtra", language.ToCode(), extra.Count, string.Join(", ", extra)));
            }

            if (empty.Count > 0)
            {
                ConsoleBridge.WriteLine(Loc.T("cli.langCheckEmpty", language.ToCode(), empty.Count, string.Join(", ", empty)));
            }
        }

        ConsoleBridge.WriteLine(problems == 0
            ? Loc.T("cli.langCheckPassed")
            : Loc.T("cli.langCheckFailed", problems));

        return problems == 0 ? 0 : 1;
    }
}
