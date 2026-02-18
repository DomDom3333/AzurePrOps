using System.IO;
using AzurePrOps.Models;
using AzurePrOps.Views;

namespace AzurePrOps.Tests;

public class DiffPreferencesAndExpansionBehaviorTests
{
    [Fact]
    public void DiffPreferences_PersistsExpandAllOnOpen_AcrossReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "diffsettings.json");
        DiffPreferencesStorage.OverrideFilePath = path;

        try
        {
            DiffPreferencesStorage.Save(new DiffPreferencesData(
                IgnoreWhitespace: true,
                WrapLines: false,
                IgnoreNewlines: true,
                ExpandAllOnOpen: false));

            DiffPreferences.ReloadFromStorage();
            Assert.False(DiffPreferences.ExpandAllOnOpen);

            DiffPreferences.ExpandAllOnOpen = true;
            DiffPreferences.ReloadFromStorage();

            Assert.True(DiffPreferences.ExpandAllOnOpen);
        }
        finally
        {
            DiffPreferencesStorage.OverrideFilePath = null;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CalculateAutoExpandCount_UsesPreferenceAndCap()
    {
        Assert.Equal(0, PullRequestDetailsWindow.CalculateAutoExpandCount(false, 50));
        Assert.Equal(0, PullRequestDetailsWindow.CalculateAutoExpandCount(true, 0));
        Assert.Equal(7, PullRequestDetailsWindow.CalculateAutoExpandCount(true, 7));
        Assert.Equal(10, PullRequestDetailsWindow.CalculateAutoExpandCount(true, 25));
    }
}
