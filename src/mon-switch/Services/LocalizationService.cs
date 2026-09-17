using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MonSwitch.Core;

namespace MonSwitch.Services;

/// <summary>
/// Language packs are JSON files embedded as manifest resources
/// (<c>Resources/Lang/&lt;code&gt;.json</c> -&gt; <c>MonSwitch.Lang.&lt;code&gt;.json</c>).
///
/// Why JSON rather than .resx satellite assemblies:
///  * The pack for Cantonese is called "yue". .NET resolves satellite assemblies through
///    <see cref="CultureInfo"/>, and "yue" only exists when ICU carries the CLDR locale.
///    With NLS fallback (or on a machine without the locale) <c>new CultureInfo("yue")</c>
///    throws, which would silently downgrade the pack to a parent culture.
///  * mon-switch publishes with InvariantGlobalization=true, so culture-based lookup is
///    off the table by design.
///  * A flat JSON dictionary keeps the four packs trivially comparable, which is what
///    <c>--check-lang</c> verifies before every release.
/// </summary>
internal sealed class LocalizationService
{
    private const string ResourcePrefix = "MonSwitch.Lang.";

    /// <summary>
    /// The packs are hand-edited, so both // comments and trailing commas must be tolerated.
    /// Without these options <see cref="JsonSerializer"/> throws and the pack silently
    /// degrades to key names - exactly the failure "--check-lang" exists to catch.
    /// </summary>
    private static readonly JsonSerializerOptions PackJsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly AppLanguage[] PackLanguages =
    [
        AppLanguage.English,
        AppLanguage.ZhHant,
        AppLanguage.ZhHans,
        AppLanguage.Yue,
    ];

    private readonly object _gate = new();
    private readonly Dictionary<AppLanguage, Dictionary<string, string>> _cache = new();

    public static LocalizationService Instance { get; } = new();

    /// <summary>What the user picked (may be <see cref="AppLanguage.System"/>).</summary>
    public AppLanguage Configured { get; private set; } = AppLanguage.System;

    /// <summary>The pack actually in use - <see cref="AppLanguage.System"/> already resolved.</summary>
    public AppLanguage Resolved => Configured.Resolve();

    public event EventHandler? LanguageChanged;

    /// <summary>Applies a language. Raise <c>false</c> during start-up to avoid churn.</summary>
    public void Apply(AppLanguage language, bool raiseEvent = true)
    {
        Configured = language;

        // Warm the pack we need plus the English fallback.
        _ = LoadPackCached(language.Resolve());
        _ = LoadPackCached(AppLanguage.English);

        if (raiseEvent)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Text(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (LoadPackCached(Resolved).TryGetValue(key, out string? value) && value.Length > 0)
        {
            return value;
        }

        if (LoadPackCached(AppLanguage.English).TryGetValue(key, out string? fallback) && fallback.Length > 0)
        {
            return fallback;
        }

        // Returning the key itself makes a missing translation obvious in the UI and in --check-lang.
        return key;
    }

    public string Text(string key, params object?[] args)
    {
        string template = Text(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            // A malformed placeholder must never take the app down.
            AppLog.Warn($"language pack placeholder mismatch for '{key}'");
            return template;
        }
    }

    public static IReadOnlyList<AppLanguage> ShippedLanguages => PackLanguages;

    private Dictionary<string, string> LoadPackCached(AppLanguage language)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(language, out Dictionary<string, string>? cached))
            {
                return cached;
            }

            Dictionary<string, string> loaded = LoadPack(language);
            _cache[language] = loaded;
            return loaded;
        }
    }

    /// <summary>Reads a pack straight from the assembly. Used by the app and by --check-lang.</summary>
    public static Dictionary<string, string> LoadPack(AppLanguage language)
    {
        if (language == AppLanguage.System)
        {
            language = AppLanguage.English;
        }

        try
        {
            using Stream? stream = OpenPackStream(language);
            if (stream is null)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string json = reader.ReadToEnd();

            Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, PackJsonOptions);
            if (parsed is null)
            {
                AppLog.Error($"language pack '{language.ToCode()}' deserialised to null");
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            AppLog.Error($"language pack '{language.ToCode()}' could not be read", ex);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Opens the embedded pack. The exact logical name is tried first; a suffix match keeps
    /// this working even if the .csproj LogicalName metadata is renamed or dropped.
    /// </summary>
    private static Stream? OpenPackStream(AppLanguage language)
    {
        Assembly assembly = typeof(LocalizationService).Assembly;
        string exactName = ResourcePrefix + language.ToCode() + ".json";

        Stream? stream = assembly.GetManifestResourceStream(exactName);
        if (stream is not null)
        {
            return stream;
        }

        string suffix = ".lang." + language.ToCode() + ".json";
        string[] names = assembly.GetManifestResourceNames();

        foreach (string name in names)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return assembly.GetManifestResourceStream(name);
            }
        }

        AppLog.Error($"language pack missing: {exactName} (embedded resources: {string.Join(", ", names)})");
        return null;
    }

    /// <summary>Display name for the language menu, e.g. "Follow system (Traditional Chinese)".</summary>
    public static string DescribeLabel(AppLanguage language) =>
        language == AppLanguage.System
            ? Loc.T("lang.systemWithResolved", Loc.T(AppLanguage.System.Resolve().DisplayNameKey()))
            : Loc.T(language.DisplayNameKey());
}
