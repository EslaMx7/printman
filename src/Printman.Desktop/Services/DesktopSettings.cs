using System.Text.Json;
using System.Text.Json.Serialization;

namespace Printman.Desktop.Services;

public sealed class DesktopSettings
{
    public bool EnableSharing { get; set; } = true;
    public bool ShareAllPrinters { get; set; }
    public List<string> SelectedPrinters { get; set; } = [];
    public bool EnableWebUi { get; set; } = true;
    public int WebPort { get; set; } = 5000;
    public bool RequirePin { get; set; } = true;
    public string? CustomPin { get; set; }
    public bool AutoStartServer { get; set; }
    public bool KeepCache { get; set; }
}

[JsonSerializable(typeof(DesktopSettings))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DesktopSettingsJsonContext : JsonSerializerContext;

public static class DesktopSettingsStore
{
    public static string? OverrideConfigDirectory { get; set; }

    public static string GetConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(OverrideConfigDirectory))
        {
            return Path.Combine(OverrideConfigDirectory, "desktop.json");
        }

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Printman", "desktop.json");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(baseDir, "printman", "desktop.json");
    }

    public static DesktopSettings Load()
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path))
            {
                return new DesktopSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, DesktopSettingsJsonContext.Default.DesktopSettings)
                ?? new DesktopSettings();
        }
        catch
        {
            return new DesktopSettings();
        }
    }

    public static void Save(DesktopSettings settings)
    {
        try
        {
            var path = GetConfigPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, DesktopSettingsJsonContext.Default.DesktopSettings);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore settings write errors on read-only environments
        }
    }
}
