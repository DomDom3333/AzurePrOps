using AzurePrOps.Models;
using AzurePrOps.ViewModels;

namespace AzurePrOps.Tests;

public class MainWindowFilterCommandEventTests
{
    private static MainWindowViewModel CreateViewModel()
    {
        var settings = new ConnectionSettings(
            Organization: string.Empty,
            Project: string.Empty,
            Repository: string.Empty,
            ReviewerId: string.Empty);

        return new MainWindowViewModel(settings);
    }

    [Fact]
    public void QuickFilterMyPrs_TriggersSingleFilterChangedEvent()
    {
        var vm = CreateViewModel();
        var events = 0;
        vm.FilterState.FilterChanged += () => events++;

        vm.ApplyQuickFilterMyPRsCommand.Execute().Subscribe();

        Assert.Equal(1, events);
    }

    [Fact]
    public void QuickFilterNeedsReview_TriggersSingleFilterChangedEvent()
    {
        var vm = CreateViewModel();
        var events = 0;
        vm.FilterState.FilterChanged += () => events++;

        vm.ApplyQuickFilterNeedsReviewCommand.Execute().Subscribe();

        Assert.Equal(1, events);
    }

    [Fact]
    public void QuickFilterRecent_TriggersFilterChangedEvent()
    {
        var vm = CreateViewModel();
        var events = 0;
        vm.FilterState.FilterChanged += () => events++;

        vm.ApplyQuickFilterRecentCommand.Execute().Subscribe();

        Assert.True(events >= 1);
    }

    [Fact]
    public void SaveCurrentFiltersCommand_AddsSavedFilterView()
    {
        var vm = CreateViewModel();
        vm.NewViewName = "My Saved Filters";

        vm.SaveCurrentFiltersCommand.Execute().Subscribe();

        Assert.Contains(vm.SavedFilterViews, view => view.Name == "My Saved Filters");
    }

    [Fact]
    public void MainWindow_ContainsBindingsForRecentAndSaveCurrentCommands()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../AzurePrOps"));
        var viewPath = Path.Combine(projectRoot, "Views", "MainWindow.axaml");
        var axaml = File.ReadAllText(viewPath);

        Assert.Contains("Command=\"{Binding ApplyQuickFilterRecentCommand}\"", axaml);
        Assert.Contains("Command=\"{Binding SaveCurrentFiltersCommand}\"", axaml);
    }

    [Fact]
    public void WorkflowPreset_TriggersSingleFilterChangedEvent()
    {
        var vm = CreateViewModel();
        var events = 0;
        vm.FilterState.FilterChanged += () => events++;

        vm.SelectedWorkflowPreset = "My Pull Requests";

        Assert.Equal(1, events);
    }

    [Fact]
    public void ResetFiltersCommand_TriggersSingleFilterChangedEvent()
    {
        var vm = CreateViewModel();
        var events = 0;
        vm.FilterState.FilterChanged += () => events++;

        vm.ResetFiltersCommand.Execute().Subscribe();

        Assert.Equal(1, events);
    }

    [Fact]
    public void ClearGroupSelectionCommand_TriggersSingleFilterChangedEvent()
    {
        var vm = CreateViewModel();
        vm.FilterState.SelectedGroups = new List<string> { "Team A" };

        var events = 0;
        vm.FilterState.FilterChanged += () => events++;

        vm.ClearGroupSelectionCommand.Execute().Subscribe();

        Assert.Equal(1, events);
    }
}
