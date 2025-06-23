using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzurePrOps.Models;

public static class ConnectionSettingsStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "settings.json");

    public static bool TryLoad(out ConnectionSettings? settings)
    {
        settings = null;
        try
        {
            if (!File.Exists(FilePath))
                return false;

            var json = File.ReadAllText(FilePath);
            settings = JsonSerializer.Deserialize<ConnectionSettings>(json);
            return settings != null;
        }
        catch
        {
            settings = null;
            return false;
        }
    }

    public static void Save(ConnectionSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings);
        File.WriteAllText(FilePath, json);
    }

    public static async Task SaveAsync(ConnectionSettings connectionSettings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(connectionSettings);
        await File.WriteAllTextAsync(FilePath, json);
    }
}
