using System;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using AzurePrOps.ReviewLogic.Services;
using ReviewModels = AzurePrOps.ReviewLogic.Models;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia;
using System.Diagnostics;
using AzurePrOps.Views;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<MainWindowViewModel>();
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

    private bool _isLoadingDiffs;
    public bool IsLoadingDiffs
    {
        get => _isLoadingDiffs;
        set => this.RaiseAndSetIfChanged(ref _isLoadingDiffs, value);
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
    public ReactiveCommand<PullRequestInfo?, Unit> OpenInBrowserCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveViewCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ApproveWithSuggestionsCommand { get; }
    public ReactiveCommand<Unit, Unit> WaitForAuthorCommand { get; }
    public ReactiveCommand<Unit, Unit> RejectCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkDraftCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkReadyCommand { get; }
    public ReactiveCommand<Unit, Unit> CompleteCommand { get; }
    public ReactiveCommand<Unit, Unit> AbandonCommand { get; }

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
        if (_pullRequestService is AzureDevOpsPullRequestService azService)
        {
            azService.UseGitClient = _settings.UseGitDiff;
        }
        _pullRequestService.SetErrorHandler(message =>
            _ = ShowErrorMessage(message));

        foreach (var v in FilterViewStorage.Load())
            FilterViews.Add(v);

        // Add error handling mechanism
        _client.SetErrorHandler(message => _ = ShowErrorMessage(message));

        var hasSelection = this.WhenAnyValue(x => x.SelectedPullRequest)
            .Select(pr => pr != null);

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

        LoadCommentsCommand = ReactiveCommand.CreateFromTask(async () => await LoadCommentsAsync(), hasSelection);

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
        }, hasSelection);

        ApproveWithSuggestionsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            await _client.ApproveWithSuggestionsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId,
                _settings.PersonalAccessToken);
        }, hasSelection);

        WaitForAuthorCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            await _client.SetPullRequestVoteAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId,
                -5,
                _settings.PersonalAccessToken);
        }, hasSelection);

        RejectCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            await _client.RejectPullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId,
                _settings.PersonalAccessToken);
        }, hasSelection);

        MarkDraftCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            await _client.SetPullRequestDraftAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                true,
                _settings.PersonalAccessToken);
        }, hasSelection);

        MarkReadyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            await _client.SetPullRequestDraftAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                false,
                _settings.PersonalAccessToken);
        }, hasSelection);

        CompleteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            var options = new ReviewModels.MergeOptions(false, false, string.Empty);
            await _client.CompletePullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                options,
                _settings.PersonalAccessToken);
        }, hasSelection);

        AbandonCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            await _client.AbandonPullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.PersonalAccessToken);
        }, hasSelection);

        OpenInBrowserCommand = ReactiveCommand.Create<PullRequestInfo?>(pr =>
        {
            var url = pr?.WebUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url.Trim(),
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open browser");
            }
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
        },
        this.WhenAnyValue(x => x.SelectedPullRequest, x => x.NewCommentText,
            (pr, text) => pr != null && !string.IsNullOrWhiteSpace(text)));

        ViewDetailsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            IsLoadingDiffs = true;

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
                _logger.LogDebug("Retrieved {Count} diffs for PR #{Id}", diffs.Count, SelectedPullRequest.Id);
                foreach (var diff in diffs)
                {
                    _logger.LogDebug("  - {Path}: OldText={Old} bytes, NewText={New} bytes, Diff={Diff} bytes", diff.FilePath, diff.OldText?.Length ?? 0, diff.NewText?.Length ?? 0, diff.Diff?.Length ?? 0);
                }

                // Always show the window, even if we couldn't get diffs
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var vm = new PullRequestDetailsWindowViewModel(
                        SelectedPullRequest,
                        _pullRequestService,
                        _settings,
                        Comments,
                        diffs,
                        new CommentsService(_client));
                    _logger.LogDebug("Created ViewModel with {Count} FileDiffs", vm.FileDiffs.Count);
                    var window = new PullRequestDetailsWindow { DataContext = vm };
                    window.Show();
                });
            }
            catch (Exception ex)
            {
                // If we get here, something really went wrong
                await ShowErrorMessage($"Failed to open pull request details: {ex.Message}");
            }
            finally
            {
                IsLoadingDiffs = false;
            }
        }, hasSelection);

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

        OpenSettingsCommand = ReactiveCommand.Create(() =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = new SettingsWindowViewModel(_settings);
                var window = new Views.SettingsWindow { DataContext = vm };
                _ = vm.LoadAsync();
                var old = desktop.MainWindow;
                desktop.MainWindow = window;
                window.Show();
                old?.Close();
            }
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
