using System;
using System.IO;
using System.Text.Json;

namespace AzurePrOps.Models;

public record DiffPreferencesData(bool IgnoreWhitespace, bool WrapLines);

public static class DiffPreferencesStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "diffsettings.json");

    public static DiffPreferencesData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new DiffPreferencesData(false, false);

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<DiffPreferencesData>(json);
            return data ?? new DiffPreferencesData(false, false);
        }
        catch
        {
            return new DiffPreferencesData(false, false);
        }
    }

    public static void Save(DiffPreferencesData data)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data);
        File.WriteAllText(FilePath, json);
    }
}
