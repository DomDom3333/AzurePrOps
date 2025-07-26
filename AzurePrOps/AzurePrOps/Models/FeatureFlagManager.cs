namespace AzurePrOps.Models;

/// <summary>
/// Provides application-wide access to feature flags.
/// </summary>
public static class FeatureFlagManager
{
    private static bool _inlineCommentsEnabled;

    public static bool InlineCommentsEnabled
    {
        get => _inlineCommentsEnabled;
        set
        {
            if (_inlineCommentsEnabled != value)
            {
                _inlineCommentsEnabled = value;
                FeatureFlagStorage.Save(new FeatureFlags(_inlineCommentsEnabled));
            }
        }
    }

    public static void Load()
    {
        var flags = FeatureFlagStorage.Load();
        _inlineCommentsEnabled = flags.InlineCommentsEnabled;
    }
}
