using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using ReactiveUI;
using Avalonia.Threading;
using AzurePrOps.Models.FilteringAndSorting;
using AzurePrOps.Models;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.Services;

namespace AzurePrOps.ViewModels.Filters;

/// <summary>
/// Orchestrates all filter-related functionality, acting as the main coordinator
/// between FilterState, FilterPanelViewModel, and FilterPresetManager.
/// This centralizes filter logic that was previously scattered in MainWindowViewModel.
/// </summary>
public class FilterOrchestrator : ReactiveObject, IDisposable
{
    private readonly FilterState _filterState;
    private readonly FilterPanelViewModel _filterPanel;
    private readonly FilterPresetManager _presetManager;
    private readonly PullRequestFilteringSortingService _filterSortService;
    
    private bool _isInitialized;
    private UserRole _currentUserRole = UserRole.General;
    private IDisposable? _filterSubscription;
    private IReadOnlyList<string> _userGroupMemberships = Array.Empty<string>();

    public FilterOrchestrator(
        PullRequestFilteringSortingService filterSortService,
        FilterState? filterState = null)
    {
        _filterSortService = filterSortService ?? throw new ArgumentNullException(nameof(filterSortService));
        _filterState = filterState ?? new FilterState();
        _presetManager = new FilterPresetManager();
        _filterPanel = new FilterPanelViewModel(_filterState, _presetManager); // Fixed: Pass presetManager parameter
        
        InitializeSubscriptions();
    }

    #region Public Properties

    /// <summary>
    /// The centralized filter state - single source of truth for all filters
    /// </summary>
    public FilterState FilterState => _filterState;

    /// <summary>
    /// The filter panel ViewModel for UI binding
    /// </summary>
    public FilterPanelViewModel FilterPanel => _filterPanel;

    /// <summary>
    /// The preset manager for applying role-based and saved filters
    /// </summary>
    public FilterPresetManager PresetManager => _presetManager;

    /// <summary>
    /// Event fired when filters change and data should be re-filtered
    /// </summary>
    public event EventHandler<FilterCriteria>? FiltersChanged;

    /// <summary>
    /// Available filter presets based on current user role
    /// </summary>
    public IEnumerable<string> AvailablePresets => _presetManager.AvailablePresets;

    #endregion

    #region Initialization and Configuration

    /// <summary>
    /// Initializes the filter orchestrator with user information and role
    /// </summary>
    public void Initialize(string userId, string userDisplayName, UserRole userRole)
    {
        _currentUserRole = userRole;
        _filterState.SetUserInfo(userId, userDisplayName);
        _isInitialized = true;
    }

    /// <summary>
    /// Updates the available filter options based on current data
    /// </summary>
    public void UpdateAvailableOptions(IEnumerable<PullRequestInfo> allPullRequests, IReadOnlyList<string>? userGroupMemberships = null)
    {
        _userGroupMemberships = userGroupMemberships ?? _userGroupMemberships;
        var creators = allPullRequests.Select(pr => pr.Creator ?? "Unknown")
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name);

        var reviewers = allPullRequests.SelectMany(pr => pr.Reviewers?.Select(r => r.DisplayName) ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name);

        var sourceBranches = allPullRequests.Select(pr => pr.SourceBranch ?? "Unknown")
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct()
            .OrderBy(branch => branch);

        var targetBranches = allPullRequests.Select(pr => pr.TargetBranch ?? "Unknown")
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct()
            .OrderBy(branch => branch);

        var groupsFromReviewers = allPullRequests
            .SelectMany(pr => pr.Reviewers?.Where(r => r.IsGroup).Select(r => r.DisplayName) ?? Enumerable.Empty<string>());

        var groupsFromMemberships = _userGroupMemberships;

        var groups = groupsFromReviewers
            .Concat(groupsFromMemberships)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group);

        _filterPanel.UpdateAvailableOptions(creators, reviewers, sourceBranches, targetBranches, groups);
    }

    #endregion

    #region Filter Application

    /// <summary>
    /// Applies filters and sorting to a collection of pull requests
    /// </summary>
    public IEnumerable<PullRequestInfo> ApplyFiltersAndSorting(IEnumerable<PullRequestInfo> pullRequests, IReadOnlyList<string>? userGroupMemberships = null)
    {
        _userGroupMemberships = userGroupMemberships ?? _userGroupMemberships;

        return _filterSortService.ApplyFiltersAndSorting(
            pullRequests, 
            _filterState.Criteria, 
            _filterState.SortCriteria, 
            _userGroupMemberships,
            _filterState.Criteria.CurrentUserId);
    }

    /// <summary>
    /// Applies a filter preset by name
    /// </summary>
    public void ApplyPreset(string presetName)
    {
        if (!_isInitialized) return;
        
        _presetManager.ApplyPreset(presetName, _filterState, _currentUserRole);
    }

    /// <summary>
    /// Clears all active filters
    /// </summary>
    public void ClearAllFilters()
    {
        _filterState.ResetToDefaults();
    }

    /// <summary>
    /// Applies role-based default filters for the current user
    /// </summary>
    public void ApplyRoleBasedDefaults()
    {
        if (!_isInitialized) return;

        // Apply role-appropriate default preset
        var defaultPreset = _currentUserRole switch
        {
            UserRole.Developer or UserRole.Architect => "Needs My Review",
            UserRole.Tester => "Approved - Ready for Testing", 
            UserRole.TeamLead or UserRole.ScrumMaster => "All Active",
            _ => "Needs My Review"
        };

        ApplyPreset(defaultPreset);
    }

    #endregion

    #region Filter State Management

    /// <summary>
    /// Gets the current filter summary for display
    /// </summary>
    public string GetFilterSummary()
    {
        return _filterState.ActiveFiltersSummary;
    }

    /// <summary>
    /// Checks if any filters are currently active
    /// </summary>
    public bool HasActiveFilters()
    {
        return _filterState.HasActiveFilters;
    }

    /// <summary>
    /// Gets the current filter count with more detailed logic
    /// </summary>
    public int GetActiveFilterCount()
    {
        return _filterState.GetActiveFilterCount();
    }

    /// <summary>
    /// Exports current filter state for saving
    /// </summary>
    public FilterCriteria ExportFilterState()
    {
        return _filterState.Criteria.Clone();
    }

    /// <summary>
    /// Imports filter state from saved filters
    /// </summary>
    public void ImportFilterState(FilterCriteria criteria)
    {
        _filterState.ApplyAtomically(state =>
        {
            state.MyPullRequestsOnly = criteria.MyPullRequestsOnly;
            state.AssignedToMeOnly = criteria.AssignedToMeOnly;
            state.NeedsMyReviewOnly = criteria.NeedsMyReviewOnly;
            state.ExcludeMyPullRequests = criteria.ExcludeMyPullRequests;
            state.GlobalSearchText = criteria.GlobalSearchText;
            state.TitleFilter = criteria.TitleFilter;
            state.CreatorFilter = criteria.CreatorFilter;
            state.ReviewerFilter = criteria.ReviewerFilter;
            state.SourceBranchFilter = criteria.SourceBranchFilter;
            state.TargetBranchFilter = criteria.TargetBranchFilter;
            state.SelectedStatuses = criteria.SelectedStatuses.ToList();
            state.SelectedReviewerVotes = criteria.SelectedReviewerVotes.ToList();
            state.IsDraft = criteria.IsDraft;
            state.CreatedAfter = criteria.CreatedAfter;
            state.CreatedBefore = criteria.CreatedBefore;
            state.EnableGroupsWithoutVoteFilter = criteria.EnableGroupsWithoutVoteFilter;
            state.SelectedGroupsWithoutVote = criteria.SelectedGroupsWithoutVote.ToList();
            state.SetFilterSource(criteria.CurrentFilterSource, criteria.CurrentFilterSourceName);
        });
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Initializes subscriptions to filter state changes
    /// </summary>
    private void InitializeSubscriptions()
    {
        // Subscribe ONLY to FilterState's FilterChanged event
        // This is the single source of truth for all filter changes
        // The FilterState itself handles raising this event whenever any filter property changes
        _filterState.FilterChanged += OnFiltersChanged;
        
        // Subscribe to sort criteria changes separately since it has its own change tracking
        _filterSubscription = _filterState.SortCriteria.WhenAnyValue(x => x.CurrentPreset)
            .Subscribe(_ => OnFiltersChanged());
    }

    /// <summary>
    /// Handles filter changes and notifies listeners
    /// </summary>
    private void OnFiltersChanged()
    {
        // Always fire the event - initialization state shouldn't block filter updates
        // The UI should be able to respond to filter changes regardless of initialization timing
        
        // Ensure the event is fired on the UI thread to prevent race conditions
        Dispatcher.UIThread.Post(() =>
        {
            FiltersChanged?.Invoke(this, _filterState.Criteria);
        });
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _filterState.FilterChanged -= OnFiltersChanged;
        _filterSubscription?.Dispose();
    }

    #endregion
}
