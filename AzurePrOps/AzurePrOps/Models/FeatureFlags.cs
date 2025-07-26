namespace AzurePrOps.Models;

/// <summary>
/// Simple feature flags that can be persisted.
/// </summary>
public record FeatureFlags(bool InlineCommentsEnabled, bool LifecycleActionsEnabled);
