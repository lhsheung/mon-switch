namespace MonSwitch.Services;

/// <summary>
/// Short facade so call sites can read as <c>Loc.T("key")</c>.
/// Every user-visible string in mon-switch goes through here (or through
/// <see cref="LocalizationService"/> directly). No littered string literals.
/// </summary>
internal static class Loc
{
    public static string T(string key) => LocalizationService.Instance.Text(key);

    public static string T(string key, params object?[] args) => LocalizationService.Instance.Text(key, args);
}
