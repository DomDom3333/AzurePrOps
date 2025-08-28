using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AzurePrOps.Models.FilteringAndSorting;

public enum SortField
{
    Title,
    Creator,
    CreatedDate,
    UpdatedDate,
    Status,
    SourceBranch,
    TargetBranch,
    ReviewerVote,
    PrId,
    ReviewerCount
}

public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Defines sorting criteria for pull requests
/// </summary>
public class SortCriteria : INotifyPropertyChanged
{
    public SortField PrimaryField { get; set; } = SortField.CreatedDate;
    public SortDirection PrimaryDirection { get; set; } = SortDirection.Descending;
    
    public SortField? SecondaryField { get; set; }
    public SortDirection SecondaryDirection { get; set; } = SortDirection.Ascending;
    
    public SortField? TertiaryField { get; set; }
    public SortDirection TertiaryDirection { get; set; } = SortDirection.Ascending;

    public string CurrentPreset { get; set; } = "Most Recent";

    /// <summary>
    /// Gets the list of active sort fields in order of priority
    /// </summary>
    public List<(SortField Field, SortDirection Direction)> GetSortFields()
    {
        var fields = new List<(SortField Field, SortDirection Direction)>
        {
            (PrimaryField, PrimaryDirection)
        };

        if (SecondaryField.HasValue)
        {
            fields.Add((SecondaryField.Value, SecondaryDirection));
        }

        if (TertiaryField.HasValue)
        {
            fields.Add((TertiaryField.Value, TertiaryDirection));
        }

        return fields;
    }

    /// <summary>
    /// Applies a sort preset
    /// </summary>
    public void ApplyPreset(string preset)
    {
        CurrentPreset = preset;
        
        switch (preset)
        {
            case "Most Recent":
                PrimaryField = SortField.CreatedDate;
                PrimaryDirection = SortDirection.Descending;
                SecondaryField = null;
                TertiaryField = null;
                break;

            case "Oldest First":
                PrimaryField = SortField.CreatedDate;
                PrimaryDirection = SortDirection.Ascending;
                SecondaryField = null;
                TertiaryField = null;
                break;

            case "Title A-Z":
                PrimaryField = SortField.Title;
                PrimaryDirection = SortDirection.Ascending;
                SecondaryField = SortField.CreatedDate;
                SecondaryDirection = SortDirection.Descending;
                TertiaryField = null;
                break;

            case "Title Z-A":
                PrimaryField = SortField.Title;
                PrimaryDirection = SortDirection.Descending;
                SecondaryField = SortField.CreatedDate;
                SecondaryDirection = SortDirection.Descending;
                TertiaryField = null;
                break;

            case "Creator A-Z":
                PrimaryField = SortField.Creator;
                PrimaryDirection = SortDirection.Ascending;
                SecondaryField = SortField.CreatedDate;
                SecondaryDirection = SortDirection.Descending;
                TertiaryField = null;
                break;

            case "Creator Z-A":
                PrimaryField = SortField.Creator;
                PrimaryDirection = SortDirection.Descending;
                SecondaryField = SortField.CreatedDate;
                SecondaryDirection = SortDirection.Descending;
                TertiaryField = null;
                break;

            case "Status Priority":
                PrimaryField = SortField.Status;
                PrimaryDirection = SortDirection.Ascending;
                SecondaryField = SortField.CreatedDate;
                SecondaryDirection = SortDirection.Descending;
                TertiaryField = null;
                break;

            case "Review Priority":
                PrimaryField = SortField.ReviewerVote;
                PrimaryDirection = SortDirection.Ascending;
                SecondaryField = SortField.CreatedDate;
                SecondaryDirection = SortDirection.Descending;
                TertiaryField = null;
                break;

            case "High Activity":
                PrimaryField = SortField.UpdatedDate;
                PrimaryDirection = SortDirection.Descending;
                SecondaryField = SortField.ReviewerCount;
                SecondaryDirection = SortDirection.Descending;
                TertiaryField = null;
                break;

            case "Needs Attention":
                PrimaryField = SortField.ReviewerVote;
                PrimaryDirection = SortDirection.Ascending;
                SecondaryField = SortField.UpdatedDate;
                SecondaryDirection = SortDirection.Ascending;
                TertiaryField = null;
                break;

            default:
                // Keep current settings for unknown presets
                break;
        }

        OnPropertyChanged();
    }

    /// <summary>
    /// Gets tooltip text for a sort preset
    /// </summary>
    public static string GetSortPresetTooltip(string preset)
    {
        return preset switch
        {
            "Most Recent" => "Sort by creation date, newest first",
            "Oldest First" => "Sort by creation date, oldest first",
            "Title A-Z" => "Sort by title alphabetically",
            "Title Z-A" => "Sort by title reverse alphabetically",
            "Creator A-Z" => "Sort by creator name alphabetically",
            "Creator Z-A" => "Sort by creator name reverse alphabetically",
            "Status Priority" => "Sort by status priority (Active, Draft, Completed, Abandoned)",
            "Review Priority" => "Sort by review votes (critical votes first)",
            "High Activity" => "Sort by recent updates and reviewer activity",
            "Needs Attention" => "Sort by PRs that need immediate attention",
            _ => "Custom sort criteria"
        };
    }

    /// <summary>
    /// Resets sorting to default (Created Date descending)
    /// </summary>
    public void Reset()
    {
        PrimaryField = SortField.CreatedDate;
        PrimaryDirection = SortDirection.Descending;
        SecondaryField = null;
        TertiaryField = null;
        CurrentPreset = "Most Recent";
        OnPropertyChanged();
    }

    /// <summary>
    /// Checks if sorting is at default settings
    /// </summary>
    public bool IsDefault => 
        PrimaryField == SortField.CreatedDate && 
        PrimaryDirection == SortDirection.Descending && 
        !SecondaryField.HasValue && 
        !TertiaryField.HasValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
