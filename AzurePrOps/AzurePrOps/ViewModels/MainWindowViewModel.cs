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
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Diagnostics;
using AzurePrOps.Views;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;
using System.Linq;

namespace AzurePrOps.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<MainWindowViewModel>();
    private readonly AzureDevOpsClient _client = new();
    private readonly IPullRequestService _pullRequestService;
    private readonly Models.ConnectionSettings _settings;
    private readonly PullRequestFilteringSortingService _filterSortService = new();
    private IReadOnlyList<string> _userGroupMemberships = new List<string>();

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

    // Group filtering
    public ObservableCollection<string> AvailableGroups { get; } = new();
    private GroupSettings _groupSettings = new(new List<string>(), new List<string>(), DateTime.MinValue);
    
    private bool _enableGroupFiltering = false;
    public bool EnableGroupFiltering
    {
        get => _enableGroupFiltering;
        set
        {
            this.RaiseAndSetIfChanged(ref _enableGroupFiltering, value);
            ApplyFiltersAndSorting();
        }
    }

    private string _selectedGroupsText = "All Groups";
    public string SelectedGroupsText
    {
        get => _selectedGroupsText;
        set => this.RaiseAndSetIfChanged(ref _selectedGroupsText, value);
    }

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

    // Commands
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
    
    // Group filtering commands
    public ReactiveCommand<Unit, Unit> SelectGroupsCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearGroupSelectionCommand { get; }
    
    // Groups Without Vote filtering commands
    public ReactiveCommand<Unit, Unit> SelectGroupsWithoutVoteCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearGroupsWithoutVoteSelectionCommand { get; }

    private bool _enableGroupsWithoutVoteFilter = false;
    public bool EnableGroupsWithoutVoteFilter
    {
        get => _enableGroupsWithoutVoteFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _enableGroupsWithoutVoteFilter, value);
            FilterCriteria.EnableGroupsWithoutVoteFilter = value;
            ApplyFiltersAndSorting();
        }
    }

    private string _selectedGroupsWithoutVoteText = "No Groups Selected";
    public string SelectedGroupsWithoutVoteText
    {
        get => _selectedGroupsWithoutVoteText;
        set => this.RaiseAndSetIfChanged(ref _selectedGroupsWithoutVoteText, value);
    }
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

    private async Task LoadAndCacheGroupsAsync()
    {
        try
        {
            // Load cached group settings
            _groupSettings = await GroupSettingsStorage.LoadAsync();

            // Get pull requests to extract groups from
            var prs = await _client.GetPullRequestsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                _settings.PersonalAccessToken);

            // Extract available groups from pull requests
            var allGroups = new HashSet<string>();
            foreach (var pr in prs)
            {
                var groups = pr.Reviewers.Where(r => r.IsGroup).Select(r => r.DisplayName);
                foreach (var group in groups)
                {
                    allGroups.Add(group);
                }
            }

            // Update group settings if cache is expired or groups have changed
            var currentGroups = allGroups.OrderBy(g => g).ToList();
            if (_groupSettings.IsExpired || !_groupSettings.AvailableGroups.SequenceEqual(currentGroups))
            {
                _groupSettings = _groupSettings with
                {
                    AvailableGroups = currentGroups,
                    LastUpdated = DateTime.Now
                };
                await GroupSettingsStorage.SaveAsync(_groupSettings);
            }

            // Update UI collections
            AvailableGroups.Clear();
            foreach (var group in currentGroups)
            {
                AvailableGroups.Add(group);
            }

            // Initialize group filtering settings from connection settings
            EnableGroupFiltering = _settings.EnableGroupFiltering;
            if (_settings.SelectedGroupsForFiltering.Any())
            {
                _groupSettings = _groupSettings with
                {
                    SelectedGroups = _settings.SelectedGroupsForFiltering.ToList()
                };
            }
            UpdateSelectedGroupsText();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load and cache groups");
        }
    }

    private void UpdateSelectedGroupsText()
    {
        if (!_groupSettings.SelectedGroups.Any())
        {
            SelectedGroupsText = "All Groups";
        }
        else
        {
            SelectedGroupsText = string.Join(", ", _groupSettings.SelectedGroups);
        }
    }

    private void UpdateSelectedGroupsWithoutVoteText()
    {
        if (!FilterCriteria.SelectedGroupsWithoutVote.Any())
        {
            SelectedGroupsWithoutVoteText = "No Groups Selected";
        }
        else
        {
            SelectedGroupsWithoutVoteText = string.Join(", ", FilterCriteria.SelectedGroupsWithoutVote);
        }
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

                // Fetch user group memberships for filtering
                // TODO: Re-enable when performance is improved - currently takes too long
                // _userGroupMemberships = await _client.GetUserGroupMembershipsAsync(
                //     _settings.Organization,
                //     _settings.PersonalAccessToken);
                
                // Temporarily disabled due to performance issues - return empty list
                _userGroupMemberships = new List<string>();
                
                _logger.LogInformation("[DEBUG_LOG] Fetched user group memberships: {GroupCount} groups: {Groups}", 
                    _userGroupMemberships?.Count ?? 0, 
                    _userGroupMemberships != null ? string.Join(", ", _userGroupMemberships) : "null");

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

                // Load and cache groups from pull requests
                await LoadAndCacheGroupsAsync();
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

            // Since MarkAsDraftAsync doesn't exist, use SetPullRequestVoteAsync or similar approach
            // This is a placeholder - you may need to implement the actual draft marking logic
            await _client.SetPullRequestVoteAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId ?? string.Empty,
                0, // No vote for draft
                _settings.PersonalAccessToken);
        }, hasSelection);

        MarkReadyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            // Since MarkAsReadyAsync doesn't exist, use SetPullRequestVoteAsync or similar approach
            // This is a placeholder - you may need to implement the actual ready marking logic
            await _client.SetPullRequestVoteAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId ?? string.Empty,
                0, // No vote for ready
                _settings.PersonalAccessToken);
        }, hasSelection);

        CompleteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            // Use a basic completion approach - you may need to implement CompletePullRequestAsync
            // For now, this is a placeholder that does nothing
            await Task.CompletedTask;
        }, hasSelection);

        AbandonCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            // Use a basic abandon approach - you may need to implement AbandonPullRequestAsync  
            // For now, this is a placeholder that does nothing
            await Task.CompletedTask;
        }, hasSelection);

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
        }, hasSelection);

        ViewDetailsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            IsLoadingDiffs = true;
            try
            {
                // Create a simple details view since PullRequestDetailsViewModel might not exist
                // This is a placeholder - you may need to implement the details window
                await Task.Delay(100); // Simulate loading
                
                // Open in browser as fallback
                var url = $"https://dev.azure.com/{_settings.Organization}/{_settings.Project}/_git/{_settings.Repository}/pullrequest/{SelectedPullRequest.Id}";
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch
                {
                    // Fallback approaches for different platforms
                    try
                    {
                        Process.Start("cmd", $"/c start {url}");
                    }
                    catch
                    {
                        Process.Start("xdg-open", url);
                    }
                }
            }
            finally
            {
                IsLoadingDiffs = false;
            }
        }, hasSelection);

        OpenInBrowserCommand = ReactiveCommand.CreateFromTask<PullRequestInfo?>(async (pr) =>
        {
            if (pr == null)
                return;

            var url = $"https://dev.azure.com/{_settings.Organization}/{_settings.Project}/_git/{_settings.Repository}/pullrequest/{pr.Id}";
            
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Fallback for different platforms
                try
                {
                    Process.Start("cmd", $"/c start {url}");
                }
                catch
                {
                    // Final fallback
                    Process.Start("xdg-open", url);
                }
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

            FilterViews.Add(view);
            FilterViewStorage.Save(FilterViews.ToArray());
            NewViewName = string.Empty;
        });

        OpenSettingsCommand = ReactiveCommand.Create(() =>
        {
            var settingsWindow = new SettingsWindow();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                settingsWindow.ShowDialog(desktop.MainWindow);
            }
        });

        ResetFiltersCommand = ReactiveCommand.Create(() =>
        {
            FilterCriteria.Reset();
            TitleFilter = string.Empty;
            CreatorFilter = string.Empty;
            SourceBranchFilter = string.Empty;
            TargetBranchFilter = string.Empty;
            StatusFilter = "All";
            SelectedWorkflowPreset = "All Pull Requests";
            EnableGroupFiltering = false;
            _groupSettings = _groupSettings with { SelectedGroups = new List<string>() };
            UpdateSelectedGroupsText();
        });

        ResetSortingCommand = ReactiveCommand.Create(() =>
        {
            SortCriteria.Reset();
            SelectedSortPreset = "Newest First";
        });

        SelectGroupsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var groupSelectionViewModel = new GroupSelectionViewModel(_groupSettings.AvailableGroups, _groupSettings.SelectedGroups);
            var groupSelectionWindow = new GroupSelectionWindow
            {
                DataContext = groupSelectionViewModel
            };

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var result = await groupSelectionWindow.ShowDialog<bool?>(desktop.MainWindow);
                if (result == true)
                {
                    _groupSettings = _groupSettings with
                    {
                        SelectedGroups = groupSelectionViewModel.SelectedGroups.ToList()
                    };
                    await GroupSettingsStorage.SaveAsync(_groupSettings);
                    UpdateSelectedGroupsText();
                    ApplyFiltersAndSorting();
                }
            }
        });

        ClearGroupSelectionCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            _groupSettings = _groupSettings with { SelectedGroups = new List<string>() };
            await GroupSettingsStorage.SaveAsync(_groupSettings);
            UpdateSelectedGroupsText();
            ApplyFiltersAndSorting();
        });

        SelectGroupsWithoutVoteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var groupSelectionViewModel = new GroupSelectionViewModel(_groupSettings.AvailableGroups, FilterCriteria.SelectedGroupsWithoutVote);
            var groupSelectionWindow = new GroupSelectionWindow
            {
                DataContext = groupSelectionViewModel
            };

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var result = await groupSelectionWindow.ShowDialog<bool?>(desktop.MainWindow);
                if (result == true)
                {
                    FilterCriteria.SelectedGroupsWithoutVote.Clear();
                    FilterCriteria.SelectedGroupsWithoutVote.AddRange(groupSelectionViewModel.SelectedGroups);
                    UpdateSelectedGroupsWithoutVoteText();
                    ApplyFiltersAndSorting();
                }
            }
        });

        ClearGroupsWithoutVoteSelectionCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            FilterCriteria.SelectedGroupsWithoutVote.Clear();
            UpdateSelectedGroupsWithoutVoteText();
            ApplyFiltersAndSorting();
        });

        // Initialize with current user as owner for group filtering
        Task.Run(async () =>
        {
            try
            {
                await LoadAndCacheGroupsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize group filtering");
            }
        });
    }

    private void LoadPreferences()
    {
        // Load saved preferences for filters and sorting
        // Implementation would depend on your preferences storage mechanism
    }

    private void UpdateFilterOptions()
    {
        // Update available filter options based on current PRs
        var statuses = _allPullRequests.Select(pr => pr.Status).Distinct().OrderBy(s => s);
        AvailableStatuses.Clear();
        AvailableStatuses.Add("All");
        foreach (var status in statuses)
            AvailableStatuses.Add(status);

        var creators = _allPullRequests.Select(pr => pr.Creator).Distinct().OrderBy(c => c);
        AvailableCreators.Clear();
        foreach (var creator in creators)
            AvailableCreators.Add(creator);

        var reviewers = _allPullRequests.SelectMany(pr => pr.Reviewers.Select(r => r.DisplayName))
            .Distinct().OrderBy(r => r);
        AvailableReviewers.Clear();
        foreach (var reviewer in reviewers)
            AvailableReviewers.Add(reviewer);

        var sourceBranches = _allPullRequests.Select(pr => pr.SourceBranch).Distinct().OrderBy(b => b);
        AvailableSourceBranches.Clear();
        foreach (var branch in sourceBranches)
            AvailableSourceBranches.Add(branch);

        var targetBranches = _allPullRequests.Select(pr => pr.TargetBranch).Distinct().OrderBy(b => b);
        AvailableTargetBranches.Clear();
        foreach (var branch in targetBranches)
            AvailableTargetBranches.Add(branch);
    }

    private void UpdateAvailableOptions()
    {
        // Update dropdown options for autocomplete
    }

    private void ApplyFiltersAndSorting()
    {
        var filteredPRs = _allPullRequests.AsEnumerable();

        // Apply group filtering if enabled
        if (EnableGroupFiltering && _groupSettings.SelectedGroups.Any())
        {
            filteredPRs = filteredPRs.Where(pr =>
                pr.Reviewers.Any(reviewer =>
                    reviewer.IsGroup &&
                    _groupSettings.SelectedGroups.Contains(reviewer.DisplayName)));
        }

        var result = _filterSortService.ApplyFiltersAndSorting(filteredPRs, FilterCriteria, SortCriteria, _userGroupMemberships);

        PullRequests.Clear();
        foreach (var pr in result)
        {
            PullRequests.Add(pr);
        }

        // Update summary statistics
        this.RaisePropertyChanged(nameof(ActivePRCount));
        this.RaisePropertyChanged(nameof(MyReviewPendingCount));
    }
}
