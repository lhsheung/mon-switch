namespace MonSwitch.Core;

/// <summary>Interface languages shipped with mon-switch.</summary>
public enum AppLanguage
{
    /// <summary>Follow the Windows display language.</summary>
    System = 0,

    /// <summary>Cantonese (Hong Kong). Colloquial written Cantonese, not a machine translation.</summary>
    Yue = 1,

    /// <summary>Traditional Chinese.</summary>
    ZhHant = 2,

    /// <summary>Simplified Chinese.</summary>
    ZhHans = 3,

    English = 4,
}

public static class AppLanguages
{
    public const string SystemCode = "system";

    public static readonly AppLanguage[] All =
    [
        AppLanguage.System,
        AppLanguage.Yue,
        AppLanguage.ZhHant,
        AppLanguage.ZhHans,
        AppLanguage.English,
    ];

    /// <summary>Code stored in settings.json and used as the language-pack file name.</summary>
    public static string ToCode(this AppLanguage language) => language switch
    {
        AppLanguage.Yue => "yue",
        AppLanguage.ZhHant => "zh-Hant",
        AppLanguage.ZhHans => "zh-Hans",
        AppLanguage.English => "en",
        _ => SystemCode,
    };

    /// <summary>Fragment used to build resource keys such as "lang.zhHant".</summary>
    public static string ToKeySegment(this AppLanguage language) => language switch
    {
        AppLanguage.Yue => "yue",
        AppLanguage.ZhHant => "zhHant",
        AppLanguage.ZhHans => "zhHans",
        AppLanguage.English => "en",
        _ => "system",
    };

    public static string DisplayNameKey(this AppLanguage language) => "lang." + language.ToKeySegment();

    public static bool TryParseCode(string? code, out AppLanguage language)
    {
        language = AppLanguage.System;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        foreach (AppLanguage candidate in All)
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.OrdinalIgnoreCase))
            {
                language = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The concrete pack to load for a given setting. <see cref="AppLanguage.System"/> is
    /// resolved with <c>GetUserDefaultUILanguage()</c> so the mapping does not depend on ICU.
    /// A zh-HK system resolves to Traditional Chinese; Cantonese stays an explicit opt-in.
    /// </summary>
    public static AppLanguage Resolve(this AppLanguage language) =>
        language == AppLanguage.System ? Native.NativeMethods.GetSystemLanguage() : language;

    /// <summary>Pack file name without extension, e.g. "zh-Hant".</summary>
    public static string PackFile(this AppLanguage language) => language.Resolve().ToCode();
}
