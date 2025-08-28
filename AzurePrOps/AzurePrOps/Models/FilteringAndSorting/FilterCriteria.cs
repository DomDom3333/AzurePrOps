using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AzurePrOps.Models.FilteringAndSorting;

/// <summary>
/// Defines all available filter criteria for pull requests
/// </summary>
public class FilterCriteria : INotifyPropertyChanged
{
    // User information for personal filters
    public string CurrentUserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;

    // Personal filters
    public bool MyPullRequestsOnly { get; set; } = false;
    public bool AssignedToMeOnly { get; set; } = false;
    public bool NeedsMyReviewOnly { get; set; } = false;
    public bool ExcludeMyPullRequests { get; set; } = false;

    // Status filters
    public List<string> SelectedStatuses { get; set; } = new();
    public bool? IsDraft { get; set; } = null; // null = all, true = drafts only, false = non-drafts only

    // Text filters
    public string GlobalSearchText { get; set; } = string.Empty;
    public string TitleFilter { get; set; } = string.Empty;
    public string CreatorFilter { get; set; } = string.Empty;
    public string ReviewerFilter { get; set; } = string.Empty;
    public string SourceBranchFilter { get; set; } = string.Empty;
    public string TargetBranchFilter { get; set; } = string.Empty;

    // Reviewer vote filters
    public List<string> SelectedReviewerVotes { get; set; } = new();

    // Date filters
    public DateTimeOffset? CreatedAfter { get; set; }
    public DateTimeOffset? CreatedBefore { get; set; }
    public DateTimeOffset? UpdatedAfter { get; set; }
    public DateTimeOffset? UpdatedBefore { get; set; }

    // Group filters
    public bool EnableGroupsWithoutVoteFilter { get; set; } = false;
    public List<string> GroupsWithoutVote { get; set; } = new();
    public List<string> SelectedGroupsWithoutVote { get; set; } = new();

    // Numeric filters
    public int? MinReviewerCount { get; set; }
    public int? MaxReviewerCount { get; set; }

    // Workflow preset tracking
    public string WorkflowPreset { get; set; } = string.Empty;

    /// <summary>
    /// Checks if any filters are currently active
    /// </summary>
    public bool HasActiveFilters => 
        MyPullRequestsOnly ||
        AssignedToMeOnly ||
        NeedsMyReviewOnly ||
        ExcludeMyPullRequests ||
        SelectedStatuses.Count > 0 ||
        IsDraft.HasValue ||
        !string.IsNullOrWhiteSpace(GlobalSearchText) ||
        !string.IsNullOrWhiteSpace(TitleFilter) ||
        !string.IsNullOrWhiteSpace(CreatorFilter) ||
        !string.IsNullOrWhiteSpace(ReviewerFilter) ||
        !string.IsNullOrWhiteSpace(SourceBranchFilter) ||
        !string.IsNullOrWhiteSpace(TargetBranchFilter) ||
        SelectedReviewerVotes.Count > 0 ||
        CreatedAfter.HasValue ||
        CreatedBefore.HasValue ||
        UpdatedAfter.HasValue ||
        UpdatedBefore.HasValue ||
        EnableGroupsWithoutVoteFilter ||
        MinReviewerCount.HasValue ||
        MaxReviewerCount.HasValue;

    /// <summary>
    /// Applies a workflow preset to the filter criteria
    /// </summary>
    public void ApplyWorkflowPreset(string preset)
    {
        WorkflowPreset = preset;
        // The actual preset logic is handled in the ViewModel
        // This just tracks which preset was applied
    }

    /// <summary>
    /// Resets all filters to their default state
    /// </summary>
    public void Reset()
    {
        MyPullRequestsOnly = false;
        AssignedToMeOnly = false;
        NeedsMyReviewOnly = false;
        ExcludeMyPullRequests = false;
        SelectedStatuses.Clear();
        IsDraft = null;
        GlobalSearchText = string.Empty;
        TitleFilter = string.Empty;
        CreatorFilter = string.Empty;
        ReviewerFilter = string.Empty;
        SourceBranchFilter = string.Empty;
        TargetBranchFilter = string.Empty;
        SelectedReviewerVotes.Clear();
        CreatedAfter = null;
        CreatedBefore = null;
        UpdatedAfter = null;
        UpdatedBefore = null;
        EnableGroupsWithoutVoteFilter = false;
        GroupsWithoutVote.Clear();
        SelectedGroupsWithoutVote.Clear();
        MinReviewerCount = null;
        MaxReviewerCount = null;
        WorkflowPreset = string.Empty;
        
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
