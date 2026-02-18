using System;
using System.IO;
using System.Text.Json;

namespace AzurePrOps.Models;

public record DiffPreferencesData(bool IgnoreWhitespace, bool WrapLines, bool IgnoreNewlines, bool ExpandAllOnOpen);

public static class DiffPreferencesStorage
{
    private static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "diffsettings.json");

    public static string? OverrideFilePath { get; set; }

    private static string FilePath => OverrideFilePath ?? DefaultFilePath;

    public static DiffPreferencesData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new DiffPreferencesData(true, false, true, true);

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<DiffPreferencesData>(json);
            return data ?? new DiffPreferencesData(true, false, true, true);
        }
        catch
        {
            return new DiffPreferencesData(true, false, true, true);
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
