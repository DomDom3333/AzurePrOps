using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
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
    
    private bool _isInitialized = false;
    private UserRole _currentUserRole = UserRole.General;
    private IDisposable? _filterSubscription;

    public FilterOrchestrator(
        PullRequestFilteringSortingService filterSortService,
        FilterState? filterState = null)
    {
        _filterSortService = filterSortService ?? throw new ArgumentNullException(nameof(filterSortService));
        _filterState = filterState ?? new FilterState();
        _presetManager = new FilterPresetManager();
        _filterPanel = new FilterPanelViewModel(_filterState, _presetManager);
        
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
    public void UpdateAvailableOptions(IEnumerable<PullRequestInfo> allPullRequests)
    {
        if (allPullRequests == null) return;

        var creators = allPullRequests.Select(pr => pr.Creator)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name);

        var reviewers = allPullRequests.SelectMany(pr => pr.Reviewers?.Select(r => r.DisplayName) ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name);

        var sourceBranches = allPullRequests.Select(pr => pr.SourceBranch)
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct()
            .OrderBy(branch => branch);

        var targetBranches = allPullRequests.Select(pr => pr.TargetBranch)
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct()
            .OrderBy(branch => branch);

        // TODO: Extract groups from pull request data or user memberships
        var groups = Enumerable.Empty<string>();

        _filterPanel.UpdateAvailableOptions(creators, reviewers, sourceBranches, targetBranches, groups);
    }

    #endregion

    #region Filter Application

    /// <summary>
    /// Applies filters and sorting to a collection of pull requests
    /// </summary>
    public IEnumerable<PullRequestInfo> ApplyFiltersAndSorting(IEnumerable<PullRequestInfo> pullRequests)
    {
        if (pullRequests == null) return Enumerable.Empty<PullRequestInfo>();

        return _filterSortService.ApplyFiltersAndSorting(
            pullRequests, 
            _filterState.Criteria, 
            _filterState.SortCriteria, 
            new List<string>(), // userGroupMemberships - empty for now
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
    /// Gets the current filter count
    /// </summary>
    public int GetActiveFilterCount()
    {
        return _filterState.HasActiveFilters ? 1 : 0; // Simplified - could be more detailed
    }

    /// <summary>
    /// Exports current filter state for saving
    /// </summary>
    public FilterCriteria ExportFilterState()
    {
        return _filterState.Criteria.Clone();
    }

    /// <summary>
    /// Imports and applies a saved filter state
    /// </summary>
    public void ImportFilterState(FilterCriteria criteria)
    {
        if (criteria == null) return;

        // Temporarily disable change notifications to avoid multiple updates
        using var _ = SuspendNotifications();
        
        // Apply the criteria to filter state
        _filterState.ResetToDefaults();
        ApplyFilterCriteria(criteria);
    }

    #endregion

    #region Quick Filter Actions

    /// <summary>
    /// Quick action: Show only my pull requests
    /// </summary>
    public void ShowMyPullRequestsOnly()
    {
        ClearAllFilters();
        _filterState.MyPullRequestsOnly = true;
        _filterState.Criteria.SetFilterSource("Quick Filter", "My Pull Requests");
    }

    /// <summary>
    /// Quick action: Show pull requests assigned to me
    /// </summary>
    public void ShowAssignedToMe()
    {
        ClearAllFilters();
        _filterState.AssignedToMeOnly = true;
        _filterState.Criteria.SetFilterSource("Quick Filter", "Assigned to Me");
    }

    /// <summary>
    /// Quick action: Show pull requests that need my review
    /// </summary>
    public void ShowNeedsMyReview()
    {
        ClearAllFilters();
        _filterState.NeedsMyReviewOnly = true;
        _filterState.StatusFilter = "Active";
        _filterState.ReviewerVoteFilter = "No vote";
        _filterState.Criteria.SetFilterSource("Quick Filter", "Needs My Review");
    }

    /// <summary>
    /// Quick action: Show recently updated pull requests
    /// </summary>
    public void ShowRecentlyUpdated(int days = 7)
    {
        ClearAllFilters();
        _filterState.StatusFilter = "Active";
        _filterState.SetDateRange(null, null, DateTimeOffset.Now.AddDays(-days), null);
        _filterState.Criteria.SetFilterSource("Quick Filter", $"Updated Last {days} Days");
    }

    #endregion

    #region Private Methods

    private void InitializeSubscriptions()
    {
        // Subscribe to filter state changes to notify consumers
        _filterSubscription = _filterState.WhenAnyValue(x => x.Criteria)
            .Skip(1) // Skip initial value
            .Subscribe(criteria => FiltersChanged?.Invoke(this, criteria));
    }

    private void ApplyFilterCriteria(FilterCriteria criteria)
    {
        // Apply personal filters
        _filterState.MyPullRequestsOnly = criteria.MyPullRequestsOnly;
        _filterState.AssignedToMeOnly = criteria.AssignedToMeOnly;
        _filterState.NeedsMyReviewOnly = criteria.NeedsMyReviewOnly;
        _filterState.ExcludeMyPullRequests = criteria.ExcludeMyPullRequests;

        // Apply status filters
        if (criteria.SelectedStatuses.Count == 1)
            _filterState.StatusFilter = criteria.SelectedStatuses.First();
        else if (criteria.SelectedStatuses.Count == 0)
            _filterState.StatusFilter = "All";

        _filterState.IsDraft = criteria.IsDraft;

        // Apply text filters
        _filterState.GlobalSearchText = criteria.GlobalSearchText;
        _filterState.TitleFilter = criteria.TitleFilter;
        _filterState.CreatorFilter = criteria.CreatorFilter;
        _filterState.ReviewerFilter = criteria.ReviewerFilter;
        _filterState.SourceBranchFilter = criteria.SourceBranchFilter;
        _filterState.TargetBranchFilter = criteria.TargetBranchFilter;

        // Apply reviewer vote filters
        if (criteria.SelectedReviewerVotes.Count == 1)
            _filterState.ReviewerVoteFilter = criteria.SelectedReviewerVotes.First();
        else if (criteria.SelectedReviewerVotes.Count == 0)
            _filterState.ReviewerVoteFilter = "All";

        // Apply date filters
        _filterState.SetDateRange(criteria.CreatedAfter, criteria.CreatedBefore, 
            criteria.UpdatedAfter, criteria.UpdatedBefore);

        // Apply group filters
        _filterState.EnableGroupsWithoutVoteFilter = criteria.EnableGroupsWithoutVoteFilter;
    }

    private IDisposable SuspendNotifications()
    {
        // This would temporarily disable filter change notifications
        // Implementation depends on your specific needs
        return System.Reactive.Disposables.Disposable.Empty;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _filterSubscription?.Dispose();
        // FilterPanel doesn't implement IDisposable, so we don't need to dispose it
    }

    #endregion
}
