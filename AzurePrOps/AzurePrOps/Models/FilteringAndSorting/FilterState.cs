using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using ReactiveUI;
using AzurePrOps.Models;

namespace AzurePrOps.Models.FilteringAndSorting;

/// <summary>
/// Centralized filter state manager that acts as the single source of truth for all filtering
/// This replaces the scattered filter properties in the ViewModel and provides proper UI synchronization
/// </summary>
public class FilterState : ReactiveObject
{
    private FilterCriteria _criteria = new();
    private SortCriteria _sortCriteria = new();
    private bool _enableGroupFiltering = false;
    private List<string> _selectedGroups = new();

    /// <summary>
    /// The underlying filter criteria - this is what gets passed to the filtering service
    /// </summary>
    public FilterCriteria Criteria
    {
        get => _criteria;
        private set => this.RaiseAndSetIfChanged(ref _criteria, value);
    }

    /// <summary>
    /// The underlying sort criteria
    /// </summary>
    public SortCriteria SortCriteria
    {
        get => _sortCriteria;
        private set => this.RaiseAndSetIfChanged(ref _sortCriteria, value);
    }

    #region Personal Filters
    
    public bool MyPullRequestsOnly
    {
        get => _criteria.MyPullRequestsOnly;
        set
        {
            if (_criteria.MyPullRequestsOnly != value)
            {
                _criteria.MyPullRequestsOnly = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public bool AssignedToMeOnly
    {
        get => _criteria.AssignedToMeOnly;
        set
        {
            if (_criteria.AssignedToMeOnly != value)
            {
                _criteria.AssignedToMeOnly = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public bool NeedsMyReviewOnly
    {
        get => _criteria.NeedsMyReviewOnly;
        set
        {
            if (_criteria.NeedsMyReviewOnly != value)
            {
                _criteria.NeedsMyReviewOnly = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public bool ExcludeMyPullRequests
    {
        get => _criteria.ExcludeMyPullRequests;
        set
        {
            if (_criteria.ExcludeMyPullRequests != value)
            {
                _criteria.ExcludeMyPullRequests = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    #endregion

    #region Status Filters

    public List<string> SelectedStatuses
    {
        get => _criteria.SelectedStatuses;
        set
        {
            if (!_criteria.SelectedStatuses.SequenceEqual(value))
            {
                _criteria.SelectedStatuses = value.ToList();
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public bool? IsDraft
    {
        get => _criteria.IsDraft;
        set
        {
            if (_criteria.IsDraft != value)
            {
                _criteria.IsDraft = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(DraftFilter));
                OnFilterChanged();
            }
        }
    }

    /// <summary>
    /// String representation of draft filter for UI binding
    /// </summary>
    public string DraftFilter
    {
        get => _criteria.IsDraft switch
        {
            true => "Drafts Only",
            false => "Non-Drafts Only",
            null => "All"
        };
        set
        {
            var newValue = value switch
            {
                "Drafts Only" => true,
                "Non-Drafts Only" => false,
                _ => (bool?)null
            };
            IsDraft = newValue;
        }
    }

    #endregion

    #region Text Filters

    public string GlobalSearchText
    {
        get => _criteria.GlobalSearchText;
        set
        {
            if (_criteria.GlobalSearchText != value)
            {
                _criteria.GlobalSearchText = value ?? string.Empty;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public string TitleFilter
    {
        get => _criteria.TitleFilter;
        set
        {
            if (_criteria.TitleFilter != value)
            {
                _criteria.TitleFilter = value ?? string.Empty;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public string CreatorFilter
    {
        get => _criteria.CreatorFilter;
        set
        {
            if (_criteria.CreatorFilter != value)
            {
                _criteria.CreatorFilter = value ?? string.Empty;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public string ReviewerFilter
    {
        get => _criteria.ReviewerFilter;
        set
        {
            if (_criteria.ReviewerFilter != value)
            {
                _criteria.ReviewerFilter = value ?? string.Empty;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public string SourceBranchFilter
    {
        get => _criteria.SourceBranchFilter;
        set
        {
            if (_criteria.SourceBranchFilter != value)
            {
                _criteria.SourceBranchFilter = value ?? string.Empty;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public string TargetBranchFilter
    {
        get => _criteria.TargetBranchFilter;
        set
        {
            if (_criteria.TargetBranchFilter != value)
            {
                _criteria.TargetBranchFilter = value ?? string.Empty;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    #endregion

    #region Date Filters

    public DateTimeOffset? CreatedAfter
    {
        get => _criteria.CreatedAfter;
        set
        {
            if (_criteria.CreatedAfter != value)
            {
                _criteria.CreatedAfter = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public DateTimeOffset? CreatedBefore
    {
        get => _criteria.CreatedBefore;
        set
        {
            if (_criteria.CreatedBefore != value)
            {
                _criteria.CreatedBefore = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    #endregion

    #region Group Filtering

    public bool EnableGroupFiltering
    {
        get => _enableGroupFiltering;
        set
        {
            this.RaiseAndSetIfChanged(ref _enableGroupFiltering, value);
            OnFilterChanged();
        }
    }

    public List<string> SelectedGroups
    {
        get => _selectedGroups;
        set
        {
            if (!_selectedGroups.SequenceEqual(value))
            {
                _selectedGroups = value?.ToList() ?? new List<string>();
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public bool EnableGroupsWithoutVoteFilter
    {
        get => _criteria.EnableGroupsWithoutVoteFilter;
        set
        {
            if (_criteria.EnableGroupsWithoutVoteFilter != value)
            {
                _criteria.EnableGroupsWithoutVoteFilter = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public List<string> SelectedGroupsWithoutVote
    {
        get => _criteria.SelectedGroupsWithoutVote;
        set
        {
            if (!_criteria.SelectedGroupsWithoutVote.SequenceEqual(value))
            {
                _criteria.SelectedGroupsWithoutVote = value?.ToList() ?? new List<string>();
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    #endregion

    #region Reviewer Vote Filters

    public List<string> SelectedReviewerVotes
    {
        get => _criteria.SelectedReviewerVotes;
        set
        {
            if (!_criteria.SelectedReviewerVotes.SequenceEqual(value))
            {
                _criteria.SelectedReviewerVotes = value?.ToList() ?? new List<string>();
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    /// <summary>
    /// String representation of reviewer vote filter for UI binding
    /// </summary>
    public string ReviewerVoteFilter
    {
        get => SelectedReviewerVotes.Any() ? string.Join(", ", SelectedReviewerVotes) : "All";
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == "All")
            {
                SelectedReviewerVotes = new List<string>();
            }
            else
            {
                SelectedReviewerVotes = value.Split(',').Select(s => s.Trim()).ToList();
            }
        }
    }

    /// <summary>
    /// String representation of status filter for UI binding
    /// </summary>
    public string StatusFilter
    {
        get => SelectedStatuses.Any() ? string.Join(", ", SelectedStatuses) : "All";
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == "All")
            {
                SelectedStatuses = new List<string>();
            }
            else
            {
                SelectedStatuses = value.Split(',').Select(s => s.Trim()).ToList();
            }
        }
    }

    #endregion

    #region Date Range Methods

    public DateTimeOffset? UpdatedAfter
    {
        get => _criteria.UpdatedAfter;
        set
        {
            if (_criteria.UpdatedAfter != value)
            {
                _criteria.UpdatedAfter = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public DateTimeOffset? UpdatedBefore
    {
        get => _criteria.UpdatedBefore;
        set
        {
            if (_criteria.UpdatedBefore != value)
            {
                _criteria.UpdatedBefore = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    /// <summary>
    /// Sets a date range filter for created dates
    /// </summary>
    public void SetDateRange(DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        CreatedAfter = startDate;
        CreatedBefore = endDate;
    }

    /// <summary>
    /// Sets a date range filter for updated dates
    /// </summary>
    public void SetUpdatedDateRange(DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        UpdatedAfter = startDate;
        UpdatedBefore = endDate;
    }

    #endregion

    #region Display Properties

    /// <summary>
    /// Summary of currently active filters for display
    /// </summary>
    public string ActiveFiltersSummary
    {
        get
        {
            var activeFilters = new List<string>();

            if (MyPullRequestsOnly) activeFilters.Add("My PRs");
            if (AssignedToMeOnly) activeFilters.Add("Assigned to Me");
            if (NeedsMyReviewOnly) activeFilters.Add("Needs My Review");
            if (ExcludeMyPullRequests) activeFilters.Add("Exclude My PRs");

            if (!string.IsNullOrWhiteSpace(GlobalSearchText)) activeFilters.Add($"Search: {GlobalSearchText}");
            if (!string.IsNullOrWhiteSpace(TitleFilter)) activeFilters.Add($"Title: {TitleFilter}");
            if (!string.IsNullOrWhiteSpace(CreatorFilter)) activeFilters.Add($"Creator: {CreatorFilter}");
            if (!string.IsNullOrWhiteSpace(ReviewerFilter)) activeFilters.Add($"Reviewer: {ReviewerFilter}");
            if (!string.IsNullOrWhiteSpace(SourceBranchFilter)) activeFilters.Add($"Source: {SourceBranchFilter}");
            if (!string.IsNullOrWhiteSpace(TargetBranchFilter)) activeFilters.Add($"Target: {TargetBranchFilter}");

            if (SelectedStatuses.Any()) activeFilters.Add($"Status: {string.Join(", ", SelectedStatuses)}");
            if (_criteria.SelectedReviewerVotes.Any()) activeFilters.Add($"Vote: {string.Join(", ", _criteria.SelectedReviewerVotes)}");

            if (IsDraft != null) activeFilters.Add(DraftFilter);

            if (CreatedAfter.HasValue) activeFilters.Add($"After: {CreatedAfter.Value:yyyy-MM-dd}");
            if (CreatedBefore.HasValue) activeFilters.Add($"Before: {CreatedBefore.Value:yyyy-MM-dd}");

            if (EnableGroupFiltering && SelectedGroups.Any()) activeFilters.Add($"Groups: {string.Join(", ", SelectedGroups)}");
            if (EnableGroupsWithoutVoteFilter && SelectedGroupsWithoutVote.Any()) activeFilters.Add($"Groups w/o Vote: {string.Join(", ", SelectedGroupsWithoutVote)}");

            return activeFilters.Any() ? string.Join(" | ", activeFilters) : "No active filters";
        }
    }

    /// <summary>
    /// Check if any filters are currently active
    /// </summary>
    public bool HasActiveFilters
    {
        get
        {
            return MyPullRequestsOnly ||
                   AssignedToMeOnly ||
                   NeedsMyReviewOnly ||
                   ExcludeMyPullRequests ||
                   !string.IsNullOrWhiteSpace(GlobalSearchText) ||
                   !string.IsNullOrWhiteSpace(TitleFilter) ||
                   !string.IsNullOrWhiteSpace(CreatorFilter) ||
                   !string.IsNullOrWhiteSpace(ReviewerFilter) ||
                   !string.IsNullOrWhiteSpace(SourceBranchFilter) ||
                   !string.IsNullOrWhiteSpace(TargetBranchFilter) ||
                   SelectedStatuses.Any() ||
                   _criteria.SelectedReviewerVotes.Any() ||
                   IsDraft != null ||
                   CreatedAfter.HasValue ||
                   CreatedBefore.HasValue ||
                   (EnableGroupFiltering && SelectedGroups.Any()) ||
                   (EnableGroupsWithoutVoteFilter && SelectedGroupsWithoutVote.Any());
        }
    }

    /// <summary>
    /// Current filter source for tracking where filters are applied from
    /// </summary>
    public string CurrentFilterSource => _criteria.CurrentFilterSource ?? "Manual";

    /// <summary>
    /// Filter status text for UI display
    /// </summary>
    public string FilterStatusText => HasActiveFilters ? $"Filtered ({GetActiveFilterCount()} active)" : "All items shown";

    #endregion

    #region Methods

    /// <summary>
    /// Set user information for personal filters
    /// </summary>
    public void SetUserInfo(string userId, string userDisplayName)
    {
        _criteria.CurrentUserId = userId;
        _criteria.UserDisplayName = userDisplayName;
        this.RaisePropertyChanged(nameof(CurrentFilterSource));
    }

    /// <summary>
    /// Reset all filters to default values
    /// </summary>
    public void ResetToDefaults()
    {
        ClearAllFilters();
    }

    /// <summary>
    /// Clear all active filters
    /// </summary>
    public void ClearAllFilters()
    {
        MyPullRequestsOnly = false;
        AssignedToMeOnly = false;
        NeedsMyReviewOnly = false;
        ExcludeMyPullRequests = false;
        
        GlobalSearchText = string.Empty;
        TitleFilter = string.Empty;
        CreatorFilter = string.Empty;
        ReviewerFilter = string.Empty;
        SourceBranchFilter = string.Empty;
        TargetBranchFilter = string.Empty;
        
        // Clear dropdown selections - they will default to "All" through their property getters
        SelectedStatuses = new List<string>();
        SelectedReviewerVotes = new List<string>();
        IsDraft = null; // This defaults to "All" through DraftFilter property
        
        CreatedAfter = null;
        CreatedBefore = null;
        UpdatedAfter = null;
        UpdatedBefore = null;
        
        EnableGroupFiltering = false;
        SelectedGroups = new List<string>();
        EnableGroupsWithoutVoteFilter = false;
        SelectedGroupsWithoutVote = new List<string>();
        
        _criteria.CurrentFilterSource = "Manual";
        this.RaisePropertyChanged(nameof(CurrentFilterSource));
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
        this.RaisePropertyChanged(nameof(FilterStatusText));
        
        // Notify that dropdown filter properties have changed to update UI
        this.RaisePropertyChanged(nameof(StatusFilter));
        this.RaisePropertyChanged(nameof(ReviewerVoteFilter));
        this.RaisePropertyChanged(nameof(DraftFilter));
    }

    /// <summary>
    /// Apply a filter view configuration
    /// </summary>
    public void ApplyFilterView(FilterView filterView)
    {
        if (filterView == null) return;

        // Apply the filter view settings to the current state
        // Note: FilterView may not have all these properties, so we need to check what's available
        // This is a placeholder implementation - you may need to adjust based on actual FilterView structure
        _criteria.CurrentFilterSource = filterView.Name ?? "FilterView";
        this.RaisePropertyChanged(nameof(CurrentFilterSource));
    }

    /// <summary>
    /// Get count of active filters
    /// </summary>
    public int GetActiveFilterCount()
    {
        int count = 0;

        if (MyPullRequestsOnly) count++;
        if (AssignedToMeOnly) count++;
        if (NeedsMyReviewOnly) count++;
        if (ExcludeMyPullRequests) count++;
        if (!string.IsNullOrWhiteSpace(GlobalSearchText)) count++;
        if (!string.IsNullOrWhiteSpace(TitleFilter)) count++;
        if (!string.IsNullOrWhiteSpace(CreatorFilter)) count++;
        if (!string.IsNullOrWhiteSpace(ReviewerFilter)) count++;
        if (!string.IsNullOrWhiteSpace(SourceBranchFilter)) count++;
        if (!string.IsNullOrWhiteSpace(TargetBranchFilter)) count++;
        if (SelectedStatuses.Any()) count++;
        if (SelectedReviewerVotes.Any()) count++;
        if (IsDraft != null) count++;
        if (CreatedAfter.HasValue) count++;
        if (CreatedBefore.HasValue) count++;
        if (UpdatedAfter.HasValue) count++;
        if (UpdatedBefore.HasValue) count++;
        if (EnableGroupFiltering && SelectedGroups.Any()) count++;
        if (EnableGroupsWithoutVoteFilter && SelectedGroupsWithoutVote.Any()) count++;

        return count;
    }

    /// <summary>
    /// Raise the FilterChanged event
    /// </summary>
    private void OnFilterChanged()
    {
        FilterChanged?.Invoke();
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
        this.RaisePropertyChanged(nameof(FilterStatusText));
    }

    /// <summary>
    /// Event raised when any filter property changes
    /// </summary>
    public event Action? FilterChanged;

    #endregion
}
