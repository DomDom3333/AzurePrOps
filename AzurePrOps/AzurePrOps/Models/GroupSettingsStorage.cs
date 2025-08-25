using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzurePrOps.Models;

public static class GroupSettingsStorage
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "group-settings.json");

    public static async Task<GroupSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new GroupSettings(new List<string>(), new List<string>(), DateTime.MinValue);
            }

            var json = await File.ReadAllTextAsync(SettingsPath);
            var settings = JsonSerializer.Deserialize<GroupSettings>(json);
            return settings ?? new GroupSettings(new List<string>(), new List<string>(), DateTime.MinValue);
        }
        catch
        {
            return new GroupSettings(new List<string>(), new List<string>(), DateTime.MinValue);
        }
    }

    public static async Task SaveAsync(GroupSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(SettingsPath, json);
        }
        catch
        {
            // Silently ignore save errors
        }
    }
}
