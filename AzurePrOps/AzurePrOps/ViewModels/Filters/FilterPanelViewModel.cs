using System;
using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using AzurePrOps.Models.FilteringAndSorting;
using AzurePrOps.Models;
using System.Collections.Generic;
using System.Linq;

namespace AzurePrOps.ViewModels.Filters;

/// <summary>
/// Dedicated ViewModel for the filter panel UI, handling all filter-related user interactions
/// This extracts filter UI logic from the main window ViewModel
/// </summary>
public class FilterPanelViewModel : ViewModelBase
{
    private readonly FilterState _filterState;
    private readonly FilterPresetManager _presetManager;
    private bool _isApplyingPreset = false;

    public FilterPanelViewModel(FilterState filterState, FilterPresetManager presetManager)
    {
        _filterState = filterState ?? throw new ArgumentNullException(nameof(filterState));
        _presetManager = presetManager ?? throw new ArgumentNullException(nameof(presetManager));
        
        InitializeCommands();
        InitializeCollections();
        SubscribeToFilterStateChanges();
    }

    #region Properties - Delegated to FilterState

    public bool ShowMyPullRequestsOnly
    {
        get => _filterState.MyPullRequestsOnly;
        set => _filterState.MyPullRequestsOnly = value;
    }

    public bool ShowAssignedToMeOnly
    {
        get => _filterState.AssignedToMeOnly;
        set => _filterState.AssignedToMeOnly = value;
    }

    public bool ShowNeedsMyReviewOnly
    {
        get => _filterState.NeedsMyReviewOnly;
        set => _filterState.NeedsMyReviewOnly = value;
    }

    public bool ExcludeMyPullRequests
    {
        get => _filterState.ExcludeMyPullRequests;
        set => _filterState.ExcludeMyPullRequests = value;
    }

    public string StatusFilter
    {
        get => _filterState.StatusFilter;
        set => _filterState.StatusFilter = value;
    }

    public string ReviewerVoteFilter
    {
        get => _filterState.ReviewerVoteFilter;
        set => _filterState.ReviewerVoteFilter = value;
    }

    public string DraftFilter
    {
        get => _filterState.DraftFilter;
        set => _filterState.DraftFilter = value;
    }

    public string TitleFilter
    {
        get => _filterState.TitleFilter;
        set => _filterState.TitleFilter = value;
    }

    public string CreatorFilter
    {
        get => _filterState.CreatorFilter;
        set => _filterState.CreatorFilter = value;
    }

    public string ReviewerFilter
    {
        get => _filterState.ReviewerFilter;
        set => _filterState.ReviewerFilter = value;
    }

    public string SourceBranchFilter
    {
        get => _filterState.SourceBranchFilter;
        set => _filterState.SourceBranchFilter = value;
    }

    public string TargetBranchFilter
    {
        get => _filterState.TargetBranchFilter;
        set => _filterState.TargetBranchFilter = value;
    }

    public string GlobalSearchText
    {
        get => _filterState.GlobalSearchText;
        set => _filterState.GlobalSearchText = value;
    }

    public bool EnableGroupFiltering
    {
        get => _filterState.EnableGroupFiltering;
        set => _filterState.EnableGroupFiltering = value;
    }

    public bool EnableGroupsWithoutVoteFilter
    {
        get => _filterState.EnableGroupsWithoutVoteFilter;
        set => _filterState.EnableGroupsWithoutVoteFilter = value;
    }

    private string _selectedDateRangePreset = "All Time";
    public string SelectedDateRangePreset
    {
        get => _selectedDateRangePreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDateRangePreset, value);
            ApplyDateRangePreset(value);
        }
    }

    #endregion

    #region Display Properties

    public string ActiveFiltersSummary => _filterState.ActiveFiltersSummary;
    public bool HasActiveFilters => _filterState.HasActiveFilters;
    public string FilterStatusText => _filterState.FilterStatusText;
    public string CurrentFilterSource => _filterState.CurrentFilterSource;

    #endregion

    #region Collections

    public ObservableCollection<string> AvailableStatuses { get; } = new()
        { "All", "Active", "Completed", "Abandoned" };

    public ObservableCollection<string> AvailableReviewerVotes { get; } = new()
        { "All", "No vote", "Approved", "Approved with suggestions", "Waiting for author", "Rejected" };

    public ObservableCollection<string> DraftFilterOptions { get; } = new()
        { "All", "Drafts Only", "Non-Drafts Only" };

    public ObservableCollection<string> DateRangePresets { get; } = new()
        { "All Time", "Last 7 days", "Last 14 days", "Last 30 days", "Last 60 days", "Last 90 days" };

    public ObservableCollection<string> AvailableCreators { get; } = new();
    public ObservableCollection<string> AvailableReviewers { get; } = new();
    public ObservableCollection<string> AvailableSourceBranches { get; } = new();
    public ObservableCollection<string> AvailableTargetBranches { get; } = new();
    public ObservableCollection<string> AvailableGroups { get; } = new();

    #endregion

    #region Commands

    public ReactiveCommand<Unit, Unit> ClearAllFiltersCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> ApplyPresetCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SaveCurrentFiltersCommand { get; private set; } = null!;

    #endregion

    #region Initialization

    private void InitializeCommands()
    {
        ClearAllFiltersCommand = ReactiveCommand.Create(ClearAllFilters);
        ApplyPresetCommand = ReactiveCommand.Create<string>(ApplyPreset);
        SaveCurrentFiltersCommand = ReactiveCommand.Create(SaveCurrentFilters);
    }

    private void InitializeCollections()
    {
        // Initialize collections - these will be populated by the main ViewModel
        // when data is loaded
    }

    private void SubscribeToFilterStateChanges()
    {
        // Subscribe to changes in FilterState to update UI properties
        _filterState.WhenAnyValue(x => x.ActiveFiltersSummary)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ActiveFiltersSummary)));

        _filterState.WhenAnyValue(x => x.HasActiveFilters)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(HasActiveFilters)));

        _filterState.WhenAnyValue(x => x.FilterStatusText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilterStatusText)));

        _filterState.WhenAnyValue(x => x.CurrentFilterSource)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CurrentFilterSource)));
    }

    #endregion

    #region Command Implementations

    private void ClearAllFilters()
    {
        _isApplyingPreset = true;
        try
        {
            _filterState.ResetToDefaults();
            SelectedDateRangePreset = "All Time";
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    private void ApplyPreset(string presetName)
    {
        if (string.IsNullOrEmpty(presetName)) return;

        _isApplyingPreset = true;
        try
        {
            _presetManager.ApplyPreset(presetName, _filterState, UserRole.General);
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    private void SaveCurrentFilters()
    {
        // This would typically open a dialog to save the current filter state
        // Implementation depends on the UI framework and requirements
    }

    #endregion

    #region Helper Methods

    private void ApplyDateRangePreset(string preset)
    {
        if (_isApplyingPreset) return;

        var now = DateTimeOffset.Now;
        switch (preset)
        {
            case "Last 7 days":
                _filterState.SetDateRange(now.AddDays(-7), null);
                break;
            case "Last 14 days":
                _filterState.SetDateRange(now.AddDays(-14), null);
                break;
            case "Last 30 days":
                _filterState.SetDateRange(now.AddDays(-30), null);
                break;
            case "Last 60 days":
                _filterState.SetDateRange(now.AddDays(-60), null);
                break;
            case "Last 90 days":
                _filterState.SetDateRange(now.AddDays(-90), null);
                break;
            case "All Time":
            default:
                _filterState.SetDateRange(null, null);
                break;
        }
    }

    public void UpdateAvailableOptions(IEnumerable<string> creators, IEnumerable<string> reviewers,
        IEnumerable<string> sourceBranches, IEnumerable<string> targetBranches, IEnumerable<string> groups)
    {
        UpdateCollection(AvailableCreators, creators);
        UpdateCollection(AvailableReviewers, reviewers);
        UpdateCollection(AvailableSourceBranches, sourceBranches);
        UpdateCollection(AvailableTargetBranches, targetBranches);
        UpdateCollection(AvailableGroups, groups);
    }

    private void UpdateCollection(ObservableCollection<string> collection, IEnumerable<string> newItems)
    {
        collection.Clear();
        if (newItems != null)
        {
            foreach (var item in newItems.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x))
            {
                collection.Add(item);
            }
        }
    }

    #endregion
}
