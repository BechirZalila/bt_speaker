using System.Text.Json;

namespace BtBatteryTray;

/// <summary>
/// Préférences de l'utilisateur, enregistrées dans %APPDATA%\BtBatteryTray\settings.json.
/// </summary>
internal sealed class Settings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BtBatteryTray", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Identifiants (InstanceId) des appareils à ne pas afficher.</summary>
    public HashSet<string> HiddenDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool LowBatteryAlert { get; set; } = true;

    public int LowBatteryThreshold { get; set; } = 20;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions);
                if (settings is not null)
                {
                    settings.HiddenDevices = new HashSet<string>(settings.HiddenDevices, StringComparer.OrdinalIgnoreCase);
                    settings.LowBatteryThreshold = Math.Clamp(settings.LowBatteryThreshold, 1, 99);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Write($"Préférences illisibles, valeurs par défaut utilisées : {ex.Message}");
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Échec de l'enregistrement des préférences : {ex.Message}");
        }
    }
}
