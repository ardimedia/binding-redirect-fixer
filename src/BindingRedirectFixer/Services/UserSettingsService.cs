using System.Text.Json;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Persists simple user preferences to a JSON file in %LOCALAPPDATA%.
/// Thread-safe for reads; writes are best-effort (errors are silently ignored).
/// </summary>
public static class UserSettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BindingRedirectFixer");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Loads settings from disk. Returns defaults if the file doesn't exist or is corrupt.
    /// </summary>
    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
            }
        }
        catch
        {
            // Corrupt or inaccessible — return defaults
        }

        return new UserSettings();
    }

    /// <summary>
    /// Saves settings to disk. Errors are silently ignored.
    /// </summary>
    public static void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort save
        }
    }
}

/// <summary>
/// User preferences that persist across sessions.
/// </summary>
public class UserSettings
{
    /// <summary>
    /// Detail panel split ratio (0.0–1.0). Represents the proportion
    /// of width allocated to the left (Version flow) panel.
    /// Default is 0.35 (35% left, 65% right — config XML needs more space).
    /// </summary>
    public double DetailSplitRatio { get; set; } = 0.35;

    /// <summary>Whether to create config file backups before applying fixes.</summary>
    public bool CreateBackup { get; set; } = true;
}
