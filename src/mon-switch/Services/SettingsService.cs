using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonSwitch.Core;

namespace MonSwitch.Services;

/// <summary>
/// Loads and saves <c>%AppData%\mon-switch\settings.json</c>.
/// A damaged file is moved aside (never deleted) and the defaults are used, so the app
/// always starts; the caller turns <see cref="WasRepaired"/> into a user-visible notice.
/// </summary>
internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.DataFolderName);

    public string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>True when the file on disk was unreadable and defaults took over.</summary>
    public bool WasRepaired { get; private set; }

    /// <summary>Where the unreadable file was moved, if the move succeeded.</summary>
    public string? RepairBackupPath { get; private set; }

    public string? LastError { get; private set; }

    public void Load()
    {
        WasRepaired = false;
        RepairBackupPath = null;
        LastError = null;

        AppSettings loaded = new();

        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    AppSettings? parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (parsed is not null)
                    {
                        loaded = parsed;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            WasRepaired = true;
            AppLog.Error("settings.json could not be parsed, falling back to defaults", ex);
            QuarantineBrokenFile();
            loaded = new AppSettings();
        }

        loaded.Normalize();
        Current = loaded;

        // First run (or after a damaged file was quarantined): materialise the defaults on disk,
        // so the file documented in the README actually exists and can be hand-edited without
        // having to change something through the UI first.
        if (!File.Exists(FilePath))
        {
            Save(loaded, out _);
        }
    }

    public bool Save(AppSettings settings, out string? error)
    {
        error = null;

        try
        {
            settings.Normalize();
            Directory.CreateDirectory(DirectoryPath);

            string json = JsonSerializer.Serialize(settings, JsonOptions);

            // Write-then-swap so a crash mid-write cannot leave a truncated settings file.
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, FilePath, overwrite: true);

            Current = settings;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("settings.json could not be written", ex);
            return false;
        }
    }

    private void QuarantineBrokenFile()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string backup = Path.Combine(DirectoryPath, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(FilePath, backup, overwrite: true);
            RepairBackupPath = backup;
        }
        catch (Exception ex)
        {
            AppLog.Warn("could not quarantine the damaged settings file: " + ex.Message);
            RepairBackupPath = null;
        }
    }
}
