using System;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using AzurePrOps.ReviewLogic.Services;
using ReviewModels = AzurePrOps.ReviewLogic.Models;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using AzurePrOps.Views;

namespace AzurePrOps.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly IPullRequestService _pullRequestService;
    private readonly Models.ConnectionSettings _settings;

    public ObservableCollection<PullRequestInfo> PullRequests { get; } = new();
    private readonly ObservableCollection<PullRequestInfo> _allPullRequests = new();
    public ObservableCollection<PullRequestComment> Comments { get; } = new();

    public ObservableCollection<FilterView> FilterViews { get; } = new();

    public ObservableCollection<string> TitleOptions { get; } = new();
    public ObservableCollection<string> CreatorOptions { get; } = new();
    public ObservableCollection<string> SourceBranchOptions { get; } = new();
    public ObservableCollection<string> TargetBranchOptions { get; } = new();

    private FilterView? _selectedFilterView;
    public FilterView? SelectedFilterView
    {
        get => _selectedFilterView;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedFilterView, value);
            if (value != null)
            {
                TitleFilter = value.Title;
                CreatorFilter = value.Creator;
                SourceBranchFilter = value.SourceBranch;
                TargetBranchFilter = value.TargetBranch;
                StatusFilter = string.IsNullOrWhiteSpace(value.Status) ? "All" : value.Status;
            }
        }
    }

    private string _newViewName = string.Empty;
    public string NewViewName
    {
        get => _newViewName;
        set => this.RaiseAndSetIfChanged(ref _newViewName, value);
    }

    private string _titleFilter = string.Empty;
    public string TitleFilter
    {
        get => _titleFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _titleFilter, value);
            ApplyFilters();
        }
    }

    private string _creatorFilter = string.Empty;
    public string CreatorFilter
    {
        get => _creatorFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _creatorFilter, value);
            ApplyFilters();
        }
    }

    private string _sourceBranchFilter = string.Empty;
    public string SourceBranchFilter
    {
        get => _sourceBranchFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceBranchFilter, value);
            ApplyFilters();
        }
    }

    private string _targetBranchFilter = string.Empty;
    public string TargetBranchFilter
    {
        get => _targetBranchFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _targetBranchFilter, value);
            ApplyFilters();
        }
    }

    private string _statusFilter = "All";
    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _statusFilter, value);
            ApplyFilters();
        }
    }

    public string[] StatusOptions { get; } = new[] { "All", "active", "completed", "abandoned" };

    private string _newCommentText = string.Empty;
    public string NewCommentText
    {
        get => _newCommentText;
        set => this.RaiseAndSetIfChanged(ref _newCommentText, value);
    }

    private PullRequestInfo? _selectedPullRequest;
    public PullRequestInfo? SelectedPullRequest
    {
        get => _selectedPullRequest;
        set => this.RaiseAndSetIfChanged(ref _selectedPullRequest, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadCommentsCommand { get; }
    public ReactiveCommand<Unit, Unit> ApproveCommand { get; }
    public ReactiveCommand<Unit, Unit> PostCommentCommand { get; }
    public ReactiveCommand<Unit, Unit> ViewDetailsCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveViewCommand { get; }

    private async Task ShowErrorMessage(string message)
    {
        // Create and show an error window with the message
        var errorViewModel = new ErrorWindowViewModel
        {
            ErrorMessage = message
        };

        // Use the appropriate way to show the error window based on your application architecture
        // This is a simple implementation, you might need to adjust based on your UI framework
        await Dispatcher.UIThread.InvokeAsync(() => 
        {
            var errorWindow = new ErrorWindow
            {
                DataContext = errorViewModel
            };
            errorWindow.Show();
        });
    }

    public MainWindowViewModel(Models.ConnectionSettings settings)
    {
        _settings = settings;

        _pullRequestService = PullRequestServiceFactory.Create(
            PullRequestServiceType.AzureDevOps);
        _pullRequestService.SetErrorHandler(message =>
            _ = ShowErrorMessage(message));

        foreach (var v in FilterViewStorage.Load())
            FilterViews.Add(v);

        // Add error handling mechanism
        _client.SetErrorHandler((message) => ShowErrorMessage(message));

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                var prs = await _client.GetPullRequestsAsync(
                    _settings.Organization,
                    _settings.Project,
                    _settings.Repository,
                    _settings.PersonalAccessToken);

                _allPullRequests.Clear();
                foreach (var pr in prs.OrderByDescending(p => p.Created))
                {
                    _allPullRequests.Add(pr);
                }
                UpdateFilterOptions();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                // Handle the error appropriately
                // Show the error message in a way that's compatible with the application UI
                await ShowErrorMessage($"Failed to refresh pull requests: {ex.Message}");
            }
        });

        LoadCommentsCommand = ReactiveCommand.CreateFromTask(async () => await LoadCommentsAsync());

        ApproveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            await _client.ApprovePullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId,
                _settings.PersonalAccessToken);
        });

        PostCommentCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null || string.IsNullOrWhiteSpace(NewCommentText))
                return;

            await _client.PostPullRequestCommentAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                NewCommentText,
                _settings.PersonalAccessToken);
            NewCommentText = string.Empty;
        });

        ViewDetailsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            try
            {
                // First load comments - if this fails, we'll still try to show the window with diffs
                try
                {
                    await LoadCommentsAsync();
                }
                catch (Exception ex)
                {
                    await ShowErrorMessage($"Failed to load comments: {ex.Message}");
                    // Continue - we'll just show the PR with empty comments
                }

                // Try to get diffs
                var diffs = await _pullRequestService.GetPullRequestDiffAsync(
                    _settings.Organization,
                    _settings.Project,
                    _settings.Repository,
                    SelectedPullRequest.Id,
                    _settings.PersonalAccessToken,
                    null,
                    null);

                // Log information about the diffs
                Console.WriteLine($"Retrieved {diffs.Count} diffs for PR #{SelectedPullRequest.Id}");
                foreach (var diff in diffs)
                {
                    Console.WriteLine($"  - {diff.FilePath}: OldText={diff.OldText?.Length ?? 0} bytes, NewText={diff.NewText?.Length ?? 0} bytes, Diff={diff.Diff?.Length ?? 0} bytes");
                    Console.WriteLine(diff.OldText);
                    Console.WriteLine(diff.NewText);
                }

                // Always show the window, even if we couldn't get diffs
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var vm = new PullRequestDetailsWindowViewModel(SelectedPullRequest, Comments, diffs);
                    Console.WriteLine($"Created ViewModel with {vm.FileDiffs.Count} FileDiffs");
                    var window = new PullRequestDetailsWindow { DataContext = vm };
                    window.Show();
                });
            }
            catch (Exception ex)
            {
                // If we get here, something really went wrong
                await ShowErrorMessage($"Failed to open pull request details: {ex.Message}");
            }
        });

        SaveViewCommand = ReactiveCommand.Create(() =>
        {
            if (string.IsNullOrWhiteSpace(NewViewName))
                return;

            var view = new FilterView(
                NewViewName,
                TitleFilter,
                CreatorFilter,
                SourceBranchFilter,
                TargetBranchFilter,
                StatusFilter == "All" ? string.Empty : StatusFilter);

            var existing = FilterViews.FirstOrDefault(v => v.Name == view.Name);
            if (existing != null)
                FilterViews.Remove(existing);

            FilterViews.Add(view);
            FilterViewStorage.Save(FilterViews);
            NewViewName = string.Empty;
            SelectedFilterView = view;
        });
    }

    private void ApplyFilters()
    {
        PullRequests.Clear();
        var filtered = _allPullRequests.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(TitleFilter))
            filtered = filtered.Where(pr => pr.Title.Contains(TitleFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(CreatorFilter))
            filtered = filtered.Where(pr => pr.Creator.Contains(CreatorFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SourceBranchFilter))
            filtered = filtered.Where(pr => pr.SourceBranch.Contains(SourceBranchFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(TargetBranchFilter))
            filtered = filtered.Where(pr => pr.TargetBranch.Contains(TargetBranchFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(StatusFilter) && StatusFilter != "All")
            filtered = filtered.Where(pr => pr.Status.Equals(StatusFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var pr in filtered)
        {
            PullRequests.Add(pr);
        }
    }

    private void UpdateFilterOptions()
    {
        TitleOptions.Clear();
        CreatorOptions.Clear();
        SourceBranchOptions.Clear();
        TargetBranchOptions.Clear();

        foreach (var pr in _allPullRequests)
        {
            if (!TitleOptions.Contains(pr.Title))
                TitleOptions.Add(pr.Title);
            if (!CreatorOptions.Contains(pr.Creator))
                CreatorOptions.Add(pr.Creator);
            if (!SourceBranchOptions.Contains(pr.SourceBranch))
                SourceBranchOptions.Add(pr.SourceBranch);
            if (!TargetBranchOptions.Contains(pr.TargetBranch))
                TargetBranchOptions.Add(pr.TargetBranch);
        }
    }

    private async Task LoadCommentsAsync()
    {
        if (SelectedPullRequest == null)
            return;

        var comments = await _client.GetPullRequestCommentsAsync(
            _settings.Organization,
            _settings.Project,
            _settings.Repository,
            SelectedPullRequest.Id,
            _settings.PersonalAccessToken);

        Comments.Clear();
        foreach (var c in comments)
        {
            Comments.Add(c);
        }
    }
}
