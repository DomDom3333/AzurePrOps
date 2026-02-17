using System;
using System.Collections.Generic;
using System.Linq;

namespace AzurePrOps.Models.FilteringAndSorting;

/// <summary>
/// Contains both filter and sort preferences that can be saved and restored
/// </summary>
public class FilterSortPreferences
{
    public FilterCriteria FilterCriteria { get; set; } = new();
    public SortCriteria SortCriteria { get; set; } = new();
    
    // Group filtering preferences
    public bool EnableGroupFiltering { get; set; } = false;
    public List<string> SelectedGroups { get; set; } = new();
    
    // Saved filter views collection
    public List<SavedFilterView> SavedViews { get; set; } = new();
    
    // Metadata
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsed { get; set; } = DateTime.Now;
    public string LastSelectedView { get; set; } = string.Empty;

    /// <summary>
    /// Creates a deep copy of the preferences
    /// </summary>
    public FilterSortPreferences Clone()
    {
        return new FilterSortPreferences
        {
            FilterCriteria = new FilterCriteria
            {
                CurrentUserId = FilterCriteria.CurrentUserId,
                UserDisplayName = FilterCriteria.UserDisplayName,
                MyPullRequestsOnly = FilterCriteria.MyPullRequestsOnly,
                AssignedToMeOnly = FilterCriteria.AssignedToMeOnly,
                NeedsMyReviewOnly = FilterCriteria.NeedsMyReviewOnly,
                ExcludeMyPullRequests = FilterCriteria.ExcludeMyPullRequests,
                SelectedStatuses = new List<string>(FilterCriteria.SelectedStatuses),
                IsDraft = FilterCriteria.IsDraft,
                GlobalSearchText = FilterCriteria.GlobalSearchText,
                TitleFilter = FilterCriteria.TitleFilter,
                CreatorFilter = FilterCriteria.CreatorFilter,
                ReviewerFilter = FilterCriteria.ReviewerFilter,
                SourceBranchFilter = FilterCriteria.SourceBranchFilter,
                TargetBranchFilter = FilterCriteria.TargetBranchFilter,
                SelectedReviewerVotes = new List<string>(FilterCriteria.SelectedReviewerVotes),
                CreatedAfter = FilterCriteria.CreatedAfter,
                CreatedBefore = FilterCriteria.CreatedBefore,
                UpdatedAfter = FilterCriteria.UpdatedAfter,
                UpdatedBefore = FilterCriteria.UpdatedBefore,
                EnableGroupsWithoutVoteFilter = FilterCriteria.EnableGroupsWithoutVoteFilter,
                SelectedGroupsWithoutVote = new List<string>(FilterCriteria.SelectedGroupsWithoutVote),
                MinReviewerCount = FilterCriteria.MinReviewerCount,
                MaxReviewerCount = FilterCriteria.MaxReviewerCount,
                WorkflowPreset = FilterCriteria.WorkflowPreset
            },
            SortCriteria = new SortCriteria
            {
                PrimaryField = SortCriteria.PrimaryField,
                PrimaryDirection = SortCriteria.PrimaryDirection,
                SecondaryField = SortCriteria.SecondaryField,
                SecondaryDirection = SortCriteria.SecondaryDirection,
                TertiaryField = SortCriteria.TertiaryField,
                TertiaryDirection = SortCriteria.TertiaryDirection,
                CurrentPreset = SortCriteria.CurrentPreset
            },
            EnableGroupFiltering = EnableGroupFiltering,
            SelectedGroups = new List<string>(SelectedGroups),
            SavedViews = SavedViews.Select(sv => SavedFilterView.FromCurrentSettings(
                sv.Name, sv.Description, sv.FilterCriteria, sv.SortCriteria, 
                sv.EnableGroupFiltering, sv.SelectedGroups)).ToList(),
            Name = Name,
            Description = Description,
            CreatedAt = CreatedAt,
            LastUsed = LastUsed,
            LastSelectedView = LastSelectedView
        };
    }

    /// <summary>
    /// Resets all preferences to default values
    /// </summary>
    public void Reset()
    {
        FilterCriteria.Reset();
        SortCriteria.Reset();
        EnableGroupFiltering = false;
        SelectedGroups.Clear();
        SavedViews.Clear();
        LastSelectedView = string.Empty;
    }
}
