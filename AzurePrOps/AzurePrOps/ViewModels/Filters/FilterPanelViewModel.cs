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
    private bool _isApplyingSavedView;

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

    public string GlobalSearchText
    {
        get => _filterState.GlobalSearchText;
        set => _filterState.GlobalSearchText = value;
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

    public DateTimeOffset? CreatedAfter
    {
        get => _filterState.CreatedAfter;
        set => _filterState.CreatedAfter = value;
    }

    public DateTimeOffset? CreatedBefore
    {
        get => _filterState.CreatedBefore;
        set => _filterState.CreatedBefore = value;
    }

    public string DraftFilter
    {
        get => _filterState.DraftFilter;
        set => _filterState.DraftFilter = value;
    }

    public bool EnableGroupsWithoutVoteFilter
    {
        get => _filterState.EnableGroupsWithoutVoteFilter;
        set => _filterState.EnableGroupsWithoutVoteFilter = value;
    }

    #endregion

    #region Available Options Collections

    public ObservableCollection<string> AvailableStatuses { get; } = new();
    public ObservableCollection<string> AvailableReviewerVotes { get; } = new();
    public ObservableCollection<string> DraftFilterOptions { get; } = new();
    public ObservableCollection<string> AvailableCreators { get; } = new();
    public ObservableCollection<string> AvailableReviewers { get; } = new();
    public ObservableCollection<string> AvailableSourceBranches { get; } = new();
    public ObservableCollection<string> AvailableTargetBranches { get; } = new();
    public ObservableCollection<string> AvailableGroups { get; } = new();

    #endregion

    #region Commands

    public ReactiveCommand<Unit, Unit> ClearAllFiltersCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ResetFiltersCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> ApplyPresetCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SaveCurrentFiltersCommand { get; private set; } = null!;
    public ReactiveCommand<SavedFilterView, Unit> ApplySavedViewCommand { get; private set; } = null!;

    #endregion

    #region Saved Views

    public ObservableCollection<SavedFilterView> SavedFilterViews { get; } = new();

    private SavedFilterView? _selectedSavedFilterView;
    public SavedFilterView? SelectedSavedFilterView
    {
        get => _selectedSavedFilterView;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSavedFilterView, value);
            if (value != null && !_isApplyingSavedView)
            {
                ApplySavedView(value);
            }
        }
    }

    private string _newSavedViewName = string.Empty;
    public string NewSavedViewName
    {
        get => _newSavedViewName;
        set => this.RaiseAndSetIfChanged(ref _newSavedViewName, value);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Updates the available filter options based on current data
    /// </summary>
    public void UpdateAvailableOptions(
        IEnumerable<string> creators,
        IEnumerable<string> reviewers,
        IEnumerable<string> sourceBranches,
        IEnumerable<string> targetBranches,
        IEnumerable<string> groups)
    {
        UpdateCollection(AvailableCreators, creators);
        UpdateCollection(AvailableReviewers, reviewers);
        UpdateCollection(AvailableSourceBranches, sourceBranches);
        UpdateCollection(AvailableTargetBranches, targetBranches);
        UpdateCollection(AvailableGroups, groups);
    }

    /// <summary>
    /// Helper method to update an observable collection efficiently
    /// </summary>
    private void UpdateCollection(ObservableCollection<string> collection, IEnumerable<string> newItems)
    {
        collection.Clear();
        foreach (var item in newItems)
        {
            collection.Add(item);
        }
    }

    #endregion

    #region Initialization

    private void InitializeCommands()
    {
        ClearAllFiltersCommand = ReactiveCommand.Create(() => _filterState.ResetToDefaults());
        ResetFiltersCommand = ReactiveCommand.Create(() => _filterState.ResetToDefaults());
        ApplyPresetCommand = ReactiveCommand.Create<string>(preset => _presetManager.ApplyPreset(preset, _filterState, UserRole.General));
        SaveCurrentFiltersCommand = ReactiveCommand.Create(SaveCurrentFilters);
        ApplySavedViewCommand = ReactiveCommand.Create<SavedFilterView>(ApplySavedView);
    }

    private void InitializeCollections()
    {
        // Initialize static collections
        AvailableStatuses.Add("All");
        AvailableStatuses.Add("Active");
        AvailableStatuses.Add("Completed");
        AvailableStatuses.Add("Abandoned");

        AvailableReviewerVotes.Add("All");
        AvailableReviewerVotes.Add("No vote");
        AvailableReviewerVotes.Add("Approved");
        AvailableReviewerVotes.Add("Approved with suggestions");
        AvailableReviewerVotes.Add("Waiting for author");
        AvailableReviewerVotes.Add("Rejected");

        DraftFilterOptions.Add("All");
        DraftFilterOptions.Add("Drafts Only");
        DraftFilterOptions.Add("Non-Drafts Only");
        
        // Set default selections to ensure dropdowns are not blank
        // These will be overridden if there are saved preferences
        SetDefaultSelections();
        LoadSavedViews();
    }
    
    private void SetDefaultSelections()
    {
        // Ensure filter state has proper defaults
        if (string.IsNullOrEmpty(_filterState.StatusFilter))
        {
            _filterState.StatusFilter = "All";
        }
        
        if (string.IsNullOrEmpty(_filterState.ReviewerVoteFilter))
        {
            _filterState.ReviewerVoteFilter = "All";
        }
        
        if (string.IsNullOrEmpty(_filterState.DraftFilter))
        {
            _filterState.DraftFilter = "All";
        }
    }

    private void SubscribeToFilterStateChanges()
    {
        // Subscribe to filter state changes to update UI properties
        // Split into multiple subscriptions to avoid WhenAnyValue parameter limit issues
        _filterState.WhenAnyValue(x => x.MyPullRequestsOnly)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.AssignedToMeOnly)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.NeedsMyReviewOnly)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.ExcludeMyPullRequests)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.GlobalSearchText)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.TitleFilter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.CreatorFilter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.ReviewerFilter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.SourceBranchFilter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.TargetBranchFilter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.DraftFilter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.CreatedAfter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.CreatedBefore)
            .Subscribe(_ => NotifyUIPropertiesChanged());
        
        _filterState.WhenAnyValue(x => x.EnableGroupsWithoutVoteFilter)
            .Subscribe(_ => NotifyUIPropertiesChanged());
    }

    private void NotifyUIPropertiesChanged()
    {
        if (!_isApplyingPreset)
        {
            this.RaisePropertyChanged(nameof(ShowMyPullRequestsOnly));
            this.RaisePropertyChanged(nameof(ShowAssignedToMeOnly));
            this.RaisePropertyChanged(nameof(ShowNeedsMyReviewOnly));
            this.RaisePropertyChanged(nameof(ExcludeMyPullRequests));
            this.RaisePropertyChanged(nameof(GlobalSearchText));
            this.RaisePropertyChanged(nameof(TitleFilter));
            this.RaisePropertyChanged(nameof(CreatorFilter));
            this.RaisePropertyChanged(nameof(ReviewerFilter));
            this.RaisePropertyChanged(nameof(SourceBranchFilter));
            this.RaisePropertyChanged(nameof(TargetBranchFilter));
            this.RaisePropertyChanged(nameof(DraftFilter));
            this.RaisePropertyChanged(nameof(CreatedAfter));
            this.RaisePropertyChanged(nameof(CreatedBefore));
            this.RaisePropertyChanged(nameof(EnableGroupsWithoutVoteFilter));
        }
    }

    private void SaveCurrentFilters()
    {
        var preferences = LoadPreferences();
        var defaultName = $"Filter View {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        var viewName = string.IsNullOrWhiteSpace(NewSavedViewName) ? defaultName : NewSavedViewName.Trim();

        var savedView = new SavedFilterView
        {
            Name = viewName,
            FilterCriteria = _filterState.Criteria.Clone(),
            SortCriteria = new SortCriteria
            {
                CurrentPreset = _filterState.SortCriteria.CurrentPreset
            },
            LastUsed = DateTime.Now
        };

        var existing = preferences.SavedViews.FirstOrDefault(v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            preferences.SavedViews.Remove(existing);
            var existingInCollection = SavedFilterViews.FirstOrDefault(v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
            if (existingInCollection != null)
            {
                SavedFilterViews.Remove(existingInCollection);
            }
        }

        preferences.SavedViews.Add(savedView);
        preferences.LastSelectedView = savedView.Name;
        FilterSortPreferencesStorage.Save(preferences);

        SavedFilterViews.Add(savedView);
        _isApplyingSavedView = true;
        SelectedSavedFilterView = savedView;
        _isApplyingSavedView = false;
        NewSavedViewName = string.Empty;
    }

    private void ApplySavedView(SavedFilterView savedView)
    {
        if (savedView?.FilterCriteria == null)
        {
            return;
        }

        _isApplyingSavedView = true;
        try
        {
            _filterState.ApplyAtomically(state =>
            {
                state.ResetToDefaults();
                var criteria = savedView.FilterCriteria;

                state.TitleFilter = criteria.TitleFilter ?? string.Empty;
                state.CreatorFilter = criteria.CreatorFilter ?? string.Empty;
                state.SourceBranchFilter = criteria.SourceBranchFilter ?? string.Empty;
                state.TargetBranchFilter = criteria.TargetBranchFilter ?? string.Empty;
                state.ReviewerFilter = criteria.ReviewerFilter ?? string.Empty;
                state.GlobalSearchText = criteria.GlobalSearchText ?? string.Empty;
                state.MyPullRequestsOnly = criteria.MyPullRequestsOnly;
                state.AssignedToMeOnly = criteria.AssignedToMeOnly;
                state.NeedsMyReviewOnly = criteria.NeedsMyReviewOnly;
                state.ExcludeMyPullRequests = criteria.ExcludeMyPullRequests;
                state.IsDraft = criteria.IsDraft;
                state.SelectedReviewerVotes = criteria.SelectedReviewerVotes.ToList();
                state.SelectedStatuses = criteria.SelectedStatuses.ToList();
                state.CreatedAfter = criteria.CreatedAfter;
                state.CreatedBefore = criteria.CreatedBefore;
                state.EnableGroupsWithoutVoteFilter = criteria.EnableGroupsWithoutVoteFilter;
                state.SelectedGroupsWithoutVote = criteria.SelectedGroupsWithoutVote.ToList();
                state.SetFilterSource("SavedView", savedView.Name);
            });

            _filterState.SortCriteria.CurrentPreset = savedView.SortCriteria?.CurrentPreset ?? "Most Recent";

            savedView.LastUsed = DateTime.Now;
            var preferences = LoadPreferences();
            preferences.LastSelectedView = savedView.Name;
            var viewToUpdate = preferences.SavedViews.FirstOrDefault(v => v.Name == savedView.Name);
            if (viewToUpdate != null)
            {
                viewToUpdate.LastUsed = savedView.LastUsed;
            }

            FilterSortPreferencesStorage.Save(preferences);
        }
        finally
        {
            _isApplyingSavedView = false;
        }
    }

    public void LoadSavedViews()
    {
        var preferences = LoadPreferences();
        SavedFilterViews.Clear();
        foreach (var view in preferences.SavedViews)
        {
            SavedFilterViews.Add(view);
        }

        if (!string.IsNullOrWhiteSpace(preferences.LastSelectedView))
        {
            var last = SavedFilterViews.FirstOrDefault(v => v.Name == preferences.LastSelectedView);
            if (last != null)
            {
                _isApplyingSavedView = true;
                SelectedSavedFilterView = last;
                _isApplyingSavedView = false;
                ApplySavedView(last);
            }
        }
    }

    private static FilterSortPreferences LoadPreferences()
    {
        return FilterSortPreferencesStorage.TryLoad(out var preferences) && preferences != null
            ? preferences
            : new FilterSortPreferences();
    }

    #endregion
}
