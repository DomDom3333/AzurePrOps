using AzurePrOps.Models;
using System.IO;
using Xunit;

namespace AzurePrOps.Tests;

public class FeatureFlagStorageTests
{
    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "flags.json");
        FeatureFlagStorage.OverrideFilePath = path;

        try
        {
            FeatureFlagStorage.Save(new FeatureFlags(true, false));
            var loaded = FeatureFlagStorage.Load();
            Assert.True(loaded.InlineCommentsEnabled);
            Assert.False(loaded.LifecycleActionsEnabled);
        }
        finally
        {
            FeatureFlagStorage.OverrideFilePath = null;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "flags.json");
        FeatureFlagStorage.OverrideFilePath = path;

        try
        {
            var loaded = FeatureFlagStorage.Load();
            Assert.True(loaded.InlineCommentsEnabled);
            Assert.True(loaded.LifecycleActionsEnabled);
        }
        finally
        {
            FeatureFlagStorage.OverrideFilePath = null;
            Directory.Delete(dir, true);
        }
    }
}

