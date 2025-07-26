namespace AzurePrOps.Models;

/// <summary>
/// Provides application-wide access to feature flags.
/// </summary>
public static class FeatureFlagManager
{
    private static bool _inlineCommentsEnabled;
    private static bool _lifecycleActionsEnabled;
    private static bool _autoRefreshEnabled;

    public static bool InlineCommentsEnabled
    {
        get => _inlineCommentsEnabled;
        set
        {
            if (_inlineCommentsEnabled != value)
            {
                _inlineCommentsEnabled = value;
                FeatureFlagStorage.Save(new FeatureFlags(_inlineCommentsEnabled, _lifecycleActionsEnabled, _autoRefreshEnabled));
            }
        }
    }

    public static bool LifecycleActionsEnabled
    {
        get => _lifecycleActionsEnabled;
        set
        {
            if (_lifecycleActionsEnabled != value)
            {
                _lifecycleActionsEnabled = value;
                FeatureFlagStorage.Save(new FeatureFlags(_inlineCommentsEnabled, _lifecycleActionsEnabled, _autoRefreshEnabled));
            }
        }
    }

    public static bool AutoRefreshEnabled
    {
        get => _autoRefreshEnabled;
        set
        {
            if (_autoRefreshEnabled != value)
            {
                _autoRefreshEnabled = value;
                FeatureFlagStorage.Save(new FeatureFlags(_inlineCommentsEnabled, _lifecycleActionsEnabled, _autoRefreshEnabled));
            }
        }
    }

    public static void Load()
    {
        var flags = FeatureFlagStorage.Load();
        _inlineCommentsEnabled = flags.InlineCommentsEnabled;
        _lifecycleActionsEnabled = flags.LifecycleActionsEnabled;
        _autoRefreshEnabled = flags.AutoRefreshEnabled;
    }
}
