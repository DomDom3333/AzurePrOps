using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using ReactiveUI;

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

    #region Reviewer Vote Filters

    public List<string> SelectedReviewerVotes
    {
        get => _criteria.SelectedReviewerVotes;
        set
        {
            if (!_criteria.SelectedReviewerVotes.SequenceEqual(value))
            {
                _criteria.SelectedReviewerVotes = value.ToList();
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

    #endregion

    #region Group Filters

    public bool EnableGroupFiltering
    {
        get => _enableGroupFiltering;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _enableGroupFiltering, value))
            {
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
                this.RaiseAndSetIfChanged(ref _selectedGroups, value.ToList());
                UpdateSelectedGroupsText();
                OnFilterChanged();
            }
        }
    }

    private string _selectedGroupsText = "All Groups";
    public string SelectedGroupsText
    {
        get => _selectedGroupsText;
        private set => this.RaiseAndSetIfChanged(ref _selectedGroupsText, value);
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
                _criteria.SelectedGroupsWithoutVote = value.ToList();
                this.RaisePropertyChanged();
                UpdateSelectedGroupsWithoutVoteText();
                OnFilterChanged();
            }
        }
    }

    private string _selectedGroupsWithoutVoteText = "No Groups Selected";
    public string SelectedGroupsWithoutVoteText
    {
        get => _selectedGroupsWithoutVoteText;
        private set => this.RaiseAndSetIfChanged(ref _selectedGroupsWithoutVoteText, value);
    }

    #endregion

    #region Numeric Filters

    public int? MinReviewerCount
    {
        get => _criteria.MinReviewerCount;
        set
        {
            if (_criteria.MinReviewerCount != value)
            {
                _criteria.MinReviewerCount = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    public int? MaxReviewerCount
    {
        get => _criteria.MaxReviewerCount;
        set
        {
            if (_criteria.MaxReviewerCount != value)
            {
                _criteria.MaxReviewerCount = value;
                this.RaisePropertyChanged();
                OnFilterChanged();
            }
        }
    }

    #endregion

    #region Filter State Properties

    /// <summary>
    /// Gets a human-readable summary of currently active filters
    /// </summary>
    public string ActiveFiltersSummary => _criteria.ActiveFiltersSummary;

    /// <summary>
    /// Checks if any filters are currently active
    /// </summary>
    public bool HasActiveFilters => _criteria.HasActiveFilters || _enableGroupFiltering;

    /// <summary>
    /// Gets the current filter source display text
    /// </summary>
    public string CurrentFilterSource => _criteria.FilterSourceDisplay;

    /// <summary>
    /// Gets the filter status text for display
    /// </summary>
    public string FilterStatusText => HasActiveFilters ? 
        $"Filters Active ({ActiveFiltersSummary.Split('•').Length} applied)" : 
        "No Filters";

    #endregion

    #region Events

    /// <summary>
    /// Raised when any filter criteria changes
    /// </summary>
    public event EventHandler? FilterChanged;

    private void OnFilterChanged()
    {
        // Update display properties
        this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(FilterStatusText));
        
        // Raise the filter changed event
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region State Management

    /// <summary>
    /// Applies a saved filter view to this filter state
    /// This properly synchronizes all UI properties with the loaded criteria
    /// </summary>
    public void ApplyFilterView(SavedFilterView filterView)
    {
        if (filterView == null) return;

        // Temporarily disable change notifications to avoid multiple filter applications
        var originalCriteria = _criteria;
        _criteria = CloneFilterCriteria(filterView.FilterCriteria);
        _sortCriteria = CloneSortCriteria(filterView.SortCriteria);
        _enableGroupFiltering = filterView.EnableGroupFiltering;
        _selectedGroups = filterView.SelectedGroups.ToList();

        // Update all UI-bound properties
        UpdateSelectedGroupsText();
        UpdateSelectedGroupsWithoutVoteText();

        // Raise property changed for all properties to update UI
        RaiseAllPropertiesChanged();

        // Finally raise the filter changed event
        OnFilterChanged();
    }

    /// <summary>
    /// Resets all filters to their default state
    /// </summary>
    public void ClearAllFilters()
    {
        _criteria = new FilterCriteria
        {
            CurrentUserId = _criteria.CurrentUserId,
            UserDisplayName = _criteria.UserDisplayName
        };
        _sortCriteria = new SortCriteria();
        _enableGroupFiltering = false;
        _selectedGroups = new List<string>();

        UpdateSelectedGroupsText();
        UpdateSelectedGroupsWithoutVoteText();
        RaiseAllPropertiesChanged();
        OnFilterChanged();
    }

    /// <summary>
    /// Creates a SavedFilterView from the current state
    /// </summary>
    public SavedFilterView CreateSavedFilterView(string name, string description)
    {
        return new SavedFilterView
        {
            Name = name,
            Description = description,
            FilterCriteria = CloneFilterCriteria(_criteria),
            SortCriteria = CloneSortCriteria(_sortCriteria),
            EnableGroupFiltering = _enableGroupFiltering,
            SelectedGroups = _selectedGroups.ToList()
        };
    }

    #endregion

    #region Helper Methods

    private void UpdateSelectedGroupsText()
    {
        if (!_selectedGroups.Any())
        {
            SelectedGroupsText = "All Groups";
        }
        else if (_selectedGroups.Count == 1)
        {
            SelectedGroupsText = _selectedGroups[0];
        }
        else
        {
            SelectedGroupsText = $"{_selectedGroups.Count} Groups Selected";
        }
    }

    private void UpdateSelectedGroupsWithoutVoteText()
    {
        if (!_criteria.SelectedGroupsWithoutVote.Any())
        {
            SelectedGroupsWithoutVoteText = "No Groups Selected";
        }
        else if (_criteria.SelectedGroupsWithoutVote.Count == 1)
        {
            SelectedGroupsWithoutVoteText = _criteria.SelectedGroupsWithoutVote[0];
        }
        else
        {
            SelectedGroupsWithoutVoteText = $"{_criteria.SelectedGroupsWithoutVote.Count} Groups Selected";
        }
    }

    private void RaiseAllPropertiesChanged()
    {
        // Personal filters
        this.RaisePropertyChanged(nameof(MyPullRequestsOnly));
        this.RaisePropertyChanged(nameof(AssignedToMeOnly));
        this.RaisePropertyChanged(nameof(NeedsMyReviewOnly));
        this.RaisePropertyChanged(nameof(ExcludeMyPullRequests));

        // Status filters
        this.RaisePropertyChanged(nameof(SelectedStatuses));
        this.RaisePropertyChanged(nameof(IsDraft));
        this.RaisePropertyChanged(nameof(DraftFilter));

        // Text filters
        this.RaisePropertyChanged(nameof(GlobalSearchText));
        this.RaisePropertyChanged(nameof(TitleFilter));
        this.RaisePropertyChanged(nameof(CreatorFilter));
        this.RaisePropertyChanged(nameof(ReviewerFilter));
        this.RaisePropertyChanged(nameof(SourceBranchFilter));
        this.RaisePropertyChanged(nameof(TargetBranchFilter));

        // Reviewer vote filters
        this.RaisePropertyChanged(nameof(SelectedReviewerVotes));

        // Date filters
        this.RaisePropertyChanged(nameof(CreatedAfter));
        this.RaisePropertyChanged(nameof(CreatedBefore));
        this.RaisePropertyChanged(nameof(UpdatedAfter));
        this.RaisePropertyChanged(nameof(UpdatedBefore));

        // Group filters
        this.RaisePropertyChanged(nameof(EnableGroupFiltering));
        this.RaisePropertyChanged(nameof(SelectedGroups));
        this.RaisePropertyChanged(nameof(EnableGroupsWithoutVoteFilter));
        this.RaisePropertyChanged(nameof(SelectedGroupsWithoutVote));

        // Numeric filters
        this.RaisePropertyChanged(nameof(MinReviewerCount));
        this.RaisePropertyChanged(nameof(MaxReviewerCount));

        // State properties
        this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(CurrentFilterSource));
        this.RaisePropertyChanged(nameof(FilterStatusText));
    }

    private static FilterCriteria CloneFilterCriteria(FilterCriteria source)
    {
        return new FilterCriteria
        {
            CurrentUserId = source.CurrentUserId,
            UserDisplayName = source.UserDisplayName,
            MyPullRequestsOnly = source.MyPullRequestsOnly,
            AssignedToMeOnly = source.AssignedToMeOnly,
            NeedsMyReviewOnly = source.NeedsMyReviewOnly,
            ExcludeMyPullRequests = source.ExcludeMyPullRequests,
            SelectedStatuses = source.SelectedStatuses.ToList(),
            IsDraft = source.IsDraft,
            GlobalSearchText = source.GlobalSearchText,
            TitleFilter = source.TitleFilter,
            CreatorFilter = source.CreatorFilter,
            ReviewerFilter = source.ReviewerFilter,
            SourceBranchFilter = source.SourceBranchFilter,
            TargetBranchFilter = source.TargetBranchFilter,
            SelectedReviewerVotes = source.SelectedReviewerVotes.ToList(),
            CreatedAfter = source.CreatedAfter,
            CreatedBefore = source.CreatedBefore,
            UpdatedAfter = source.UpdatedAfter,
            UpdatedBefore = source.UpdatedBefore,
            EnableGroupsWithoutVoteFilter = source.EnableGroupsWithoutVoteFilter,
            GroupsWithoutVote = source.GroupsWithoutVote.ToList(),
            SelectedGroupsWithoutVote = source.SelectedGroupsWithoutVote.ToList(),
            MinReviewerCount = source.MinReviewerCount,
            MaxReviewerCount = source.MaxReviewerCount,
            WorkflowPreset = source.WorkflowPreset,
            CurrentFilterSource = source.CurrentFilterSource,
            CurrentFilterSourceName = source.CurrentFilterSourceName,
            LastApplied = source.LastApplied
        };
    }

    private static SortCriteria CloneSortCriteria(SortCriteria source)
    {
        return new SortCriteria
        {
            PrimaryField = source.PrimaryField,
            PrimaryDirection = source.PrimaryDirection,
            SecondaryField = source.SecondaryField,
            SecondaryDirection = source.SecondaryDirection,
            TertiaryField = source.TertiaryField,
            TertiaryDirection = source.TertiaryDirection,
            CurrentPreset = source.CurrentPreset
        };
    }

    #endregion
}
