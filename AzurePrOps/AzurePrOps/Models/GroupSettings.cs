using System;
using System.Collections.Generic;

namespace AzurePrOps.Models;

public record GroupSettings(
    List<string> AvailableGroups,
    List<string> SelectedGroups,
    DateTime LastUpdated)
{
    public List<string> AvailableGroups { get; init; } = AvailableGroups ?? new List<string>();
    public List<string> SelectedGroups { get; init; } = SelectedGroups ?? new List<string>();
    
    public bool IsExpired => DateTime.Now - LastUpdated > TimeSpan.FromHours(24);
}
