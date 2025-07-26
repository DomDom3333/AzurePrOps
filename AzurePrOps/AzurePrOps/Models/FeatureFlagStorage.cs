using System;
using System.IO;
using System.Text.Json;

namespace AzurePrOps.Models;

/// <summary>
/// Handles persistence of feature flags to disk.
/// </summary>
public static class FeatureFlagStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "featureflags.json");

    public static FeatureFlags Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new FeatureFlags(false, false);

            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<FeatureFlags>(json);
            return data ?? new FeatureFlags(false, false);
        }
        catch
        {
            return new FeatureFlags(false, false);
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
