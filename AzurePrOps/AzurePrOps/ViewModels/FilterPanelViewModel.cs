using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.Models;
using ReactiveUI;
using AzurePrOps.Models.FilteringAndSorting;

namespace AzurePrOps.ViewModels;

/// <summary>
/// Dedicated ViewModel for the filter panel that provides a clean interface
/// for managing all filtering operations
/// </summary>
public class FilterPanelViewModel : ViewModelBase
{
    private readonly FilterState _filterState;
    private FilterSortPreferences _preferences = new();

    public FilterPanelViewModel(FilterState filterState)
    {
        _filterState = filterState ?? throw new ArgumentNullException(nameof(filterState));

        // Initialize commands
        ClearAllFiltersCommand = ReactiveCommand.Create(ClearAllFilters);
        SaveCurrentFilterViewCommand = ReactiveCommand.CreateFromTask(SaveCurrentFilterView);
        ApplyFilterViewCommand = ReactiveCommand.Create<FilterView>(ApplyFilterView);
        DeleteFilterViewCommand = ReactiveCommand.Create<FilterView>(DeleteFilterView);
        ApplyQuickFilterCommand = ReactiveCommand.Create<string>(ApplyQuickFilter);

        // Initialize collections
        InitializeDropdownOptions();
        LoadSavedFilterViews();
    }

    #region Properties

    /// <summary>
    /// The underlying filter state - exposes all filter properties for binding
    /// </summary>
    public FilterState FilterState => _filterState;

    /// <summary>
    /// Available options for status dropdown
    /// </summary>
    public ObservableCollection<string> AvailableStatuses { get; } = new() 
    { 
        "All", "Active", "Completed", "Abandoned" 
    };

    /// <summary>
    /// Available options for reviewer votes dropdown
    /// </summary>
    public ObservableCollection<string> AvailableReviewerVotes { get; } = new() 
    { 
        "All", "No vote", "Approved", "Approved with suggestions", "Waiting for author", "Rejected" 
    };

    /// <summary>
    /// Available options for draft filter dropdown
    /// </summary>
    public ObservableCollection<string> DraftFilterOptions { get; } = new() 
    { 
        "All", "Drafts Only", "Non-Drafts Only" 
    };

    /// <summary>
    /// Available creators for filtering (populated from current PRs)
    /// </summary>
    public ObservableCollection<string> AvailableCreators { get; } = new();

    /// <summary>
    /// Available reviewers for filtering (populated from current PRs)
    /// </summary>
    public ObservableCollection<string> AvailableReviewers { get; } = new();

    /// <summary>
    /// Available source branches for filtering (populated from current PRs)
    /// </summary>
    public ObservableCollection<string> AvailableSourceBranches { get; } = new();

    /// <summary>
    /// Available target branches for filtering (populated from current PRs)
    /// </summary>
    public ObservableCollection<string> AvailableTargetBranches { get; } = new();

    /// <summary>
    /// Available groups for filtering
    /// </summary>
    public ObservableCollection<string> AvailableGroups { get; } = new();

    /// <summary>
    /// Saved filter views that can be applied
    /// </summary>
    public ObservableCollection<SavedFilterView> SavedFilterViews { get; } = new();

    /// <summary>
    /// Quick filter presets for common scenarios
    /// </summary>
    public ObservableCollection<QuickFilter> QuickFilters { get; } = new()
    {
        new("My PRs", "Show only my pull requests", "MyPRs"),
        new("Needs My Review", "Show PRs that need my review", "NeedsMyReview"),
        new("Assigned to Me", "Show PRs assigned to me", "AssignedToMe"),
        new("Recent Activity", "Show PRs with recent activity", "RecentActivity"),
        new("Active PRs", "Show only active pull requests", "ActivePRs"),
        new("Draft PRs", "Show only draft pull requests", "DraftPRs"),
        new("Clear Filters", "Remove all active filters", "ClearAll")
    };

    private bool _isFilterPanelExpanded = true;
    public bool IsFilterPanelExpanded
    {
        get => _isFilterPanelExpanded;
        set => this.RaiseAndSetIfChanged(ref _isFilterPanelExpanded, value);
    }

    private string _newFilterViewName = string.Empty;
    public string NewFilterViewName
    {
        get => _newFilterViewName;
        set => this.RaiseAndSetIfChanged(ref _newFilterViewName, value);
    }

    private string _newFilterViewDescription = string.Empty;
    public string NewFilterViewDescription
    {
        get => _newFilterViewDescription;
        set => this.RaiseAndSetIfChanged(ref _newFilterViewDescription, value);
    }

    #endregion

    #region Commands

    public ReactiveCommand<Unit, Unit> ClearAllFiltersCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCurrentFilterViewCommand { get; }
    public ReactiveCommand<FilterView, Unit> ApplyFilterViewCommand { get; }
    public ReactiveCommand<FilterView, Unit> DeleteFilterViewCommand { get; }
    public ReactiveCommand<string, Unit> ApplyQuickFilterCommand { get; }

    #endregion

    #region Command Implementations

    private void ClearAllFilters()
    {
        _filterState.ClearAllFilters();
    }

    private async Task SaveCurrentFilterView()
    {
        if (string.IsNullOrWhiteSpace(NewFilterViewName))
        {
            // TODO: Show validation message
            return;
        }

        // Create a SavedFilterView directly instead of using CreateSavedFilterView which returns FilterView
        var savedView = new SavedFilterView
        {
            Name = NewFilterViewName,
            Description = NewFilterViewDescription,
            FilterCriteria = new FilterCriteria
            {
                CurrentUserId = _filterState.Criteria.CurrentUserId,
                UserDisplayName = _filterState.Criteria.UserDisplayName,
                MyPullRequestsOnly = _filterState.MyPullRequestsOnly,
                AssignedToMeOnly = _filterState.AssignedToMeOnly,
                NeedsMyReviewOnly = _filterState.NeedsMyReviewOnly,
                ExcludeMyPullRequests = _filterState.ExcludeMyPullRequests,
                SelectedStatuses = _filterState.SelectedStatuses.ToList(),
                IsDraft = _filterState.IsDraft,
                GlobalSearchText = _filterState.GlobalSearchText,
                TitleFilter = _filterState.TitleFilter,
                CreatorFilter = _filterState.CreatorFilter,
                ReviewerFilter = _filterState.ReviewerFilter,
                SourceBranchFilter = _filterState.SourceBranchFilter,
                TargetBranchFilter = _filterState.TargetBranchFilter,
                SelectedReviewerVotes = _filterState.Criteria.SelectedReviewerVotes.ToList(),
                EnableGroupsWithoutVoteFilter = _filterState.EnableGroupsWithoutVoteFilter
            },
            SortCriteria = new SortCriteria
            {
                // Copy current sort criteria properties
                CurrentPreset = _filterState.SortCriteria.CurrentPreset
            },
            CreatedAt = DateTime.Now,
            LastUsed = DateTime.Now
        };
        
        // Save to storage
        await SaveFilterViewToStorage(savedView);
        
        // Add to collection
        SavedFilterViews.Add(savedView);
        
        // Clear the input fields
        NewFilterViewName = string.Empty;
        NewFilterViewDescription = string.Empty;
    }

    private void ApplyFilterView(FilterView filterView)
    {
        if (filterView == null) return;

        _filterState.ApplyFilterView(filterView);
        
        // Note: FilterView doesn't have LastUsed property, only SavedFilterView does
        // If we need to track usage, we'd need to convert or handle differently
    }

    private void DeleteFilterView(FilterView filterView)
    {
        if (filterView == null) return;

        // Remove from the SavedFilterViews collection by matching name
        var savedViewToRemove = SavedFilterViews.FirstOrDefault(sv => sv.Name == filterView.Name);
        if (savedViewToRemove != null)
        {
            SavedFilterViews.Remove(savedViewToRemove);
            // Remove from storage
            _ = Task.Run(() => DeleteFilterViewFromStorage(savedViewToRemove));
        }
    }

    private void ApplyQuickFilter(string filterType)
    {
        switch (filterType)
        {
            case "MyPRs":
                _filterState.ClearAllFilters();
                _filterState.MyPullRequestsOnly = true;
                break;
                
            case "NeedsMyReview":
                _filterState.ClearAllFilters();
                _filterState.NeedsMyReviewOnly = true;
                break;
                
            case "AssignedToMe":
                _filterState.ClearAllFilters();
                _filterState.AssignedToMeOnly = true;
                break;
                
            case "RecentActivity":
                _filterState.ClearAllFilters();
                _filterState.UpdatedAfter(DateTimeOffset.Now.AddDays(-7));
                break;
                
            case "ActivePRs":
                _filterState.ClearAllFilters();
                _filterState.SelectedStatuses = new() { "Active" };
                break;
                
            case "DraftPRs":
                _filterState.ClearAllFilters();
                _filterState.IsDraft = true;
                break;
                
            case "ClearAll":
                _filterState.ClearAllFilters();
                break;
        }
    }

    #endregion

    #region Data Management

    /// <summary>
    /// Updates the available filter options based on current pull requests
    /// </summary>
    public void UpdateAvailableOptions(IEnumerable<PullRequestInfo> pullRequests)
    {
        // Extract unique values for dropdowns
        var creators = pullRequests.Select(pr => pr.Creator ?? "Unknown")
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name)
            .ToList();

        var reviewers = pullRequests.SelectMany(pr => pr.Reviewers?.Select(r => r.DisplayName) ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name)
            .ToList();

        var sourceBranches = pullRequests.Select(pr => pr.SourceBranch ?? "Unknown")
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct()
            .OrderBy(branch => branch)
            .ToList();

        var targetBranches = pullRequests.Select(pr => pr.TargetBranch ?? "Unknown")
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct()
            .OrderBy(branch => branch)
            .ToList();

        // Update collections on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateCollection(AvailableCreators, creators);
            UpdateCollection(AvailableReviewers, reviewers);
            UpdateCollection(AvailableSourceBranches, sourceBranches);
            UpdateCollection(AvailableTargetBranches, targetBranches);
        });
    }

    /// <summary>
    /// Updates available groups for filtering
    /// </summary>
    public void UpdateAvailableGroups(IEnumerable<string> groups)
    {
        var sortedGroups = groups.OrderBy(g => g).ToList();
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateCollection(AvailableGroups, sortedGroups);
        });
    }

    private void UpdateCollection(ObservableCollection<string> collection, IEnumerable<string> newItems)
    {
        collection.Clear();
        foreach (var item in newItems)
        {
            collection.Add(item);
        }
    }

    private void InitializeDropdownOptions()
    {
        // Options are already initialized in property declarations
        // This method can be extended for dynamic initialization if needed
    }

    private void LoadSavedFilterViews()
    {
        try
        {
            var preferences = FilterSortPreferencesStorage.TryLoad(out var loadedPrefs) ? loadedPrefs : new FilterSortPreferences();
            
            SavedFilterViews.Clear();
            if (preferences?.SavedViews != null)
            {
                foreach (var view in preferences.SavedViews)
                {
                    SavedFilterViews.Add(view);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading saved filter views: {ex.Message}");
        }
    }

    private Task SaveFilterViewToStorage(SavedFilterView filterView)
    {
        try
        {
            var preferences = FilterSortPreferencesStorage.TryLoad(out var loadedPrefs) ? loadedPrefs : new FilterSortPreferences();
            
            // Remove existing view with same name
            preferences.SavedViews.RemoveAll(v => v?.Name == filterView.Name);
            
            // Add the new/updated view
            preferences.SavedViews.Add(filterView);
            
            FilterSortPreferencesStorage.Save(preferences);
        }
        catch (Exception ex)
        {
            // TODO: Log error
            System.Diagnostics.Debug.WriteLine($"Error saving filter view: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }

    private Task DeleteFilterViewFromStorage(SavedFilterView filterView)
    {
        try
        {
            var preferences = FilterSortPreferencesStorage.TryLoad(out var loadedPrefs) ? loadedPrefs : new FilterSortPreferences();
            
            preferences.SavedViews.RemoveAll(v => v?.Name == filterView.Name);
            
            FilterSortPreferencesStorage.Save(preferences);
        }
        catch (Exception ex)
        {
            // TODO: Log error
            System.Diagnostics.Debug.WriteLine($"Error deleting filter view: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }

    #endregion
}

/// <summary>
/// Represents a quick filter option for common filtering scenarios
/// </summary>
public record QuickFilter(string Name, string Description, string FilterType);
