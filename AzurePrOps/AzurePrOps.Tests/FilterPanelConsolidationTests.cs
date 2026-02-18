using AzurePrOps.Models;
using AzurePrOps.Models.FilteringAndSorting;
using AzurePrOps.Services;
using AzurePrOps.ViewModels.Filters;

namespace AzurePrOps.Tests;

[Collection("FilterPreferences")]
public class FilterPanelConsolidationTests : IDisposable
{
    private readonly string _preferencesPath;
    private readonly string? _backupContent;

    public FilterPanelConsolidationTests()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _preferencesPath = Path.Combine(appData, "AzurePrOps", "filter-sort-preferences.json");
        _backupContent = File.Exists(_preferencesPath) ? File.ReadAllText(_preferencesPath) : null;

        if (File.Exists(_preferencesPath))
        {
            File.Delete(_preferencesPath);
        }
    }

    [Fact]
    public void ApplyPreset_UpdatesFilterStateThroughOrchestrator()
    {
        var orchestrator = new FilterOrchestrator(new PullRequestFilteringSortingService());
        orchestrator.Initialize("user-1", "User One", UserRole.Developer);

        orchestrator.ApplyPreset("Needs My Review");

        Assert.True(orchestrator.FilterState.NeedsMyReviewOnly);
        Assert.Contains("No vote", orchestrator.FilterState.SelectedReviewerVotes);
        Assert.Equal("Preset", orchestrator.FilterState.CurrentFilterSource);
        Assert.Equal("Needs My Review", orchestrator.FilterState.Criteria.CurrentFilterSourceName);
    }

    [Fact]
    public void SaveAndLoadViews_UsesSingleSavedViewStack()
    {
        var orchestrator = new FilterOrchestrator(new PullRequestFilteringSortingService());
        orchestrator.FilterState.TitleFilter = "Bugfix";
        orchestrator.FilterPanel.NewSavedViewName = "Consolidated View";

        orchestrator.FilterPanel.SaveCurrentFiltersCommand.Execute().Subscribe();

        Assert.Contains(orchestrator.FilterPanel.SavedFilterViews, v => v.Name == "Consolidated View");

        Assert.True(FilterSortPreferencesStorage.TryLoad(out var preferences));
        var savedView = Assert.Single(preferences!.SavedViews, v => v.Name == "Consolidated View");

        Assert.Equal("Consolidated View", savedView.Name);
        Assert.Equal("Bugfix", savedView.FilterCriteria.TitleFilter);
    }

    [Fact]
    public void ApplyingSavedView_UpdatesSummaryTextFromFilterState()
    {
        var orchestrator = new FilterOrchestrator(new PullRequestFilteringSortingService());

        var savedView = new SavedFilterView
        {
            Name = "Summary Check",
            FilterCriteria = new FilterCriteria
            {
                TitleFilter = "Hotfix",
                SelectedStatuses = new List<string> { "Active" }
            },
            SortCriteria = new SortCriteria { CurrentPreset = "Most Recent" }
        };

        orchestrator.FilterPanel.SavedFilterViews.Add(savedView);
        orchestrator.FilterPanel.SelectedSavedFilterView = savedView;

        var summary = orchestrator.GetFilterSummary();

        Assert.Contains("Title: Hotfix", summary);
        Assert.Contains("Status: Active", summary);
    }

    public void Dispose()
    {
        if (_backupContent is null)
        {
            if (File.Exists(_preferencesPath))
            {
                File.Delete(_preferencesPath);
            }

            return;
        }

        var dir = Path.GetDirectoryName(_preferencesPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_preferencesPath, _backupContent);
    }
}
