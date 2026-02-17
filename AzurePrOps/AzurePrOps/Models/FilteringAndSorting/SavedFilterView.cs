using System;
using System.Collections.Generic;
using System.Linq;

namespace AzurePrOps.Models.FilteringAndSorting;

/// <summary>
/// Represents a saved filter view that can be applied to restore specific filter and sort settings
/// </summary>
public class SavedFilterView
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsed { get; set; } = DateTime.Now;
    
    /// <summary>
    /// The filter criteria associated with this saved view
    /// </summary>
    public FilterCriteria FilterCriteria { get; set; } = new();
    
    /// <summary>
    /// The sort criteria associated with this saved view
    /// </summary>
    public SortCriteria SortCriteria { get; set; } = new();
    
    /// <summary>
    /// Group filtering settings for this saved view
    /// </summary>
    public bool EnableGroupFiltering { get; set; } = false;
    public List<string> SelectedGroups { get; set; } = new();

    /// <summary>
    /// Creates a saved filter view from current filter and sort criteria
    /// </summary>
    public static SavedFilterView FromCurrentSettings(
        string name,
        string description,
        FilterCriteria filterCriteria,
        SortCriteria sortCriteria,
        bool enableGroupFiltering = false,
        List<string>? selectedGroups = null)
    {
        return new SavedFilterView
        {
            Name = name,
            Description = description,
            FilterCriteria = CloneFilterCriteria(filterCriteria),
            SortCriteria = CloneSortCriteria(sortCriteria),
            EnableGroupFiltering = enableGroupFiltering,
            SelectedGroups = selectedGroups?.ToList() ?? new List<string>(),
            CreatedAt = DateTime.Now,
            LastUsed = DateTime.Now
        };
    }

    /// <summary>
    /// Creates a deep copy of filter criteria
    /// </summary>
    private static FilterCriteria CloneFilterCriteria(FilterCriteria original)
    {
        return new FilterCriteria
        {
            CurrentUserId = original.CurrentUserId,
            UserDisplayName = original.UserDisplayName,
            MyPullRequestsOnly = original.MyPullRequestsOnly,
            AssignedToMeOnly = original.AssignedToMeOnly,
            NeedsMyReviewOnly = original.NeedsMyReviewOnly,
            ExcludeMyPullRequests = original.ExcludeMyPullRequests,
            SelectedStatuses = new List<string>(original.SelectedStatuses),
            IsDraft = original.IsDraft,
            GlobalSearchText = original.GlobalSearchText,
            TitleFilter = original.TitleFilter,
            CreatorFilter = original.CreatorFilter,
            ReviewerFilter = original.ReviewerFilter,
            SourceBranchFilter = original.SourceBranchFilter,
            TargetBranchFilter = original.TargetBranchFilter,
            SelectedReviewerVotes = new List<string>(original.SelectedReviewerVotes),
            CreatedAfter = original.CreatedAfter,
            CreatedBefore = original.CreatedBefore,
            UpdatedAfter = original.UpdatedAfter,
            UpdatedBefore = original.UpdatedBefore,
            EnableGroupsWithoutVoteFilter = original.EnableGroupsWithoutVoteFilter,
            SelectedGroupsWithoutVote = new List<string>(original.SelectedGroupsWithoutVote),
            MinReviewerCount = original.MinReviewerCount,
            MaxReviewerCount = original.MaxReviewerCount,
            WorkflowPreset = original.WorkflowPreset
        };
    }

    /// <summary>
    /// Creates a deep copy of sort criteria
    /// </summary>
    private static SortCriteria CloneSortCriteria(SortCriteria original)
    {
        return new SortCriteria
        {
            PrimaryField = original.PrimaryField,
            PrimaryDirection = original.PrimaryDirection,
            SecondaryField = original.SecondaryField,
            SecondaryDirection = original.SecondaryDirection,
            TertiaryField = original.TertiaryField,
            TertiaryDirection = original.TertiaryDirection,
            CurrentPreset = original.CurrentPreset
        };
    }

    /// <summary>
    /// Updates the last used timestamp
    /// </summary>
    public void MarkAsUsed()
    {
        LastUsed = DateTime.Now;
    }

    public override string ToString()
    {
        return Name;
    }
}
