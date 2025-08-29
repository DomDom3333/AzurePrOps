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

    // Filter source tracking for better UX
    public string FilterSource { get; set; } = "Manual";
    public string CurrentFilterSource { get; set; } = "Manual";
    public string CurrentFilterSourceName { get; set; } = string.Empty;
    public string WorkflowPreset { get; set; } = string.Empty;
    public DateTime LastApplied { get; set; } = DateTime.Now;

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
    /// Gets a human-readable summary of currently active filters
    /// </summary>
    public string ActiveFiltersSummary
    {
        get
        {
            var filters = new List<string>();
            
            if (MyPullRequestsOnly) filters.Add("My PRs");
            if (AssignedToMeOnly) filters.Add("Assigned to me");
            if (NeedsMyReviewOnly) filters.Add("Needs my review");
            if (ExcludeMyPullRequests) filters.Add("Exclude my PRs");
            
            if (SelectedStatuses.Count > 0) 
                filters.Add($"Status: {string.Join(", ", SelectedStatuses)}");
            
            if (IsDraft.HasValue)
                filters.Add(IsDraft.Value ? "Drafts only" : "Non-drafts only");
            
            if (!string.IsNullOrWhiteSpace(GlobalSearchText))
                filters.Add($"Search: \"{GlobalSearchText}\"");
            
            if (!string.IsNullOrWhiteSpace(TitleFilter))
                filters.Add($"Title: \"{TitleFilter}\"");
            
            if (!string.IsNullOrWhiteSpace(CreatorFilter))
                filters.Add($"Creator: \"{CreatorFilter}\"");
            
            if (!string.IsNullOrWhiteSpace(ReviewerFilter))
                filters.Add($"Reviewer: \"{ReviewerFilter}\"");
            
            if (!string.IsNullOrWhiteSpace(SourceBranchFilter))
                filters.Add($"Source: \"{SourceBranchFilter}\"");
            
            if (!string.IsNullOrWhiteSpace(TargetBranchFilter))
                filters.Add($"Target: \"{TargetBranchFilter}\"");
            
            if (SelectedReviewerVotes.Count > 0)
                filters.Add($"Votes: {string.Join(", ", SelectedReviewerVotes)}");
            
            if (CreatedAfter.HasValue || CreatedBefore.HasValue)
            {
                var dateRange = "Created: ";
                if (CreatedAfter.HasValue) dateRange += $"after {CreatedAfter.Value:yyyy-MM-dd}";
                if (CreatedBefore.HasValue) 
                {
                    if (CreatedAfter.HasValue) dateRange += " and ";
                    dateRange += $"before {CreatedBefore.Value:yyyy-MM-dd}";
                }
                filters.Add(dateRange);
            }
            
            if (UpdatedAfter.HasValue || UpdatedBefore.HasValue)
            {
                var dateRange = "Updated: ";
                if (UpdatedAfter.HasValue) dateRange += $"after {UpdatedAfter.Value:yyyy-MM-dd}";
                if (UpdatedBefore.HasValue) 
                {
                    if (UpdatedAfter.HasValue) dateRange += " and ";
                    dateRange += $"before {UpdatedBefore.Value:yyyy-MM-dd}";
                }
                filters.Add(dateRange);
            }
            
            if (MinReviewerCount.HasValue || MaxReviewerCount.HasValue)
            {
                var reviewerRange = "Reviewers: ";
                if (MinReviewerCount.HasValue && MaxReviewerCount.HasValue)
                    reviewerRange += $"{MinReviewerCount}-{MaxReviewerCount}";
                else if (MinReviewerCount.HasValue)
                    reviewerRange += $"≥{MinReviewerCount}";
                else
                    reviewerRange += $"≤{MaxReviewerCount}";
                filters.Add(reviewerRange);
            }
            
            if (EnableGroupsWithoutVoteFilter && SelectedGroupsWithoutVote.Count > 0)
                filters.Add($"Groups without vote: {string.Join(", ", SelectedGroupsWithoutVote)}");
            
            return filters.Count > 0 ? string.Join(" • ", filters) : "No filters applied";
        }
    }

    /// <summary>
    /// Gets the current filter source display text
    /// </summary>
    public string FilterSourceDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentFilterSourceName))
                return CurrentFilterSource;
            
            return CurrentFilterSource switch
            {
                "Preset" => $"Preset: {CurrentFilterSourceName}",
                "SavedView" => $"Saved View: {CurrentFilterSourceName}",
                _ => "Manual Filters"
            };
        }
    }

    /// <summary>
    /// Sets the filter source information for tracking
    /// </summary>
    public void SetFilterSource(string source, string sourceName = "")
    {
        CurrentFilterSource = source;
        CurrentFilterSourceName = sourceName;
        LastApplied = DateTime.Now;
        OnPropertyChanged(nameof(CurrentFilterSource));
        OnPropertyChanged(nameof(FilterSourceDisplay));
    }

    /// <summary>
    /// Applies a workflow preset to the filter criteria
    /// </summary>
    public void ApplyWorkflowPreset(string preset)
    {
        WorkflowPreset = preset;
        SetFilterSource("Preset", preset);
    }

    /// <summary>
    /// Resets all filter criteria to their default values
    /// </summary>
    public void Reset()
    {
        // Personal filters
        MyPullRequestsOnly = false;
        AssignedToMeOnly = false;
        NeedsMyReviewOnly = false;
        ExcludeMyPullRequests = false;

        // Status filters
        SelectedStatuses.Clear();
        IsDraft = null;

        // Text filters
        GlobalSearchText = string.Empty;
        TitleFilter = string.Empty;
        CreatorFilter = string.Empty;
        ReviewerFilter = string.Empty;
        SourceBranchFilter = string.Empty;
        TargetBranchFilter = string.Empty;

        // Reviewer vote filters
        SelectedReviewerVotes.Clear();

        // Date filters
        CreatedAfter = null;
        CreatedBefore = null;
        UpdatedAfter = null;
        UpdatedBefore = null;

        // Group filters
        EnableGroupsWithoutVoteFilter = false;
        GroupsWithoutVote.Clear();
        SelectedGroupsWithoutVote.Clear();

        // Numeric filters
        MinReviewerCount = null;
        MaxReviewerCount = null;

        // Reset source tracking
        CurrentFilterSource = "Manual";
        CurrentFilterSourceName = string.Empty;
        WorkflowPreset = string.Empty;
        LastApplied = DateTime.Now;
    }

    /// <summary>
    /// Creates a deep copy of the filter criteria
    /// </summary>
    public FilterCriteria Clone()
    {
        return new FilterCriteria
        {
            CurrentUserId = CurrentUserId,
            UserDisplayName = UserDisplayName,
            MyPullRequestsOnly = MyPullRequestsOnly,
            AssignedToMeOnly = AssignedToMeOnly,
            NeedsMyReviewOnly = NeedsMyReviewOnly,
            ExcludeMyPullRequests = ExcludeMyPullRequests,
            SelectedStatuses = new List<string>(SelectedStatuses),
            IsDraft = IsDraft,
            GlobalSearchText = GlobalSearchText,
            TitleFilter = TitleFilter,
            CreatorFilter = CreatorFilter,
            ReviewerFilter = ReviewerFilter,
            SourceBranchFilter = SourceBranchFilter,
            TargetBranchFilter = TargetBranchFilter,
            SelectedReviewerVotes = new List<string>(SelectedReviewerVotes),
            CreatedAfter = CreatedAfter,
            CreatedBefore = CreatedBefore,
            UpdatedAfter = UpdatedAfter,
            UpdatedBefore = UpdatedBefore,
            EnableGroupsWithoutVoteFilter = EnableGroupsWithoutVoteFilter,
            GroupsWithoutVote = new List<string>(GroupsWithoutVote),
            SelectedGroupsWithoutVote = new List<string>(SelectedGroupsWithoutVote),
            MinReviewerCount = MinReviewerCount,
            MaxReviewerCount = MaxReviewerCount,
            FilterSource = FilterSource,
            CurrentFilterSource = CurrentFilterSource,
            CurrentFilterSourceName = CurrentFilterSourceName,
            WorkflowPreset = WorkflowPreset,
            LastApplied = LastApplied
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
