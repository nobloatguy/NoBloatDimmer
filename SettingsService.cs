using System.IO;
using System.Text.Json;

namespace NoBloatDimmer;

public static class SettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NoBloatTools",
        "NoBloatDimmer");

    private static readonly string SettingsPath = Path.Combine(
        SettingsDirectory,
        "settings.json");

    public static DimmerSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new DimmerSettings();
            }

            var json = File.ReadAllText(SettingsPath);

            return JsonSerializer.Deserialize<DimmerSettings>(json)
                   ?? new DimmerSettings();
        }
        catch
        {
            return new DimmerSettings();
        }
    }

    public static void Save(DimmerSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            var json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Do not crash the dimmer if Windows blocks a settings write.
        }
    }
}
