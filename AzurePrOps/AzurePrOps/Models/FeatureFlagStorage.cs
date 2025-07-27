using System;
using System.IO;
using System.Text.Json;

namespace AzurePrOps.Models;

/// <summary>
/// Handles persistence of feature flags to disk.
/// </summary>
public static class FeatureFlagStorage
{
    public static string? OverrideFilePath;

    private static string FilePath => OverrideFilePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "featureflags.json");

    public static FeatureFlags Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new FeatureFlags(true, true);

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<FeatureFlags>(json);
            return data ?? new FeatureFlags(true, true);
        }
        catch
        {
            return new FeatureFlags(true, true);
        }
    }

    public static void Save(FeatureFlags flags)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(flags);
        File.WriteAllText(FilePath, json);
    }
}
