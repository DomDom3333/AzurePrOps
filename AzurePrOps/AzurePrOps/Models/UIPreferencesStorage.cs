using System;
using System.IO;
using System.Text.Json;

namespace AzurePrOps.Models;

public record UIPreferencesData(bool AutoRefreshEnabled, int SelectedThemeIndex, int RefreshIntervalSeconds, bool ShowNotifications, bool MinimizeToTray);

public static class UIPreferencesStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "uisettings.json");

    public static UIPreferencesData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new UIPreferencesData(true, 0, 60, true, false);

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<UIPreferencesData>(json);
            return data ?? new UIPreferencesData(true, 0, 60, true, false);
        }
        catch
        {
            return new UIPreferencesData(true, 0, 60, true, false);
        }
    }

    public static void Save(UIPreferencesData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data);
        File.WriteAllText(FilePath, json);
    }
}