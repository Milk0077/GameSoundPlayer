using System.Text.Json;

namespace GameSoundboard.Core.Settings;

public sealed class AppSettingsStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public AppSettingsStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameSoundboard");
    }

    public string ConfigPath => Path.Combine(_directory, "config.json");

    public AppSettings Load()
    {
        if (!File.Exists(ConfigPath)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), _options);
            if (settings is null || settings.Version != 1) return new AppSettings();
            settings.Sounds ??= [];
            settings.MusicPaths ??= [];
            settings.Hotkeys ??= [];
            settings.Buses ??= [];
            return settings;
        }
        catch (JsonException) { return new AppSettings(); }
        catch (IOException) { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = ConfigPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, _options));
        File.Move(temporaryPath, ConfigPath, true);
    }
}
