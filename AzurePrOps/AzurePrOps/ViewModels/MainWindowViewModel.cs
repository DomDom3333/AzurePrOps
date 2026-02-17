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
using System.Linq;
using AzurePrOps.Infrastructure;
using AzurePrOps.ViewModels.Filters;

namespace AzurePrOps.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly AzureDevOpsClient _client = new();
    private readonly IPullRequestService _pullRequestService;
    private Models.ConnectionSettings _settings;
    private readonly AuthenticationService _authService;
    private IReadOnlyList<string> _userGroupMemberships = new List<string>();

    public ObservableCollection<PullRequestInfo> PullRequests { get; } = new();
    private readonly ObservableCollection<PullRequestInfo> _allPullRequests = new();

    // Modular filter system
    private readonly FilterOrchestrator _filterOrchestrator;
    
    // Legacy collections for backward compatibility (during transition)
    public ObservableCollection<FilterView> FilterViews { get; } = new();
    public ObservableCollection<SavedFilterView> SavedFilterViews { get; } = new();

    // Preferences for saved views
    private FilterSortPreferences _preferences = new();

    // Expose filter components for UI binding
    public FilterOrchestrator FilterOrchestrator => _filterOrchestrator;
    public AzurePrOps.ViewModels.Filters.FilterPanelViewModel FilterPanel => _filterOrchestrator.FilterPanel;
    public FilterState FilterState => _filterOrchestrator.FilterState;

    // Essential filter state display properties for UI binding
    public string CurrentFilterSource => FilterState.CurrentFilterSource;
    public string ActiveFiltersSummary => FilterOrchestrator.GetFilterSummary();
    public bool HasActiveFilters => FilterOrchestrator.HasActiveFilters();
    public string FilterStatusText => FilterState.FilterStatusText;
    public string ActiveFiltersCount => HasActiveFilters ? "Active" : "None";
    public string FilterSummary => ActiveFiltersSummary;

    // UI binding properties that delegate to FilterOrchestrator
    public string GlobalSearchText
    {
        get => FilterState.Criteria.GlobalSearchText;
        set
        {
            if (FilterState.Criteria.GlobalSearchText != value)
            {
                FilterState.Criteria.GlobalSearchText = value;
                this.RaisePropertyChanged();
            }
        }
    }

    // Quick filter boolean properties
    public bool ShowMyPullRequestsOnly
    {
        get => FilterState.MyPullRequestsOnly;
        set
        {
            if (FilterState.MyPullRequestsOnly != value)
            {
                FilterState.MyPullRequestsOnly = value;  // Use FilterState property to trigger events
                this.RaisePropertyChanged();
            }
        }
    }

    public bool ShowNeedsMyReviewOnly
    {
        get => FilterState.NeedsMyReviewOnly;
        set
        {
            if (FilterState.NeedsMyReviewOnly != value)
            {
                FilterState.NeedsMyReviewOnly = value;  // Use FilterState property to trigger events
                this.RaisePropertyChanged();
            }
        }
    }

    public bool ShowAssignedToMeOnly
    {
        get => FilterState.AssignedToMeOnly;
        set
        {
            if (FilterState.AssignedToMeOnly != value)
            {
                FilterState.AssignedToMeOnly = value;  // Use FilterState property to trigger events
                this.RaisePropertyChanged();
            }
        }
    }

    public bool ExcludeMyPullRequests
    {
        get => FilterState.ExcludeMyPullRequests;
        set
        {
            if (FilterState.ExcludeMyPullRequests != value)
            {
                FilterState.ExcludeMyPullRequests = value;  // Use FilterState property to trigger events
                this.RaisePropertyChanged();
            }
        }
    }

    // Filter dropdown properties
    public string StatusFilter
    {
        get => FilterState.Criteria.SelectedStatuses.FirstOrDefault() ?? "All";
        set
        {
            FilterState.Criteria.SelectedStatuses.Clear();
            if (!string.IsNullOrEmpty(value) && value != "All")
            {
                FilterState.Criteria.SelectedStatuses.Add(value);
            }
            this.RaisePropertyChanged();
        }
    }

    public string ReviewerVoteFilter
    {
        get => FilterState.Criteria.SelectedReviewerVotes.FirstOrDefault() ?? "All";
        set
        {
            FilterState.Criteria.SelectedReviewerVotes.Clear();
            if (!string.IsNullOrEmpty(value) && value != "All")
            {
                FilterState.Criteria.SelectedReviewerVotes.Add(value);
            }
            this.RaisePropertyChanged();
        }
    }

    public string DraftFilter
    {
        get => FilterState.Criteria.IsDraft switch
        {
            true => "Drafts Only",
            false => "Non-Drafts Only",
            _ => "All"
        };
        set
        {
            FilterState.Criteria.IsDraft = value switch
            {
                "Drafts Only" => true,
                "Non-Drafts Only" => false,
                _ => null
            };
            this.RaisePropertyChanged();
        }
    }

    // Date filter properties
    public DateTimeOffset? CreatedAfter
    {
        get => FilterState.Criteria.CreatedAfter;
        set
        {
            if (FilterState.Criteria.CreatedAfter != value)
            {
                FilterState.Criteria.CreatedAfter = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public DateTimeOffset? CreatedBefore
    {
        get => FilterState.Criteria.CreatedBefore;
        set
        {
            if (FilterState.Criteria.CreatedBefore != value)
            {
                FilterState.Criteria.CreatedBefore = value;
                this.RaisePropertyChanged();
            }
        }
    }

    // Text filter properties
    public string CreatorFilter
    {
        get => FilterState.Criteria.CreatorFilter;
        set
        {
            if (FilterState.Criteria.CreatorFilter != value)
            {
                FilterState.Criteria.CreatorFilter = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public string ReviewerFilter
    {
        get => FilterState.Criteria.ReviewerFilter;
        set
        {
            if (FilterState.Criteria.ReviewerFilter != value)
            {
                FilterState.Criteria.ReviewerFilter = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public string TitleFilter
    {
        get => FilterState.Criteria.TitleFilter;
        set
        {
            if (FilterState.Criteria.TitleFilter != value)
            {
                FilterState.Criteria.TitleFilter = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public string SourceBranchFilter
    {
        get => FilterState.Criteria.SourceBranchFilter;
        set
        {
            if (FilterState.Criteria.SourceBranchFilter != value)
            {
                FilterState.Criteria.SourceBranchFilter = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public string TargetBranchFilter
    {
        get => FilterState.Criteria.TargetBranchFilter;
        set
        {
            if (FilterState.Criteria.TargetBranchFilter != value)
            {
                FilterState.Criteria.TargetBranchFilter = value;
                this.RaisePropertyChanged();
            }
        }
    }

    // Groups without vote filter
    public bool EnableGroupsWithoutVoteFilter
    {
        get => FilterState.EnableGroupsWithoutVoteFilter;
        set
        {
            if (FilterState.EnableGroupsWithoutVoteFilter != value)
            {
                FilterState.EnableGroupsWithoutVoteFilter = value;
                this.RaisePropertyChanged();
            }
        }
    }

    // Available options for dropdowns - delegate to FilterOrchestrator
    public ObservableCollection<string> AvailableStatuses => FilterOrchestrator.FilterPanel.AvailableStatuses;
    public ObservableCollection<string> AvailableReviewerVotes => FilterOrchestrator.FilterPanel.AvailableReviewerVotes;
    public ObservableCollection<string> DraftFilterOptions => FilterOrchestrator.FilterPanel.DraftFilterOptions;
    public ObservableCollection<string> AvailableCreators => FilterOrchestrator.FilterPanel.AvailableCreators;
    public ObservableCollection<string> AvailableReviewers => FilterOrchestrator.FilterPanel.AvailableReviewers;
    public ObservableCollection<string> AvailableSourceBranches => FilterOrchestrator.FilterPanel.AvailableSourceBranches;
    public ObservableCollection<string> AvailableTargetBranches => FilterOrchestrator.FilterPanel.AvailableTargetBranches;

    // Workflow and sort presets
    public ObservableCollection<string> WorkflowPresets { get; } = new()
    {
        "All Pull Requests", "Team Lead Overview", "My Pull Requests", "Need My Review",
        "Approved & Ready for Testing", "Waiting for Author Response", "Recently Updated", "High Priority"
    };

    public ObservableCollection<string> SortPresets { get; } = new()
    {
        "Most Recent", "Oldest First", "Title A-Z", "Title Z-A", "Creator A-Z", "Creator Z-A",
        "Status Priority", "Review Priority", "High Activity", "Needs Attention"
    };

    public ObservableCollection<string> DateRangePresets { get; } = new()
    {
        "All Time", "Today", "Yesterday", "This Week", "Last Week", "This Month", "Last Month", "Last 7 Days",
        "Last 30 Days"
    };

    public string SelectedWorkflowPresetTooltip => GetWorkflowPresetTooltip(SelectedWorkflowPreset);
    public string SelectedSortPresetTooltip => Models.FilteringAndSorting.SortCriteria.GetSortPresetTooltip(SelectedSortPreset);

    // Preset selection properties (UI binding only)
    private string _selectedDateRangePreset = "All Time";
    public string SelectedDateRangePreset
    {
        get => _selectedDateRangePreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDateRangePreset, value);
            if (!_isApplyingFilterView) ApplyDateRangePreset(value);
        }
    }

    private string _selectedSortPreset = "Most Recent";
    public string SelectedSortPreset
    {
        get => _selectedSortPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSortPreset, value);
            if (!_isApplyingFilterView)
            {
                FilterState.SortCriteria.ApplyPreset(value);
                ApplyFiltersAndSorting();
            }
        }
    }

    private string _selectedWorkflowPreset = "All Pull Requests";
    public string SelectedWorkflowPreset
    {
        get => _selectedWorkflowPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedWorkflowPreset, value);
            if (!_isApplyingFilterView) ApplyWorkflowPreset(value);
        }
    }
    
    /// <summary>
    /// Initialize default dropdown selections to ensure UI shows proper values
    /// </summary>
    public void InitializeDropdownDefaults()
    {
        // Force property change notifications for preset dropdowns
        this.RaisePropertyChanged(nameof(SelectedWorkflowPreset));
        this.RaisePropertyChanged(nameof(SelectedSortPreset)); 
        this.RaisePropertyChanged(nameof(SelectedDateRangePreset));
        
        // Ensure filter dropdowns also show their default values
        this.RaisePropertyChanged(nameof(StatusFilter));
        this.RaisePropertyChanged(nameof(ReviewerVoteFilter));
        this.RaisePropertyChanged(nameof(DraftFilter));
    }

    // Group filtering UI properties
    private GroupSettings _groupSettings = new(new List<string>(), new List<string>(), DateTime.MinValue);
    private bool _enableGroupFiltering = false;

    public bool EnableGroupFiltering
    {
        get => _enableGroupFiltering;
        set
        {
            this.RaiseAndSetIfChanged(ref _enableGroupFiltering, value);
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private string _selectedGroupsText = "All Groups";
    public string SelectedGroupsText
    {
        get => _selectedGroupsText;
        set => this.RaiseAndSetIfChanged(ref _selectedGroupsText, value);
    }

    private string _selectedGroupsWithoutVoteText = "No Groups Selected";
    public string SelectedGroupsWithoutVoteText
    {
        get => _selectedGroupsWithoutVoteText;
        set => this.RaiseAndSetIfChanged(ref _selectedGroupsWithoutVoteText, value);
    }

    public ObservableCollection<string> AvailableGroups { get; } = new();

    // Filter view selection properties
    private FilterView? _selectedFilterView;
    public FilterView? SelectedFilterView
    {
        get => _selectedFilterView;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedFilterView, value);
            if (value != null)
            {
                var matchingSavedView = SavedFilterViews.FirstOrDefault(sv => sv.Name == value.Name);
                if (matchingSavedView != null)
                {
                    ApplySavedFilterView(matchingSavedView);
                }
                else
                {
                    _isApplyingFilterView = true;
                    ResetFiltersToDefaults();
                    FilterState.Criteria.TitleFilter = value.Title;
                    FilterState.Criteria.CreatorFilter = value.Creator;
                    FilterState.Criteria.SourceBranchFilter = value.SourceBranch;
                    FilterState.Criteria.TargetBranchFilter = value.TargetBranch;
                    
                    if (!string.IsNullOrWhiteSpace(value.Status) && value.Status != "All")
                    {
                        FilterState.Criteria.SelectedStatuses.Clear();
                        FilterState.Criteria.SelectedStatuses.Add(value.Status);
                    }

                    _isApplyingFilterView = false;
                    ApplyFiltersAndSorting();
                }
            }
        }
    }

    private SavedFilterView? _selectedSavedFilterView;
    public SavedFilterView? SelectedSavedFilterView
    {
        get => _selectedSavedFilterView;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSavedFilterView, value);
            if (value != null)
            {
                ApplySavedFilterView(value);
            }
        }
    }

    private string _newViewName = string.Empty;
    public string NewViewName
    {
        get => _newViewName;
        set => this.RaiseAndSetIfChanged(ref _newViewName, value);
    }

    // Non-filter UI properties
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
    public int ActivePRCount =>
        PullRequests.Count(pr => pr.Status.Equals("Active", StringComparison.OrdinalIgnoreCase));

    public int MyReviewPendingCount => PullRequests.Count(pr =>
        pr.Reviewers.Any(r => r.Id.Equals(_settings.ReviewerId, StringComparison.OrdinalIgnoreCase) &&
                              (r.Vote.Equals("No vote", StringComparison.OrdinalIgnoreCase) ||
                               string.IsNullOrWhiteSpace(r.Vote))));

    public bool LifecycleActionsEnabled => FeatureFlagManager.LifecycleActionsEnabled;

    // Commands
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> ApproveCommand { get; }
    public ReactiveCommand<Unit, Unit> PostCommentCommand { get; }
    public ReactiveCommand<PullRequestInfo?, Unit> ViewDetailsCommand { get; }
    public ReactiveCommand<PullRequestInfo?, Unit> OpenInBrowserCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveViewCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCurrentFiltersCommand { get; }
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
    public ReactiveCommand<Unit, Unit> ClearAllFiltersCommand { get; }

    // Group filtering commands
    public ReactiveCommand<Unit, Unit> SelectGroupsCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearGroupSelectionCommand { get; }

    // Groups Without Vote filtering commands
    public ReactiveCommand<Unit, Unit> SelectGroupsWithoutVoteCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearGroupsWithoutVoteSelectionCommand { get; }

    // Quick action commands
    public ReactiveCommand<Unit, Unit> ApplyQuickFilterMyPRsCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyQuickFilterNeedsReviewCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyQuickFilterRecentCommand { get; }

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

            // Validate PAT before making API calls
            if (!await _authService.ValidateAndRedirectIfNeededAsync())
            {
                return; // User was redirected to login
            }

            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrEmpty(personalAccessToken))
            {
                _authService.RedirectToLogin();
                return;
            }

            // Get pull requests to extract groups from
            var prs = await _client.GetPullRequestsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                personalAccessToken);

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
        catch (UnauthorizedAccessException ex)
        {
            _authService.HandlePatValidationError(ex, "LoadAndCacheGroupsAsync");
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
        {
            _authService.HandlePatValidationError(ex, "LoadAndCacheGroupsAsync");
        }
        catch (Exception)
        {
            // Log error (removed excessive logging)
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
        if (!FilterState.SelectedGroupsWithoutVote.Any())
        {
            SelectedGroupsWithoutVoteText = "No Groups Selected";
        }
        else
        {
            SelectedGroupsWithoutVoteText = string.Join(", ", FilterState.SelectedGroupsWithoutVote);
        }
    }

    public MainWindowViewModel(Models.ConnectionSettings settings)
    {
        _settings = settings;

        // Initialize modular filter system FIRST
        _filterOrchestrator = new FilterOrchestrator(new PullRequestFilteringSortingService());

        // Initialize the filter orchestrator with user information
        var userRole = _settings.UserRole;
        _filterOrchestrator.Initialize(
            _settings.ReviewerId ?? string.Empty, 
            _settings.UserDisplayName ?? string.Empty, 
            userRole);

        // Subscribe to filter changes to automatically apply filtering
        _filterOrchestrator.FiltersChanged += (_, criteria) => ApplyFiltersAndSorting();

        // Get the AuthenticationService from the service registry
        _authService = ServiceRegistry.Resolve<AuthenticationService>() ?? new AuthenticationService();

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

        // Initialize user information - will be updated automatically when app starts
        _filterOrchestrator.FilterState.Criteria.CurrentUserId = _settings.ReviewerId ?? string.Empty;
        _filterOrchestrator.FilterState.Criteria.UserDisplayName = _settings.UserDisplayName ?? string.Empty;

        // Filter changes are handled by FilterOrchestrator.FiltersChanged event subscription above
        // No need for direct property change handlers as they create redundant subscriptions

        // Add error handling mechanism
        _client.SetErrorHandler(message => _ = ShowErrorMessage(message));

        // Automatically retrieve current user information when app starts
        _ = InitializeUserInformationAsync();

        var hasSelection = this.WhenAnyValue(x => x.SelectedPullRequest)
            .Select(pr => pr != null);

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                // Validate PAT before making API calls
                if (!await _authService.ValidateAndRedirectIfNeededAsync())
                {
                    return; // User was redirected to login
                }

                var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
                if (string.IsNullOrEmpty(personalAccessToken))
                {
                    _authService.RedirectToLogin();
                    return;
                }

                var prs = await _client.GetPullRequestsAsync(
                    _settings.Organization,
                    _settings.Project,
                    _settings.Repository,
                    personalAccessToken);

                // Fetch user group memberships for filtering
                // TODO: Re-enable when performance is improved - currently takes too long
                // _userGroupMemberships = await _client.GetUserGroupMembershipsAsync(
                //     _settings.Organization,
                //     personalAccessToken);

                // Temporarily disabled due to performance issues - return empty list
                _userGroupMemberships = new List<string>();

                _allPullRequests.Clear();

                // Clear the selected pull request when refreshing
                SelectedPullRequest = null;

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

            try
            {
                // Validate PAT before making API calls
                if (!await _authService.ValidateAndRedirectIfNeededAsync())
                {
                    return; // User was redirected to login
                }

                var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
                if (string.IsNullOrEmpty(personalAccessToken))
                {
                    _authService.RedirectToLogin();
                    return;
                }

                await _client.ApprovePullRequestAsync(
                    _settings.Organization,
                    _settings.Project,
                    _settings.Repository,
                    SelectedPullRequest.Id,
                    _settings.ReviewerId ?? throw new InvalidOperationException("Reviewer ID is required"),
                    personalAccessToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                _authService.HandlePatValidationError(ex, "ApproveCommand");
            }
            catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("401") ||
                                                                  ex.Message.Contains("403"))
            {
                _authService.HandlePatValidationError(ex, "ApproveCommand");
            }
            catch (Exception ex)
            {
                await ShowErrorMessage($"Failed to approve pull request: {ex.Message}");
            }
        }, hasSelection);

        ApproveWithSuggestionsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrEmpty(personalAccessToken))
            {
                await ShowErrorMessage("No Personal Access Token found. Please log in again.");
                return;
            }

            await _client.ApproveWithSuggestionsAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId ?? throw new InvalidOperationException("Reviewer ID is required"),
                personalAccessToken);
        }, hasSelection);

        WaitForAuthorCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrEmpty(personalAccessToken))
            {
                await ShowErrorMessage("No Personal Access Token found. Please log in again.");
                return;
            }

            await _client.SetPullRequestVoteAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId,
                -5,
                personalAccessToken);
        }, hasSelection);

        RejectCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrEmpty(personalAccessToken))
            {
                await ShowErrorMessage("No Personal Access Token found. Please log in again.");
                return;
            }

            await _client.RejectPullRequestAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId,
                personalAccessToken);
        }, hasSelection);

        MarkDraftCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrEmpty(personalAccessToken))
            {
                await ShowErrorMessage("No Personal Access Token found. Please log in again.");
                return;
            }

            // Since MarkAsDraftAsync doesn't exist, use SetPullRequestVoteAsync or similar approach
            // This is a placeholder - you may need to implement the actual draft marking logic
            await _client.SetPullRequestVoteAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId ?? string.Empty,
                0, // No vote for draft
                personalAccessToken);
        }, hasSelection);

        MarkReadyCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPullRequest == null)
                return;

            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrEmpty(personalAccessToken))
            {
                await ShowErrorMessage("No Personal Access Token found. Please log in again.");
                return;
            }

            // Since MarkAsReadyAsync doesn't exist, use SetPullRequestVoteAsync or similar approach
            // This is a placeholder - you may need to implement the actual ready marking logic
            await _client.SetPullRequestVoteAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                _settings.ReviewerId ?? string.Empty,
                0, // No vote for ready
                personalAccessToken);
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

            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrEmpty(personalAccessToken))
            {
                await ShowErrorMessage("No Personal Access Token found. Please log in again.");
                return;
            }

            await _client.PostPullRequestCommentAsync(
                _settings.Organization,
                _settings.Project,
                _settings.Repository,
                SelectedPullRequest.Id,
                NewCommentText,
                personalAccessToken);

            NewCommentText = string.Empty;
        }, hasSelection);

        ViewDetailsCommand = ReactiveCommand.CreateFromTask<PullRequestInfo?>(async (pr) =>
        {
            if (pr == null)
                return;

            IsLoadingDiffs = true;
            try
            {
                // Create and show the PullRequestDetailsWindow
                var detailsViewModel = new PullRequestDetailsWindowViewModel(
                    pr,
                    _pullRequestService,
                    _settings);

                var detailsWindow = new PullRequestDetailsWindow
                {
                    DataContext = detailsViewModel
                };

                // Show the window
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    detailsWindow.Show(desktop.MainWindow);
                }
                else
                {
                    detailsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessage($"Failed to open details window: {ex.Message}");
            }
            finally
            {
                IsLoadingDiffs = false;
            }
        });

        OpenInBrowserCommand = ReactiveCommand.Create<PullRequestInfo?>((pr) =>
        {
            if (pr == null)
                return;

            var url =
                $"https://dev.azure.com/{_settings.Organization}/{_settings.Project}/_git/{_settings.Repository}/pullrequest/{pr.Id}";

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

            var view = new FilterView
            {
                Name = NewViewName,
                Title = FilterState.Criteria.TitleFilter,
                Creator = FilterState.Criteria.CreatorFilter,
                SourceBranch = FilterState.Criteria.SourceBranchFilter,
                TargetBranch = FilterState.Criteria.TargetBranchFilter,
                Status = FilterState.Criteria.SelectedStatuses.FirstOrDefault() ?? string.Empty
            };

            FilterViews.Add(view);
            FilterViewStorage.Save(FilterViews.ToArray());
            NewViewName = string.Empty;
        });

        SaveCurrentFiltersCommand = ReactiveCommand.Create(() =>
        {
            // Generate a unique name for the saved filter view
            var defaultName = $"Filter View {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
            var viewName = NewViewName.Trim().Length > 0 ? NewViewName.Trim() : defaultName;

            // Create the saved filter view object using current FilterState
            var savedView = new SavedFilterView
            {
                Name = viewName,
                FilterCriteria = new FilterCriteria
                {
                    TitleFilter = FilterState.Criteria.TitleFilter,
                    CreatorFilter = FilterState.Criteria.CreatorFilter,
                    SourceBranchFilter = FilterState.Criteria.SourceBranchFilter,
                    TargetBranchFilter = FilterState.Criteria.TargetBranchFilter,
                    ReviewerFilter = FilterState.Criteria.ReviewerFilter,
                    GlobalSearchText = FilterState.Criteria.GlobalSearchText,
                    MyPullRequestsOnly = FilterState.Criteria.MyPullRequestsOnly,
                    AssignedToMeOnly = FilterState.Criteria.AssignedToMeOnly,
                    NeedsMyReviewOnly = FilterState.Criteria.NeedsMyReviewOnly,
                    ExcludeMyPullRequests = FilterState.Criteria.ExcludeMyPullRequests,
                    IsDraft = FilterState.Criteria.IsDraft,
                    SelectedReviewerVotes = new List<string>(FilterState.Criteria.SelectedReviewerVotes),
                    SelectedStatuses = new List<string>(FilterState.Criteria.SelectedStatuses),
                    CreatedAfter = FilterState.Criteria.CreatedAfter,
                    CreatedBefore = FilterState.Criteria.CreatedBefore,
                    EnableGroupsWithoutVoteFilter = FilterState.EnableGroupsWithoutVoteFilter,
                    SelectedGroupsWithoutVote = new List<string>(FilterState.SelectedGroupsWithoutVote)
                },
                SortCriteria = new SortCriteria
                {
                    CurrentPreset = FilterState.SortCriteria.CurrentPreset
                },
                LastUsed = DateTime.Now
            };

            // Add to preferences and save
            _preferences.SavedViews.Add(savedView);
            SavedFilterViews.Add(savedView);
            FilterSortPreferencesStorage.Save(_preferences);

            // Clear the new view name
            NewViewName = string.Empty;
        });

        OpenSettingsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var settingsViewModel = new SettingsWindowViewModel(_settings);
            var settingsWindow = new SettingsWindow
            {
                DataContext = settingsViewModel
            };
            settingsViewModel.DialogWindow = settingsWindow;

            // Load Azure DevOps data asynchronously
            try
            {
                await settingsViewModel.LoadAsync();
            }
            catch (Exception)
            {
                // If loading fails, the settings window will still open but show error messages
                // The ViewModel handles error display internally
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var result = await settingsWindow.ShowDialog<ConnectionSettings?>(desktop.MainWindow);

                // If settings were saved, refresh the main window
                if (result != null)
                {
                    // Store the old settings for comparison
                    var oldSettings = _settings;

                    // Update the current settings reference
                    _settings = result;

                    // Update the pull request service with new settings
                    if (_pullRequestService is AzureDevOpsPullRequestService azService)
                    {
                        azService.UseGitClient = _settings.UseGitDiff;
                    }

                    // Update user information in filter criteria
                    FilterState.Criteria.CurrentUserId = _settings.ReviewerId ?? string.Empty;
                    FilterState.Criteria.UserDisplayName = _settings.UserDisplayName ?? string.Empty;

                    // Check if connection details changed
                    bool connectionChanged = oldSettings.Organization != result.Organization ||
                                             oldSettings.Project != result.Project ||
                                             oldSettings.Repository != result.Repository;

                    // Always refresh the data after settings change to ensure UI reflects new preferences
                    // This covers cases like diff preferences, UI preferences, feature flags, etc.
                    try
                    {
                        await RefreshCommand.Execute();
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorMessage(
                            $"Settings saved successfully, but failed to refresh data: {ex.Message}");
                    }
                }
            }
        });

        ResetFiltersCommand = ReactiveCommand.Create(() =>
        {
            FilterState.Criteria.Reset();
            FilterState.SortCriteria.Reset();
            SelectedWorkflowPreset = "All Pull Requests";
            EnableGroupFiltering = false;
            _groupSettings = _groupSettings with { SelectedGroups = new List<string>() };
            UpdateSelectedGroupsText();
        });

        ResetSortingCommand = ReactiveCommand.Create(() =>
        {
            FilterState.SortCriteria.Reset();
            SelectedSortPreset = "Most Recent";
        });

        ClearAllFiltersCommand = ReactiveCommand.Create(() =>
        {
            // Reset all filter properties through FilterOrchestrator
            FilterState.Criteria.Reset();
            FilterState.SortCriteria.Reset();
            
            // Reset UI-specific properties
            SelectedDateRangePreset = "All Time";
            EnableGroupFiltering = false;
            
            // Clear selected groups
            _groupSettings = _groupSettings with { SelectedGroups = new List<string>() };
            FilterState.SelectedGroupsWithoutVote = new List<string>();

            // Update UI
            UpdateSelectedGroupsText();
            UpdateSelectedGroupsWithoutVoteText();
            ApplyFiltersAndSorting();
        });

        ApplyQuickFilterMyPRsCommand = ReactiveCommand.Create(() =>
        {
            ResetFiltersToDefaults();
            FilterState.Criteria.MyPullRequestsOnly = true;
            FilterState.Criteria.SelectedStatuses.Clear();
            FilterState.Criteria.SelectedStatuses.Add("Active");
            SelectedWorkflowPreset = "My Pull Requests";
            ApplyFiltersAndSorting();
        });

        ApplyQuickFilterNeedsReviewCommand = ReactiveCommand.Create(() =>
        {
            ResetFiltersToDefaults();
            FilterState.Criteria.NeedsMyReviewOnly = true;
            FilterState.Criteria.SelectedStatuses.Clear();
            FilterState.Criteria.SelectedStatuses.Add("Active");
            FilterState.Criteria.SelectedReviewerVotes.Clear();
            FilterState.Criteria.SelectedReviewerVotes.Add("No vote");
            SelectedWorkflowPreset = "Need My Review";
            ApplyFiltersAndSorting();
        });

        ApplyQuickFilterRecentCommand = ReactiveCommand.Create(() =>
        {
            ResetFiltersToDefaults();
            SelectedDateRangePreset = "Last 7 Days";
            SelectedSortPreset = "Most Recent";
            SelectedWorkflowPreset = "Recently Updated";
            ApplyFiltersAndSorting();
        });

        // Initialize missing group selection commands
        SelectGroupsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // Open group selection dialog
            var groupSelectionWindow = new Views.GroupSelectionWindow();
            var groupSelectionViewModel = new GroupSelectionViewModel(_groupSettings.AvailableGroups.ToList(),
                _groupSettings.SelectedGroups.ToList());
            groupSelectionWindow.DataContext = groupSelectionViewModel;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var result = await groupSelectionWindow.ShowDialog<bool>(desktop.MainWindow);
                if (result)
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

        ClearGroupSelectionCommand = ReactiveCommand.Create(() =>
        {
            _groupSettings = _groupSettings with { SelectedGroups = new List<string>() };
            UpdateSelectedGroupsText();
            ApplyFiltersAndSorting();
        });

        SelectGroupsWithoutVoteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // Open group selection dialog for groups without vote
            var groupSelectionWindow = new Views.GroupSelectionWindow();
            var groupSelectionViewModel = new GroupSelectionViewModel(
                _groupSettings.AvailableGroups.ToList(),
                FilterState.SelectedGroupsWithoutVote.ToList());
            groupSelectionWindow.DataContext = groupSelectionViewModel;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var result = await groupSelectionWindow.ShowDialog<bool>(desktop.MainWindow);
                if (result)
                {
                    FilterState.SelectedGroupsWithoutVote = groupSelectionViewModel.SelectedGroups.ToList();

                    UpdateSelectedGroupsWithoutVoteText();
                    ApplyFiltersAndSorting();
                }
            }
        });

        ClearGroupsWithoutVoteSelectionCommand = ReactiveCommand.Create(() =>
        {
            FilterState.SelectedGroupsWithoutVote = new List<string>();
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
            catch (Exception)
            {
                // Log error (removed excessive logging)
            }
        });

        // Automatically retrieve and set user information
        Task.Run(async () =>
        {
            try
            {
                await InitializeUserInformationAsync();
            }
            catch (Exception)
            {
                // Log error (removed excessive logging)
            }
        });
        
        // Initialize dropdown defaults to ensure UI shows proper values on startup
        InitializeDropdownDefaults();
    }

    private void LoadPreferences()
    {
        try
        {
            // Load saved preferences for filters and sorting
            if (FilterSortPreferencesStorage.TryLoad(out var preferences) && preferences != null)
            {
                _preferences = preferences;
            }
            else
            {
                _preferences = new FilterSortPreferences();
            }

            // Load saved filter views into the collection
            SavedFilterViews.Clear();
            foreach (var savedView in _preferences.SavedViews)
            {
                SavedFilterViews.Add(savedView);
            }

            // Apply the last selected view if it exists
            if (!string.IsNullOrEmpty(_preferences.LastSelectedView))
            {
                var lastSelectedView = SavedFilterViews.FirstOrDefault(v => v.Name == _preferences.LastSelectedView);
                if (lastSelectedView != null)
                {
                    SelectedSavedFilterView = lastSelectedView;
                }
            }
        }
        catch (Exception)
        {
            // Log error (removed excessive logging)
            // Continue with default preferences if loading fails
            _preferences = new FilterSortPreferences();
        }
    }

    private bool _isApplyingFilterView = false;

    private void ApplySavedFilterView(SavedFilterView savedView)
    {
        try
        {
            // Set flag to prevent multiple filter applications during this process
            _isApplyingFilterView = true;

            // Apply filter criteria from saved view
            var filterData = savedView.FilterCriteria;

            // Reset all filters first (like workflow presets do)
            ResetFiltersToDefaults();

            // Apply all filter properties directly
            // Property setters won't trigger ApplyFiltersAndSorting due to the flag
            FilterState.Criteria.TitleFilter = filterData.TitleFilter ?? string.Empty;
            FilterState.Criteria.CreatorFilter = filterData.CreatorFilter ?? string.Empty;
            FilterState.Criteria.SourceBranchFilter = filterData.SourceBranchFilter ?? string.Empty;
            FilterState.Criteria.TargetBranchFilter = filterData.TargetBranchFilter ?? string.Empty;
            FilterState.Criteria.ReviewerFilter = filterData.ReviewerFilter ?? string.Empty;

            // Apply status and vote filters using the UI properties
            FilterState.Criteria.SelectedStatuses.Clear();
            if (filterData.SelectedStatuses.Any())
            {
                FilterState.Criteria.SelectedStatuses.Add(filterData.SelectedStatuses.First());
            }

            FilterState.Criteria.SelectedReviewerVotes.Clear();
            if (filterData.SelectedReviewerVotes.Any())
            {
                FilterState.Criteria.SelectedReviewerVotes.Add(filterData.SelectedReviewerVotes.First());
            }

            // Apply date filters
            FilterState.Criteria.CreatedAfter = filterData.CreatedAfter;
            FilterState.Criteria.CreatedBefore = filterData.CreatedBefore;

            // Apply draft filter
            FilterState.Criteria.IsDraft = filterData.IsDraft;

            // Apply boolean filters
            FilterState.Criteria.MyPullRequestsOnly = filterData.MyPullRequestsOnly;
            FilterState.Criteria.AssignedToMeOnly = filterData.AssignedToMeOnly;
            FilterState.Criteria.NeedsMyReviewOnly = filterData.NeedsMyReviewOnly;

            // Apply workflow preset if specified
            if (!string.IsNullOrEmpty(filterData.WorkflowPreset))
            {
                SelectedWorkflowPreset = filterData.WorkflowPreset;
            }

            // Apply sort criteria from saved view
            var sortData = savedView.SortCriteria;
            SelectedSortPreset = sortData.CurrentPreset ?? "Most Recent";

            // Track the filter source for clarity
            FilterState.Criteria.SetFilterSource("SavedView", savedView.Name);

            // Clear the flag before applying filters
            _isApplyingFilterView = false;

            // Now apply the filters once
            ApplyFiltersAndSorting();

            // Update last used timestamp
            savedView.LastUsed = DateTime.Now;

            // Save the updated preferences
            _preferences.LastSelectedView = savedView.Name;
            FilterSortPreferencesStorage.Save(_preferences);

            // Notify UI of state changes
            this.RaisePropertyChanged(nameof(CurrentFilterSource));
            this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
            this.RaisePropertyChanged(nameof(HasActiveFilters));
            this.RaisePropertyChanged(nameof(FilterStatusText));
        }
        catch (Exception)
        {
            // Ensure flag is cleared even if an error occurs
            _isApplyingFilterView = false;
        }
    }

    private void UpdateFilterOptions()
    {
        // Filter options are now managed by FilterOrchestrator
        // This method can be simplified or removed entirely
        FilterOrchestrator.UpdateAvailableOptions(_allPullRequests);
    }

    private void UpdateAvailableOptions()
    {
        // Delegate to FilterOrchestrator
        FilterOrchestrator.UpdateAvailableOptions(_allPullRequests);
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

        // Use the FilterOrchestrator to apply filters and sorting
        var result = _filterOrchestrator.ApplyFiltersAndSorting(filteredPRs);

        PullRequests.Clear();
        foreach (var pr in result)
        {
            PullRequests.Add(pr);
        }

        // Update summary statistics and filter state properties
        this.RaisePropertyChanged(nameof(ActivePRCount));
        this.RaisePropertyChanged(nameof(MyReviewPendingCount));
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
    }

    private void UpdateStatusFilter(string value)
    {
        FilterState.Criteria.SelectedStatuses.Clear();
        if (!string.IsNullOrEmpty(value) && value != "All")
        {
            FilterState.Criteria.SelectedStatuses.Add(value);
        }
    }

    private void UpdateReviewerVoteFilter(string value)
    {
        FilterState.Criteria.SelectedReviewerVotes.Clear();
        if (!string.IsNullOrEmpty(value) && value != "All")
        {
            FilterState.Criteria.SelectedReviewerVotes.Add(value);
        }
    }

    private void ApplyDateRangePreset(string preset)
    {
        var now = DateTimeOffset.Now;
        var (after, before) = preset switch
        {
            "Today" => (now.Date, now.Date.AddDays(1).AddTicks(-1)),
            "Yesterday" => (now.Date.AddDays(-1), now.Date.AddTicks(-1)),
            "This Week" => (now.Date.AddDays(-(int)now.DayOfWeek),
                now.Date.AddDays(7 - (int)now.DayOfWeek).AddTicks(-1)),
            "Last Week" => (now.Date.AddDays(-(int)now.DayOfWeek - 7),
                now.Date.AddDays(-(int)now.DayOfWeek).AddTicks(-1)),
            "This Month" => (new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset),
                new DateTimeOffset(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59,
                    now.Offset)),
            "Last Month" => (new DateTimeOffset(now.Year, now.Month - 1, 1, 0, 0, 0, now.Offset),
                new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddTicks(-1)),
            "Last 7 Days" => (now.AddDays(-7), now),
            "Last 30 Days" => (now.AddDays(-30), now),
            _ => ((DateTimeOffset?)null, (DateTimeOffset?)null)
        };

        FilterState.Criteria.CreatedAfter = after;
        FilterState.Criteria.CreatedBefore = before;
        ApplyFiltersAndSorting();
    }

    private void ApplyWorkflowPreset(string preset)
    {
        // Reset all filters first
        ResetFiltersToDefaults();

        // Get the user's role to determine appropriate filters
        var userRole = _settings?.UserRole ?? UserRole.Developer;

        switch (preset)
        {
            case "Team Lead Overview":
                FilterState.Criteria.SelectedStatuses.Clear();
                FilterState.Criteria.SelectedStatuses.Add("Active");
                SelectedDateRangePreset = "Last 7 Days";
                break;
            case "My Pull Requests":
                FilterState.Criteria.MyPullRequestsOnly = true;
                FilterState.Criteria.SelectedStatuses.Clear();
                FilterState.Criteria.SelectedStatuses.Add("Active");
                break;
            case "Need My Review":
                ApplyNeedMyReviewPreset(userRole);
                break;
            case "Approved & Ready for Testing":
                ApplyApprovedReadyForTestingPreset(userRole);
                break;
            case "Waiting for Author Response":
                ApplyWaitingForAuthorPreset(userRole);
                break;
            case "Recently Updated":
                SelectedDateRangePreset = "Last 7 Days";
                SelectedSortPreset = "Most Recent";
                break;
            case "High Priority":
                FilterState.Criteria.SelectedStatuses.Clear();
                FilterState.Criteria.SelectedStatuses.Add("Active");
                SelectedSortPreset = "Needs Attention";
                break;
        }

        // Track the filter source for clarity
        FilterState.Criteria.SetFilterSource("Preset", preset);

        // Notify UI of state changes
        this.RaisePropertyChanged(nameof(CurrentFilterSource));
        this.RaisePropertyChanged(nameof(ActiveFiltersSummary));
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(FilterStatusText));
    }

    private void ApplyNeedMyReviewPreset(UserRole userRole)
    {
        FilterState.Criteria.SelectedStatuses.Clear();
        FilterState.Criteria.SelectedStatuses.Add("Active");

        switch (userRole)
        {
            case UserRole.Developer:
            case UserRole.Architect:
                // For developers/architects: Show PRs where I haven't voted yet
                FilterState.Criteria.NeedsMyReviewOnly = true;
                FilterState.Criteria.SelectedReviewerVotes.Clear();
                FilterState.Criteria.SelectedReviewerVotes.Add("No vote");
                break;
            case UserRole.Tester:
                // For testers: Show PRs that are approved and ready for testing
                FilterState.Criteria.SelectedReviewerVotes.Clear();
                FilterState.Criteria.SelectedReviewerVotes.Add("Approved");
                break;
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // For team leads: Show PRs that need any review (broader view)
                // Don't filter by personal vote, show all active PRs needing attention
                break;
            case UserRole.General:
            default:
                // General role: Default developer behavior
                FilterState.Criteria.NeedsMyReviewOnly = true;
                FilterState.Criteria.SelectedReviewerVotes.Clear();
                FilterState.Criteria.SelectedReviewerVotes.Add("No vote");
                break;
        }
    }

    private void ApplyApprovedReadyForTestingPreset(UserRole userRole)
    {
        FilterState.Criteria.SelectedStatuses.Clear();
        FilterState.Criteria.SelectedStatuses.Add("Active");

        switch (userRole)
        {
            case UserRole.Developer:
            case UserRole.Architect:
                // For developers: Show PRs where I have approved
                FilterState.Criteria.SelectedReviewerVotes.Clear();
                FilterState.Criteria.SelectedReviewerVotes.Add("Approved");
                break;
            case UserRole.Tester:
                // For testers: Show ALL PRs that have been approved by anyone and are ready for testing
                // Don't filter by my vote - show PRs approved by developers
                // This is achieved by not setting any personal vote filter
                break;
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // For team leads: Show all approved PRs for oversight
                // Don't filter by personal vote
                break;
            case UserRole.General:
            default:
                // General role: Default developer behavior
                FilterState.Criteria.SelectedReviewerVotes.Clear();
                FilterState.Criteria.SelectedReviewerVotes.Add("Approved");
                break;
        }
    }

    private void ApplyWaitingForAuthorPreset(UserRole userRole)
    {
        FilterState.Criteria.SelectedStatuses.Clear();
        FilterState.Criteria.SelectedStatuses.Add("Active");

        switch (userRole)
        {
            case UserRole.Developer:
            case UserRole.Architect:
                // For developers: Show PRs where I voted "waiting for author"
                FilterState.Criteria.SelectedReviewerVotes.Clear();
                FilterState.Criteria.SelectedReviewerVotes.Add("Waiting for author");
                break;
            case UserRole.Tester:
                // For testers: Show ALL PRs waiting for author response (not just mine)
                // This helps them track what's blocked in testing pipeline
                break;
            case UserRole.TeamLead:
            case UserRole.ScrumMaster:
                // For team leads: Show all PRs waiting for author for bottleneck analysis
                break;
            case UserRole.General:
            default:
                // General role: Default developer behavior
                FilterState.Criteria.SelectedReviewerVotes.Clear();
                FilterState.Criteria.SelectedReviewerVotes.Add("Waiting for author");
                break;
        }
    }

    private void ResetFiltersToDefaults()
    {
        // Reset all filter properties through FilterOrchestrator
        FilterState.Criteria.Reset();
        FilterState.SortCriteria.Reset();
        
        // Reset UI-specific properties
        SelectedDateRangePreset = "All Time";
        EnableGroupFiltering = false;
        
        // Update filter source tracking when manually resetting
        FilterState.Criteria.SetFilterSource("Manual");
    }


    private string GetWorkflowPresetTooltip(string preset)
    {
        return preset switch
        {
            "All Pull Requests" => "Show all pull requests without any filters",
            "Team Lead Overview" => "Active PRs from the last 7 days for team oversight",
            "My Pull Requests" => "Show only pull requests created by you",
            "Need My Review" => "Active PRs where you haven't voted yet",
            "Approved & Ready for Testing" => "Active PRs that have been approved and are ready for testing",
            "Waiting for Author Response" => "PRs waiting for author response",
            "Recently Updated" => "PRs updated in the last 7 days, sorted by most recent",
            "High Priority" => "PRs that need immediate attention",
            _ => "Custom workflow preset"
        };
    }

    private async Task InitializeUserInformationAsync()
    {
        try
        {
            var personalAccessToken = ConnectionSettingsStorage.GetPersonalAccessToken();
            if (string.IsNullOrWhiteSpace(_settings.Organization) || string.IsNullOrWhiteSpace(personalAccessToken))
            {
                return;
            }

            // Try to get user info without aggressive PAT validation that could cause redirect loops
            var userInfo = await _client.GetCurrentUserAsync(_settings.Organization, personalAccessToken);
            
            if (userInfo != UserInfo.Empty && !string.IsNullOrEmpty(userInfo.Id) && !string.IsNullOrEmpty(userInfo.DisplayName))
            {
                // Update filter criteria with automatically retrieved user information
                FilterState.Criteria.CurrentUserId = userInfo.Id;
                FilterState.Criteria.UserDisplayName = userInfo.DisplayName;
                
                // Update connection settings to store the retrieved user information for future use
                var updatedSettings = _settings with 
                { 
                    ReviewerId = userInfo.Id, 
                    UserDisplayName = userInfo.DisplayName 
                };
                
                // Save the updated settings so the user doesn't need to enter this information manually
                ConnectionSettingsStorage.Save(updatedSettings);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _authService.HandlePatValidationError(ex, "InitializeUserInformationAsync");
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
        {
            _authService.HandlePatValidationError(ex, "InitializeUserInformationAsync");
        }
        catch (Exception)
        {
            // Log error (removed excessive logging)
        }
    }
}
