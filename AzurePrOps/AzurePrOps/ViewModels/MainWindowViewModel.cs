using System;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.Models;
using AzurePrOps.Models.FilteringAndSorting;
using AzurePrOps.Services;
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
    private readonly PullRequestFilteringSortingService _filterSortService = new();

    public ObservableCollection<PullRequestInfo> PullRequests { get; } = new();
    private readonly ObservableCollection<PullRequestInfo> _allPullRequests = new();

    // Enhanced filtering and sorting
    public FilterCriteria FilterCriteria { get; } = new();
    public SortCriteria SortCriteria { get; } = new();
    private FilterSortPreferences _preferences = new();

    public ObservableCollection<FilterView> FilterViews { get; } = new();
    public ObservableCollection<SavedFilterView> SavedFilterViews { get; } = new();

    // Available options for dropdowns
    public ObservableCollection<string> AvailableStatuses { get; } = new();
    public ObservableCollection<string> AvailableReviewerVotes { get; } = new();
    public ObservableCollection<string> AvailableCreators { get; } = new();
    public ObservableCollection<string> AvailableReviewers { get; } = new();
    public ObservableCollection<string> AvailableSourceBranches { get; } = new();
    public ObservableCollection<string> AvailableTargetBranches { get; } = new();

    // Sort presets
    public ObservableCollection<string> SortPresets { get; } = new()
    {
        "Newest First", "Oldest First", "Title A-Z", "Creator A-Z", 
        "Status Priority", "Review Priority", "High Activity"
    };

    private string _selectedSortPreset = "Newest First";
    public string SelectedSortPreset
    {
        get => _selectedSortPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSortPreset, value);
            SortCriteria.ApplyPreset(value);
            this.RaisePropertyChanged(nameof(SelectedSortPresetTooltip));
            ApplyFiltersAndSorting();
        }
    }

    public string SelectedSortPresetTooltip => 
        Models.FilteringAndSorting.SortCriteria.GetSortPresetTooltip(SelectedSortPreset);

    // Workflow presets
    public ObservableCollection<string> WorkflowPresets { get; } = new()
    {
        "All Pull Requests", "Team Lead Overview", "My Pull Requests", "Need My Review", "Ready for QA"
    };

    private string _selectedWorkflowPreset = "All Pull Requests";
    public string SelectedWorkflowPreset
    {
        get => _selectedWorkflowPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedWorkflowPreset, value);
            FilterCriteria.ApplyWorkflowPreset(value);
            this.RaisePropertyChanged(nameof(SelectedWorkflowPresetTooltip));
            ApplyFiltersAndSorting();
        }
    }

    public string SelectedWorkflowPresetTooltip => 
        Models.FilteringAndSorting.FilterCriteria.GetWorkflowPresetTooltip(SelectedWorkflowPreset);

    public ObservableCollection<string> TitleOptions { get; } = new();
    public ObservableCollection<string> CreatorOptions { get; } = new();
    public ObservableCollection<string> SourceBranchOptions { get; } = new();
    public ObservableCollection<string> TargetBranchOptions { get; } = new();

    public bool LifecycleActionsEnabled => FeatureFlagManager.LifecycleActionsEnabled;

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
            FilterCriteria.TitleFilter = value;
            ApplyFiltersAndSorting();
        }
    }

    private string _creatorFilter = string.Empty;
    public string CreatorFilter
    {
        get => _creatorFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _creatorFilter, value);
            FilterCriteria.CreatorFilter = value;
            ApplyFiltersAndSorting();
        }
    }

    private string _sourceBranchFilter = string.Empty;
    public string SourceBranchFilter
    {
        get => _sourceBranchFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceBranchFilter, value);
            FilterCriteria.SourceBranchFilter = value;
            ApplyFiltersAndSorting();
        }
    }

    private string _targetBranchFilter = string.Empty;
    public string TargetBranchFilter
    {
        get => _targetBranchFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _targetBranchFilter, value);
            FilterCriteria.TargetBranchFilter = value;
            ApplyFiltersAndSorting();
        }
    }

    private string _statusFilter = "All";
    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _statusFilter, value);
            if (value == "All")
                FilterCriteria.SelectedStatuses.Clear();
            else
            {
                FilterCriteria.SelectedStatuses.Clear();
                FilterCriteria.SelectedStatuses.Add(value);
            }
            ApplyFiltersAndSorting();
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

    // Summary statistics properties
    public int ActivePRCount => PullRequests.Count(pr => pr.Status.Equals("Active", StringComparison.OrdinalIgnoreCase));
    
    public int MyReviewPendingCount => PullRequests.Count(pr => 
        pr.Reviewers.Any(r => r.Id.Equals(_settings.ReviewerId, StringComparison.OrdinalIgnoreCase) && 
                              (r.Vote.Equals("No vote", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(r.Vote))));

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
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
    public ReactiveCommand<Unit, Unit> ResetFiltersCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetSortingCommand { get; }
    
    // Sort field options for dropdown
    public ObservableCollection<string> SortFieldOptions { get; } = new()
    {
        "Title", "Creator", "Created Date", "Status", "Source Branch", 
        "Target Branch", "Reviewer Vote", "PR ID", "Reviewer Count"
    };

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

        // Initialize new filtering and sorting system
        LoadPreferences();
        FilterCriteria.CurrentUserId = _settings.ReviewerId ?? string.Empty;
        
        // Set up property change handlers for real-time filtering
        FilterCriteria.PropertyChanged += (_, _) => ApplyFiltersAndSorting();
        SortCriteria.PropertyChanged += (_, _) => ApplyFiltersAndSorting();

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
                    var vote = pr.Reviewers.FirstOrDefault(r => r.Id == _settings.ReviewerId)?.Vote ?? "No vote";
                    var showDraft = pr.IsDraft && FeatureFlagManager.LifecycleActionsEnabled;
                    _allPullRequests.Add(pr with { ReviewerVote = vote, ShowDraftBadge = showDraft });
                }
                UpdateFilterOptions();
                UpdateAvailableOptions();
                ApplyFiltersAndSorting();
            }
            catch (Exception ex)
            {
                // Handle the error appropriately
                // Show the error message in a way that's compatible with the application UI
                await ShowErrorMessage($"Failed to refresh pull requests: {ex.Message}");
            }
        });


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
            await Dispatcher.UIThread.InvokeAsync(() => NewCommentText = string.Empty);
        },
        this.WhenAnyValue(x => x.SelectedPullRequest, x => x.NewCommentText,
            (pr, text) => pr != null && !string.IsNullOrWhiteSpace(text)));

        ViewDetailsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            var pr = SelectedPullRequest;

            // Create window immediately to avoid UI freeze
            PullRequestDetailsWindowViewModel? vm = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm = new PullRequestDetailsWindowViewModel(
                    pr,
                    _pullRequestService,
                    _settings,
                    null,
                    null,
                    new CommentsService(_client));
                _logger.LogDebug("Created ViewModel for PR #{Id}", pr.Id);
                var window = new PullRequestDetailsWindow { DataContext = vm };
                window.Show();
            });

            // Start background loading of diffs without blocking UI
            await Dispatcher.UIThread.InvokeAsync(() => IsLoadingDiffs = true);
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var diffs = await _pullRequestService.GetPullRequestDiffAsync(
                        _settings.Organization,
                        _settings.Project,
                        _settings.Repository,
                        pr.Id,
                        _settings.PersonalAccessToken,
                        null,
                        null);

                    _logger.LogDebug("Retrieved {Count} diffs for PR #{Id}", diffs.Count, pr.Id);
                    foreach (var diff in diffs)
                    {
                        _logger.LogDebug("  - {Path}: OldText={Old} bytes, NewText={New} bytes, Diff={Diff} bytes", diff.FilePath, diff.OldText?.Length ?? 0, diff.NewText?.Length ?? 0, diff.Diff?.Length ?? 0);
                    }

                    if (vm != null)
                    {
                        await vm.LoadDiffsAsync(diffs);
                    }
                }
                catch (Exception ex)
                {
                    await ShowErrorMessage($"Failed to load diffs: {ex.Message}");
                }
                finally
                {
                    await Dispatcher.UIThread.InvokeAsync(() => IsLoadingDiffs = false);
                }
            });
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

        OpenSettingsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = new SettingsWindowViewModel(_settings);
                var window = new Views.SettingsWindow { DataContext = vm };
                vm.DialogWindow = window;
                await vm.LoadAsync();
                await window.ShowDialog(desktop.MainWindow);
            }
        });

        ResetFiltersCommand = ReactiveCommand.Create(() =>
        {
            FilterCriteria.Reset();
            ApplyFiltersAndSorting();
        });

        ResetSortingCommand = ReactiveCommand.Create(() =>
        {
            SortCriteria.Reset();
            ApplyFiltersAndSorting();
        });
    }

    private void ApplyFiltersAndSorting()
    {
        PullRequests.Clear();
        
        // Apply enhanced filtering and sorting
        var filtered = _filterSortService.ApplyFiltersAndSorting(_allPullRequests, FilterCriteria, SortCriteria);
        
        foreach (var pr in filtered)
        {
            PullRequests.Add(pr);
        }
        
        // Notify summary statistics properties have changed
        this.RaisePropertyChanged(nameof(ActivePRCount));
        this.RaisePropertyChanged(nameof(MyReviewPendingCount));
        
        // Save preferences after applying filters
        SavePreferences();
    }

    private void UpdateAvailableOptions()
    {
        // Update available options for dropdowns
        var statuses = _filterSortService.GetAvailableStatuses(_allPullRequests);
        AvailableStatuses.Clear();
        foreach (var status in statuses) AvailableStatuses.Add(status);
        
        var votes = _filterSortService.GetAvailableReviewerVotes(_allPullRequests);
        AvailableReviewerVotes.Clear();
        foreach (var vote in votes) AvailableReviewerVotes.Add(vote);
        
        var creators = _filterSortService.GetAvailableCreators(_allPullRequests);
        AvailableCreators.Clear();
        foreach (var creator in creators) AvailableCreators.Add(creator);
        
        var reviewers = _filterSortService.GetAvailableReviewers(_allPullRequests);
        AvailableReviewers.Clear();
        foreach (var reviewer in reviewers) AvailableReviewers.Add(reviewer);
        
        var sourceBranches = _filterSortService.GetAvailableBranches(_allPullRequests, true);
        AvailableSourceBranches.Clear();
        foreach (var branch in sourceBranches) AvailableSourceBranches.Add(branch);
        
        var targetBranches = _filterSortService.GetAvailableBranches(_allPullRequests, false);
        AvailableTargetBranches.Clear();
        foreach (var branch in targetBranches) AvailableTargetBranches.Add(branch);
    }

    private async void LoadPreferences()
    {
        if (FilterSortPreferencesStorage.TryLoad(out var preferences) && preferences != null)
        {
            _preferences = preferences;
            
            // Apply loaded preferences
            FilterCriteria.FromData(_preferences.FilterCriteria);
            SortCriteria.FromData(_preferences.SortCriteria);
            
            // Load saved views
            SavedFilterViews.Clear();
            foreach (var view in _preferences.SavedViews)
            {
                SavedFilterViews.Add(view);
            }
        }
    }

    private void SavePreferences()
    {
        _preferences.FilterCriteria = FilterCriteria.ToData();
        _preferences.SortCriteria = SortCriteria.ToData();
        _preferences.SavedViews = SavedFilterViews.ToList();
        
        FilterSortPreferencesStorage.Save(_preferences);
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

}
