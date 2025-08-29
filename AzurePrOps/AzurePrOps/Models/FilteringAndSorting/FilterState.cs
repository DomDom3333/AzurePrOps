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

    #region Status and Vote Filters

    private string _statusFilter = "All";
    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            var safeValue = value ?? "All";
            if (_statusFilter != safeValue)
            {
                this.RaiseAndSetIfChanged(ref _statusFilter, safeValue);
                UpdateStatusFilter(safeValue);
                OnFilterChanged();
            }
        }
    }

    private string _reviewerVoteFilter = "All";
    public string ReviewerVoteFilter
    {
        get => _reviewerVoteFilter;
        set
        {
            var safeValue = value ?? "All";
            if (_reviewerVoteFilter != safeValue)
            {
                this.RaiseAndSetIfChanged(ref _reviewerVoteFilter, safeValue);
                UpdateReviewerVoteFilter(safeValue);
                OnFilterChanged();
            }
        }
    }

    #endregion

    #region Group Filters

    public bool EnableGroupFiltering
    {
        get => _enableGroupFiltering;
        set
        {
            if (_enableGroupFiltering != value)
            {
                this.RaiseAndSetIfChanged(ref _enableGroupFiltering, value);
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

    public List<string> SelectedGroups
    {
        get => _selectedGroups;
        set
        {
            if (!_selectedGroups.SequenceEqual(value))
            {
                _selectedGroups = value.ToList();
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    #endregion

    #region Display Properties

    public string CurrentFilterSource => _criteria.FilterSource ?? "Manual";

    public string ActiveFiltersSummary
    {
        get
        {
            var filters = new List<string>();
            
            if (MyPullRequestsOnly) filters.Add("My PRs");
            if (AssignedToMeOnly) filters.Add("Assigned to Me");
            if (NeedsMyReviewOnly) filters.Add("Needs My Review");
            if (ExcludeMyPullRequests) filters.Add("Exclude My PRs");
            if (StatusFilter != "All") filters.Add($"Status: {StatusFilter}");
            if (ReviewerVoteFilter != "All") filters.Add($"Vote: {ReviewerVoteFilter}");
            if (DraftFilter != "All") filters.Add($"Draft: {DraftFilter}");
            if (!string.IsNullOrWhiteSpace(TitleFilter)) filters.Add($"Title: {TitleFilter}");
            if (!string.IsNullOrWhiteSpace(CreatorFilter)) filters.Add($"Creator: {CreatorFilter}");
            if (!string.IsNullOrWhiteSpace(ReviewerFilter)) filters.Add($"Reviewer: {ReviewerFilter}");
            if (!string.IsNullOrWhiteSpace(SourceBranchFilter)) filters.Add($"Source: {SourceBranchFilter}");
            if (!string.IsNullOrWhiteSpace(TargetBranchFilter)) filters.Add($"Target: {TargetBranchFilter}");
            if (!string.IsNullOrWhiteSpace(GlobalSearchText)) filters.Add($"Search: {GlobalSearchText}");
            if (EnableGroupFiltering) filters.Add("Group Filtering");
            if (EnableGroupsWithoutVoteFilter) filters.Add("Groups Without Vote");
            
            return filters.Count > 0 ? string.Join(", ", filters) : "No active filters";
        }
    }

    public bool HasActiveFilters
    {
        get
        {
            return MyPullRequestsOnly || AssignedToMeOnly || NeedsMyReviewOnly || ExcludeMyPullRequests ||
                   StatusFilter != "All" || ReviewerVoteFilter != "All" || DraftFilter != "All" ||
                   !string.IsNullOrWhiteSpace(TitleFilter) || !string.IsNullOrWhiteSpace(CreatorFilter) ||
                   !string.IsNullOrWhiteSpace(ReviewerFilter) || !string.IsNullOrWhiteSpace(SourceBranchFilter) ||
                   !string.IsNullOrWhiteSpace(TargetBranchFilter) || !string.IsNullOrWhiteSpace(GlobalSearchText) ||
                   EnableGroupFiltering || EnableGroupsWithoutVoteFilter;
        }
    }

    public string FilterStatusText
    {
        get
        {
            var activeCount = GetActiveFilterCount();
            if (activeCount == 0) return "No filters applied";
            return $"{activeCount} filter{(activeCount == 1 ? "" : "s")} applied";
        }
    }

    #endregion

    #region Events

    public event EventHandler? FilterChanged;

    #endregion

    #region Public Methods

    public void ResetToDefaults()
    {
        _criteria = new FilterCriteria
        {
            CurrentUserId = _criteria.CurrentUserId,
            UserDisplayName = _criteria.UserDisplayName
        };
        
        _statusFilter = "All";
        _reviewerVoteFilter = "All";
        _enableGroupFiltering = false;
        _selectedGroups.Clear();

        // Raise all property changed notifications
        this.RaisePropertyChanged(nameof(MyPullRequestsOnly));
        this.RaisePropertyChanged(nameof(AssignedToMeOnly));
        this.RaisePropertyChanged(nameof(NeedsMyReviewOnly));
        this.RaisePropertyChanged(nameof(ExcludeMyPullRequests));
        this.RaisePropertyChanged(nameof(StatusFilter));
        this.RaisePropertyChanged(nameof(ReviewerVoteFilter));
        this.RaisePropertyChanged(nameof(DraftFilter));
        this.RaisePropertyChanged(nameof(TitleFilter));
        this.RaisePropertyChanged(nameof(CreatorFilter));
        this.RaisePropertyChanged(nameof(ReviewerFilter));
        this.RaisePropertyChanged(nameof(SourceBranchFilter));
        this.RaisePropertyChanged(nameof(TargetBranchFilter));
        this.RaisePropertyChanged(nameof(GlobalSearchText));
        this.RaisePropertyChanged(nameof(EnableGroupFiltering));
        this.RaisePropertyChanged(nameof(EnableGroupsWithoutVoteFilter));
        
        OnFilterChanged();
    }

    public void SetDateRange(DateTimeOffset? createdAfter, DateTimeOffset? createdBefore, 
        DateTimeOffset? updatedAfter = null, DateTimeOffset? updatedBefore = null)
    {
        var changed = false;
        
        if (_criteria.CreatedAfter != createdAfter)
        {
            _criteria.CreatedAfter = createdAfter;
            changed = true;
        }
        
        if (_criteria.CreatedBefore != createdBefore)
        {
            _criteria.CreatedBefore = createdBefore;
            changed = true;
        }
        
        if (_criteria.UpdatedAfter != updatedAfter)
        {
            _criteria.UpdatedAfter = updatedAfter;
            changed = true;
        }
        
        if (_criteria.UpdatedBefore != updatedBefore)
        {
            _criteria.UpdatedBefore = updatedBefore;
            changed = true;
        }
        
        if (changed)
        {
            OnFilterChanged();
        }
    }

    public void SetUserInfo(string userId, string displayName)
    {
        _criteria.CurrentUserId = userId ?? string.Empty;
        _criteria.UserDisplayName = displayName ?? string.Empty;
    }

    /// <summary>
    /// Alias for ResetToDefaults for backward compatibility
    /// </summary>
    public void ClearAllFilters()
    {
        ResetToDefaults();
    }

    /// <summary>
    /// Creates a saved filter view from the current filter state
    /// </summary>
    public FilterView CreateSavedFilterView(string name, string description = "")
    {
        return new FilterView
        {
            Name = name,
            Description = description,
            FilterCriteria = new FilterCriteria
            {
                MyPullRequestsOnly = _criteria.MyPullRequestsOnly,
                AssignedToMeOnly = _criteria.AssignedToMeOnly,
                NeedsMyReviewOnly = _criteria.NeedsMyReviewOnly,
                ExcludeMyPullRequests = _criteria.ExcludeMyPullRequests,
                SelectedStatuses = _criteria.SelectedStatuses.ToList(),
                SelectedReviewerVotes = _criteria.SelectedReviewerVotes.ToList(),
                IsDraft = _criteria.IsDraft,
                TitleFilter = _criteria.TitleFilter,
                CreatorFilter = _criteria.CreatorFilter,
                ReviewerFilter = _criteria.ReviewerFilter,
                SourceBranchFilter = _criteria.SourceBranchFilter,
                TargetBranchFilter = _criteria.TargetBranchFilter,
                GlobalSearchText = _criteria.GlobalSearchText,
                CreatedAfter = _criteria.CreatedAfter,
                CreatedBefore = _criteria.CreatedBefore,
                UpdatedAfter = _criteria.UpdatedAfter,
                UpdatedBefore = _criteria.UpdatedBefore,
                EnableGroupsWithoutVoteFilter = _criteria.EnableGroupsWithoutVoteFilter,
                CurrentUserId = _criteria.CurrentUserId,
                UserDisplayName = _criteria.UserDisplayName
            }
        };
    }

    /// <summary>
    /// Applies a saved filter view to the current filter state
    /// </summary>
    public void ApplyFilterView(FilterView filterView)
    {
        if (filterView?.FilterCriteria == null) return;

        var criteria = filterView.FilterCriteria;
        
        // Store current user info
        var currentUserId = _criteria.CurrentUserId;
        var currentUserDisplayName = _criteria.UserDisplayName;
        
        // Apply the filter criteria
        _criteria = new FilterCriteria
        {
            MyPullRequestsOnly = criteria.MyPullRequestsOnly,
            AssignedToMeOnly = criteria.AssignedToMeOnly,
            NeedsMyReviewOnly = criteria.NeedsMyReviewOnly,
            ExcludeMyPullRequests = criteria.ExcludeMyPullRequests,
            SelectedStatuses = criteria.SelectedStatuses?.ToList() ?? new List<string>(),
            SelectedReviewerVotes = criteria.SelectedReviewerVotes?.ToList() ?? new List<string>(),
            IsDraft = criteria.IsDraft,
            TitleFilter = criteria.TitleFilter ?? string.Empty,
            CreatorFilter = criteria.CreatorFilter ?? string.Empty,
            ReviewerFilter = criteria.ReviewerFilter ?? string.Empty,
            SourceBranchFilter = criteria.SourceBranchFilter ?? string.Empty,
            TargetBranchFilter = criteria.TargetBranchFilter ?? string.Empty,
            GlobalSearchText = criteria.GlobalSearchText ?? string.Empty,
            CreatedAfter = criteria.CreatedAfter,
            CreatedBefore = criteria.CreatedBefore,
            UpdatedAfter = criteria.UpdatedAfter,
            UpdatedBefore = criteria.UpdatedBefore,
            EnableGroupsWithoutVoteFilter = criteria.EnableGroupsWithoutVoteFilter,
            CurrentUserId = currentUserId,
            UserDisplayName = currentUserDisplayName
        };

        // Update UI filter strings
        _statusFilter = criteria.SelectedStatuses?.FirstOrDefault() ?? "All";
        _reviewerVoteFilter = criteria.SelectedReviewerVotes?.FirstOrDefault() ?? "All";
        _enableGroupFiltering = false; // Reset group filtering
        _selectedGroups.Clear();

        // Set filter source
        _criteria.SetFilterSource("Saved Filter", filterView.Name);

        // Raise all property changed notifications
        this.RaisePropertyChanged(nameof(MyPullRequestsOnly));
        this.RaisePropertyChanged(nameof(AssignedToMeOnly));
        this.RaisePropertyChanged(nameof(NeedsMyReviewOnly));
        this.RaisePropertyChanged(nameof(ExcludeMyPullRequests));
        this.RaisePropertyChanged(nameof(StatusFilter));
        this.RaisePropertyChanged(nameof(ReviewerVoteFilter));
        this.RaisePropertyChanged(nameof(DraftFilter));
        this.RaisePropertyChanged(nameof(TitleFilter));
        this.RaisePropertyChanged(nameof(CreatorFilter));
        this.RaisePropertyChanged(nameof(ReviewerFilter));
        this.RaisePropertyChanged(nameof(SourceBranchFilter));
        this.RaisePropertyChanged(nameof(TargetBranchFilter));
        this.RaisePropertyChanged(nameof(GlobalSearchText));
        this.RaisePropertyChanged(nameof(EnableGroupFiltering));
        this.RaisePropertyChanged(nameof(EnableGroupsWithoutVoteFilter));
        
        OnFilterChanged();
    }

    /// <summary>
    /// Sets the UpdatedAfter filter date
    /// </summary>
    public void UpdatedAfter(DateTimeOffset? date)
    {
        if (_criteria.UpdatedAfter != date)
        {
            _criteria.UpdatedAfter = date;
            OnFilterChanged();
        }
    }

    #endregion

    #region Private Methods

    private void UpdateStatusFilter(string value)
    {
        _criteria.SelectedStatuses.Clear();
        if (value != "All")
        {
            _criteria.SelectedStatuses.Add(value);
        }
    }

    private void UpdateReviewerVoteFilter(string value)
    {
        _criteria.SelectedReviewerVotes.Clear();
        if (value != "All")
        {
            _criteria.SelectedReviewerVotes.Add(value);
        }
    }

    private int GetActiveFilterCount()
    {
        var count = 0;
        
        if (MyPullRequestsOnly) count++;
        if (AssignedToMeOnly) count++;
        if (NeedsMyReviewOnly) count++;
        if (ExcludeMyPullRequests) count++;
        if (StatusFilter != "All") count++;
        if (ReviewerVoteFilter != "All") count++;
        if (DraftFilter != "All") count++;
        if (!string.IsNullOrWhiteSpace(TitleFilter)) count++;
        if (!string.IsNullOrWhiteSpace(CreatorFilter)) count++;
        if (!string.IsNullOrWhiteSpace(ReviewerFilter)) count++;
        if (!string.IsNullOrWhiteSpace(SourceBranchFilter)) count++;
        if (!string.IsNullOrWhiteSpace(TargetBranchFilter)) count++;
        if (!string.IsNullOrWhiteSpace(GlobalSearchText)) count++;
        if (EnableGroupFiltering) count++;
        if (EnableGroupsWithoutVoteFilter) count++;
        
        return count;
    }

    private void OnFilterChanged()
    {
        this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(FilterStatusText));
        this.RaisePropertyChanged(nameof(CurrentFilterSource));
        
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
