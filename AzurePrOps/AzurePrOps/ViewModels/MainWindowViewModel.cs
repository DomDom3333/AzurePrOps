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
using AzurePrOps.Infrastructure;

namespace AzurePrOps.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<MainWindowViewModel>();
    private readonly AzureDevOpsClient _client = new();
    private readonly IPullRequestService _pullRequestService;
    private Models.ConnectionSettings _settings; // Remove readonly to allow updates
    private readonly PullRequestFilteringSortingService _filterSortService = new();
    private readonly AuthenticationService _authService;
    private IReadOnlyList<string> _userGroupMemberships = new List<string>();

    public ObservableCollection<PullRequestInfo> PullRequests { get; } = new();
    private readonly ObservableCollection<PullRequestInfo> _allPullRequests = new();

    // Enhanced filtering and sorting
    public FilterCriteria FilterCriteria { get; } = new();
    public SortCriteria SortCriteria { get; } = new();
    private FilterSortPreferences _preferences = new();

    public ObservableCollection<FilterView> FilterViews { get; } = new();
    public ObservableCollection<SavedFilterView> SavedFilterViews { get; } = new();

    // Available options for dropdowns - with proper initialization
    public ObservableCollection<string> AvailableStatuses { get; } = new() { "All", "Active", "Completed", "Abandoned" };
    public ObservableCollection<string> AvailableReviewerVotes { get; } = new() { "All", "No vote", "Approved", "Approved with suggestions", "Waiting for author", "Rejected" };
    public ObservableCollection<string> AvailableCreators { get; } = new();
    public ObservableCollection<string> AvailableReviewers { get; } = new();
    public ObservableCollection<string> AvailableSourceBranches { get; } = new();
    public ObservableCollection<string> AvailableTargetBranches { get; } = new();

    // Group filtering - consolidated and improved
    public ObservableCollection<string> AvailableGroups { get; } = new();
    private GroupSettings _groupSettings = new(new List<string>(), new List<string>(), DateTime.MinValue);

    #region Quick Filter Properties - Reorganized for better UX

    // Personal filters section
    private bool _showMyPullRequestsOnly = false;
    public bool ShowMyPullRequestsOnly
    {
        get => _showMyPullRequestsOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showMyPullRequestsOnly, value);
            FilterCriteria.MyPullRequestsOnly = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private bool _showAssignedToMeOnly = false;
    public bool ShowAssignedToMeOnly
    {
        get => _showAssignedToMeOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showAssignedToMeOnly, value);
            FilterCriteria.AssignedToMeOnly = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private bool _showNeedsMyReviewOnly = false;
    public bool ShowNeedsMyReviewOnly
    {
        get => _showNeedsMyReviewOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _showNeedsMyReviewOnly, value);
            FilterCriteria.NeedsMyReviewOnly = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private bool _excludeMyPullRequests = false;
    public bool ExcludeMyPullRequests
    {
        get => _excludeMyPullRequests;
        set
        {
            this.RaiseAndSetIfChanged(ref _excludeMyPullRequests, value);
            FilterCriteria.ExcludeMyPullRequests = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    // Status filters section
    private string _draftFilter = "All";
    public string DraftFilter
    {
        get => _draftFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _draftFilter, value);
            FilterCriteria.IsDraft = value switch
            {
                "Drafts Only" => true,
                "Non-Drafts Only" => false,
                _ => null
            };
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    public ObservableCollection<string> DraftFilterOptions { get; } = new() { "All", "Drafts Only", "Non-Drafts Only" };

    // Group filtering - consolidated
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

    private bool _enableGroupsWithoutVoteFilter = false;
    public bool EnableGroupsWithoutVoteFilter
    {
        get => _enableGroupsWithoutVoteFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _enableGroupsWithoutVoteFilter, value);
            FilterCriteria.EnableGroupsWithoutVoteFilter = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private string _selectedGroupsWithoutVoteText = "No Groups Selected";
    public string SelectedGroupsWithoutVoteText
    {
        get => _selectedGroupsWithoutVoteText;
        set => this.RaiseAndSetIfChanged(ref _selectedGroupsWithoutVoteText, value);
    }

    #endregion

    #region Enhanced Search and Filter Properties

    // Search functionality
    private string _globalSearchText = string.Empty;
    public string GlobalSearchText
    {
        get => _globalSearchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _globalSearchText, value);
            FilterCriteria.GlobalSearchText = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    // Text filters with proper defaults
    private string _titleFilter = string.Empty;
    public string TitleFilter
    {
        get => _titleFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _titleFilter, value);
            FilterCriteria.TitleFilter = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
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
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private string _reviewerFilter = string.Empty;
    public string ReviewerFilter
    {
        get => _reviewerFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _reviewerFilter, value);
            FilterCriteria.ReviewerFilter = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
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
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
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
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private string _statusFilter = "All";
    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            // Ensure we never set it to null - default to "All"
            var safeValue = value ?? "All";
            this.RaiseAndSetIfChanged(ref _statusFilter, safeValue);
            UpdateStatusFilter(safeValue);
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private string _reviewerVoteFilter = "All";
    public string ReviewerVoteFilter
    {
        get => _reviewerVoteFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _reviewerVoteFilter, value);
            UpdateReviewerVoteFilter(value);
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    #endregion

    #region Date Range Properties

    private DateTimeOffset? _createdAfter;
    public DateTimeOffset? CreatedAfter
    {
        get => _createdAfter;
        set
        {
            this.RaiseAndSetIfChanged(ref _createdAfter, value);
            FilterCriteria.CreatedAfter = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    private DateTimeOffset? _createdBefore;
    public DateTimeOffset? CreatedBefore
    {
        get => _createdBefore;
        set
        {
            this.RaiseAndSetIfChanged(ref _createdBefore, value);
            FilterCriteria.CreatedBefore = value;
            if (!_isApplyingFilterView) ApplyFiltersAndSorting();
        }
    }

    // Quick date range presets
    public ObservableCollection<string> DateRangePresets { get; } = new()
    {
        "All Time", "Today", "Yesterday", "This Week", "Last Week", "This Month", "Last Month", "Last 7 Days", "Last 30 Days"
    };

    private string _selectedDateRangePreset = "All Time";
    public string SelectedDateRangePreset
    {
        get => _selectedDateRangePreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDateRangePreset, value);
            ApplyDateRangePreset(value);
        }
    }

    #endregion

    // Sort presets with better organization
    public ObservableCollection<string> SortPresets { get; } = new()
    {
        "Most Recent", "Oldest First", "Title A-Z", "Title Z-A", "Creator A-Z", "Creator Z-A",
        "Status Priority", "Review Priority", "High Activity", "Needs Attention"
    };

    private string _selectedSortPreset = "Most Recent";
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

    // Workflow presets with enhanced options
    public ObservableCollection<string> WorkflowPresets { get; } = new()
    {
        "All Pull Requests", "Team Lead Overview", "My Pull Requests", "Need My Review", 
        "Ready for QA", "Awaiting Author", "Recently Updated", "High Priority"
    };

    private string _selectedWorkflowPreset = "All Pull Requests";
    public string SelectedWorkflowPreset
    {
        get => _selectedWorkflowPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedWorkflowPreset, value);
            ApplyWorkflowPreset(value);
            this.RaisePropertyChanged(nameof(SelectedWorkflowPresetTooltip));
            ApplyFiltersAndSorting();
        }
    }

    public string SelectedWorkflowPresetTooltip => 
        GetWorkflowPresetTooltip(SelectedWorkflowPreset);

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
                // Check if this is actually a comprehensive saved filter disguised as a simple FilterView
                var matchingSavedView = SavedFilterViews.FirstOrDefault(sv => sv.Name == value.Name);
                if (matchingSavedView != null)
                {
                    // Apply the comprehensive saved filter instead
                    ApplySavedFilterView(matchingSavedView);
                }
                else
                {
                    // Apply as a simple filter view (original behavior)
                    _isApplyingFilterView = true;
                    
                    // Reset filters first to ensure clean state
                    ResetFiltersToDefaults();
                    
                    // Apply the simple filter properties
                    TitleFilter = value.Title;
                    CreatorFilter = value.Creator;
                    SourceBranchFilter = value.SourceBranch;
                    TargetBranchFilter = value.TargetBranch;
                    StatusFilter = string.IsNullOrWhiteSpace(value.Status) ? "All" : value.Status;
                    
                    _isApplyingFilterView = false;
                    
                    // Apply the filters after setting all filter properties
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

    #region Filter State Properties

    // Filter summary for better UX
    public string ActiveFiltersCount => GetActiveFiltersCount();
    public bool HasActiveFilters => GetActiveFiltersCount() != "No filters active";
    public string FilterSummary => GetFilterSummary();

    #endregion

    // Sort field options for dropdown
    public ObservableCollection<string> SortFieldOptions { get; } = new()
    {
        "Title", "Creator", "Created Date", "Status", "Source Branch", 
        "Target Branch", "Reviewer Vote", "PR ID", "Reviewer Count"
    };

    // Summary statistics properties
    public int ActivePRCount => PullRequests.Count(pr => pr.Status.Equals("Active", StringComparison.OrdinalIgnoreCase));
    
    public int MyReviewPendingCount => PullRequests.Count(pr => 
        pr.Reviewers.Any(r => r.Id.Equals(_settings.ReviewerId, StringComparison.OrdinalIgnoreCase) && 
                              (r.Vote.Equals("No vote", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(r.Vote))));

    // Commands
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> ApproveCommand { get; }
    public ReactiveCommand<Unit, Unit> PostCommentCommand { get; }
    public ReactiveCommand<PullRequestInfo?, Unit> ViewDetailsCommand { get; }
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
                _logger.LogWarning("No Personal Access Token found for loading groups");
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
        FilterCriteria.CurrentUserId = _settings.ReviewerId ?? string.Empty;
        FilterCriteria.UserDisplayName = _settings.UserDisplayName ?? string.Empty;

        // Initialize the status filter to ensure "All" works properly
        UpdateStatusFilter(_statusFilter);
        UpdateReviewerVoteFilter(_reviewerVoteFilter);

        // Set up property change handlers for real-time filtering
        FilterCriteria.PropertyChanged += (_, _) => ApplyFiltersAndSorting();
        SortCriteria.PropertyChanged += (_, _) => ApplyFiltersAndSorting();

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
                
                _logger.LogInformation("[DEBUG_LOG] Fetched user group memberships: {GroupCount} groups: {Groups}", 
                    _userGroupMemberships?.Count ?? 0, 
                    _userGroupMemberships != null ? string.Join(", ", _userGroupMemberships) : "null");

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
                    _settings.ReviewerId,
                    personalAccessToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                _authService.HandlePatValidationError(ex, "ApproveCommand");
            }
            catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
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
                _settings.ReviewerId,
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
                _logger.LogError(ex, "Failed to open pull request details window");
                await ShowErrorMessage($"Failed to open details window: {ex.Message}");
            }
            finally
            {
                IsLoadingDiffs = false;
            }
        });

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
            catch (Exception ex)
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
                    FilterCriteria.CurrentUserId = _settings.ReviewerId ?? string.Empty;
                    FilterCriteria.UserDisplayName = _settings.UserDisplayName ?? string.Empty;
                    
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
                        await ShowErrorMessage($"Settings saved successfully, but failed to refresh data: {ex.Message}");
                    }
                }
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

        ClearAllFiltersCommand = ReactiveCommand.Create(() =>
        {
            // Reset all filter properties to their defaults
            ShowMyPullRequestsOnly = false;
            ShowAssignedToMeOnly = false;
            ShowNeedsMyReviewOnly = false;
            StatusFilter = "All";
            ReviewerVoteFilter = "All";
            DraftFilter = "All";
            SelectedDateRangePreset = "All Time";
            TitleFilter = string.Empty;
            CreatorFilter = string.Empty;
            ReviewerFilter = string.Empty;
            SourceBranchFilter = string.Empty;
            TargetBranchFilter = string.Empty;
            GlobalSearchText = string.Empty;
            EnableGroupFiltering = false;
            EnableGroupsWithoutVoteFilter = false;

            // Clear selected groups
            _groupSettings = _groupSettings with { SelectedGroups = new List<string>() };
            FilterCriteria.SelectedGroupsWithoutVote.Clear();

            // Update UI
            UpdateSelectedGroupsText();
            UpdateSelectedGroupsWithoutVoteText();
            ApplyFiltersAndSorting();
        });

        ApplyQuickFilterMyPRsCommand = ReactiveCommand.Create(() =>
        {
            ResetFiltersToDefaults();
            ShowMyPullRequestsOnly = true;
            StatusFilter = "Active";
            SelectedWorkflowPreset = "My Pull Requests";
        });

        ApplyQuickFilterNeedsReviewCommand = ReactiveCommand.Create(() =>
        {
            ResetFiltersToDefaults();
            ShowNeedsMyReviewOnly = true;
            StatusFilter = "Active";
            ReviewerVoteFilter = "No vote";
            SelectedWorkflowPreset = "Need My Review";
        });

        ApplyQuickFilterRecentCommand = ReactiveCommand.Create(() =>
        {
            ResetFiltersToDefaults();
            SelectedDateRangePreset = "Last 7 Days";
            SelectedSortPreset = "Most Recent";
            SelectedWorkflowPreset = "Recently Updated";
        });

        // Initialize missing group selection commands
        SelectGroupsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // Open group selection dialog
            var groupSelectionWindow = new Views.GroupSelectionWindow();
            var groupSelectionViewModel = new GroupSelectionViewModel(_groupSettings.AvailableGroups.ToList(), _groupSettings.SelectedGroups.ToList());
            groupSelectionWindow.DataContext = groupSelectionViewModel;
            
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var result = await groupSelectionWindow.ShowDialog<bool>(desktop.MainWindow);
                if (result)
                {
                    _groupSettings = _groupSettings with { SelectedGroups = groupSelectionViewModel.SelectedGroups.ToList() };
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
                FilterCriteria.SelectedGroupsWithoutVote.ToList());
            groupSelectionWindow.DataContext = groupSelectionViewModel;
            
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var result = await groupSelectionWindow.ShowDialog<bool>(desktop.MainWindow);
                if (result)
                {
                    FilterCriteria.SelectedGroupsWithoutVote.Clear();
                    foreach (var group in groupSelectionViewModel.SelectedGroups)
                    {
                        FilterCriteria.SelectedGroupsWithoutVote.Add(group);
                    }
                    UpdateSelectedGroupsWithoutVoteText();
                    ApplyFiltersAndSorting();
                }
            }
        });

        ClearGroupsWithoutVoteSelectionCommand = ReactiveCommand.Create(() =>
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

        // Automatically retrieve and set user information
        Task.Run(async () =>
        {
            try
            {
                await InitializeUserInformationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize user information");
            }
        });
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load preferences");
            // Continue with default preferences if loading fails
            _preferences = new FilterSortPreferences();
        }
    }

    private bool _isApplyingFilterView = false;

    private void ApplySavedFilterView(SavedFilterView savedView)
    {
        try
        {
            _logger.LogInformation("Starting to apply saved filter view: {ViewName}", savedView.Name);
            
            // Set flag to prevent multiple filter applications during this process
            _isApplyingFilterView = true;
            
            // Apply filter criteria from saved view
            var filterData = savedView.FilterCriteria;

            _logger.LogInformation("Filter data - MyPRs: {MyPRs}, AssignedToMe: {AssignedToMe}, NeedsMyReview: {NeedsMyReview}", 
                filterData.MyPullRequestsOnly, filterData.AssignedToMeOnly, filterData.NeedsMyReviewOnly);
            _logger.LogInformation("Filter data - Statuses: [{Statuses}], Votes: [{Votes}]", 
                string.Join(", ", filterData.SelectedStatuses), string.Join(", ", filterData.SelectedReviewerVotes));
            
            // Reset all filters first (like workflow presets do)
            ResetFiltersToDefaults();
            
            // Apply all filter properties directly
            // Property setters won't trigger ApplyFiltersAndSorting due to the flag
            TitleFilter = filterData.TitleFilter ?? string.Empty;
            CreatorFilter = filterData.CreatorFilter ?? string.Empty;
            SourceBranchFilter = filterData.SourceBranchFilter ?? string.Empty;
            TargetBranchFilter = filterData.TargetBranchFilter ?? string.Empty;
            ReviewerFilter = filterData.ReviewerFilter ?? string.Empty;
            
            // Apply status and vote filters using the UI properties
            StatusFilter = filterData.SelectedStatuses.Any() 
                ? filterData.SelectedStatuses.First() 
                : "All";
            
            ReviewerVoteFilter = filterData.SelectedReviewerVotes.Any() 
                ? filterData.SelectedReviewerVotes.First() 
                : "All";
            
            // Apply date filters
            CreatedAfter = filterData.CreatedAfter;
            CreatedBefore = filterData.CreatedBefore;
            
            // Apply draft filter
            DraftFilter = filterData.IsDraft switch
            {
                true => "Drafts Only",
                false => "Non-Drafts Only",
                _ => "All"
            };
            
            // Apply boolean filters
            ShowMyPullRequestsOnly = filterData.MyPullRequestsOnly;
            ShowAssignedToMeOnly = filterData.AssignedToMeOnly;
            ShowNeedsMyReviewOnly = filterData.NeedsMyReviewOnly;
            
            _logger.LogInformation("[DEBUG_SAVED_FILTER] Applied boolean filters - ShowMyPullRequestsOnly: {ShowMyPRs}, FilterCriteria.MyPullRequestsOnly: {CriteriaMyPRs}", 
                ShowMyPullRequestsOnly, FilterCriteria.MyPullRequestsOnly);
            
            // Apply workflow preset if specified
            if (!string.IsNullOrEmpty(filterData.WorkflowPreset))
            {
                SelectedWorkflowPreset = filterData.WorkflowPreset;
            }
            
            // Apply sort criteria from saved view
            var sortData = savedView.SortCriteria;
            SelectedSortPreset = sortData.CurrentPreset ?? "Most Recent";
            
            // Clear the flag before applying filters
            _isApplyingFilterView = false;
            
            // Now apply the filters once
            ApplyFiltersAndSorting();
            
            // Update last used timestamp
            savedView.LastUsed = DateTime.Now;
            
            // Save the updated preferences
            _preferences.LastSelectedView = savedView.Name;
            FilterSortPreferencesStorage.Save(_preferences);
            
            _logger.LogInformation("Successfully applied saved filter view: {ViewName}", savedView.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply saved filter view: {ViewName}", savedView.Name);
            // Ensure flag is cleared even if an error occurs
            _isApplyingFilterView = false;
        }
    }

    private void UpdateFilterOptions()
    {
        // Update available filter options based on current PRs
        var statuses = _allPullRequests.Select(pr => pr.Status).Distinct().OrderBy(s => s);
        
        // Build new status list while preserving "All" at the top
        var newStatuses = new List<string> { "All" };
        newStatuses.AddRange(statuses);
        
        // Only update if the collection has changed to preserve binding
        if (!AvailableStatuses.SequenceEqual(newStatuses))
        {
            var currentSelection = StatusFilter; // Preserve current selection
            
            // Clear and rebuild the collection immediately
            AvailableStatuses.Clear();
            foreach (var status in newStatuses)
                AvailableStatuses.Add(status);
            
            // Use a timer-based approach to ensure the ComboBox has time to process the collection changes
            var timer = new System.Timers.Timer(100); // 100ms delay
            timer.Elapsed += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                
                Dispatcher.UIThread.Post(() =>
                {
                    // Force a complete reset of the binding
                    var targetSelection = AvailableStatuses.Contains(currentSelection) ? currentSelection : "All";
                    
                    // Completely clear the binding first
                    _statusFilter = null;
                    this.RaisePropertyChanged(nameof(StatusFilter));
                    
                    // Set to the correct value after a small delay
                    Dispatcher.UIThread.Post(() =>
                    {
                        _statusFilter = targetSelection;
                        this.RaisePropertyChanged(nameof(StatusFilter));
                        UpdateStatusFilter(targetSelection);
                        
                        _logger.LogInformation("[DEBUG_LOG] StatusFilter set to: {StatusFilter}, Available count: {AvailableCount}", 
                            _statusFilter, AvailableStatuses.Count);
                    }, DispatcherPriority.ApplicationIdle);
                }, DispatcherPriority.Background);
            };
            timer.Start();
        }

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

        _logger.LogInformation("[DEBUG_LOG] ApplyFiltersAndSorting called. Total PRs: {TotalCount}, StatusFilter: {StatusFilter}, SelectedStatuses count: {SelectedStatusesCount}", 
            _allPullRequests.Count, _statusFilter, FilterCriteria.SelectedStatuses.Count);

        // Apply group filtering if enabled
        if (EnableGroupFiltering && _groupSettings.SelectedGroups.Any())
        {
            filteredPRs = filteredPRs.Where(pr =>
                pr.Reviewers.Any(reviewer =>
                    reviewer.IsGroup &&
                    _groupSettings.SelectedGroups.Contains(reviewer.DisplayName)));
        }

        var result = _filterSortService.ApplyFiltersAndSorting(filteredPRs, FilterCriteria, SortCriteria, _userGroupMemberships);

        _logger.LogInformation("[DEBUG_LOG] After filtering - Result count: {ResultCount}", result.Count());

        PullRequests.Clear();
        foreach (var pr in result)
        {
            PullRequests.Add(pr);
        }

        _logger.LogInformation("[DEBUG_LOG] PullRequests collection updated. Final count: {FinalCount}", PullRequests.Count);

        // Update summary statistics and filter state properties
        this.RaisePropertyChanged(nameof(ActivePRCount));
        this.RaisePropertyChanged(nameof(MyReviewPendingCount));
        this.RaisePropertyChanged(nameof(ActiveFiltersCount));
        this.RaisePropertyChanged(nameof(HasActiveFilters));
        this.RaisePropertyChanged(nameof(FilterSummary));
    }

    private void UpdateStatusFilter(string value)
    {
        FilterCriteria.SelectedStatuses.Clear();
        if (!string.IsNullOrEmpty(value) && value != "All")
        {
            FilterCriteria.SelectedStatuses.Add(value);
        }
    }

    private void UpdateReviewerVoteFilter(string value)
    {
        FilterCriteria.SelectedReviewerVotes.Clear();
        if (!string.IsNullOrEmpty(value) && value != "All")
        {
            FilterCriteria.SelectedReviewerVotes.Add(value);
        }
    }

    private void ApplyDateRangePreset(string preset)
    {
        var now = DateTimeOffset.Now;
        var (after, before) = preset switch
        {
            "Today" => (now.Date, now.Date.AddDays(1).AddTicks(-1)),
            "Yesterday" => (now.Date.AddDays(-1), now.Date.AddTicks(-1)),
            "This Week" => (now.Date.AddDays(-(int)now.DayOfWeek), now.Date.AddDays(7 - (int)now.DayOfWeek).AddTicks(-1)),
            "Last Week" => (now.Date.AddDays(-(int)now.DayOfWeek - 7), now.Date.AddDays(-(int)now.DayOfWeek).AddTicks(-1)),
            "This Month" => (new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset), new DateTimeOffset(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59, now.Offset)),
            "Last Month" => (new DateTimeOffset(now.Year, now.Month - 1, 1, 0, 0, 0, now.Offset), new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddTicks(-1)),
            "Last 7 Days" => (now.AddDays(-7), now),
            "Last 30 Days" => (now.AddDays(-30), now),
            _ => ((DateTimeOffset?)null, (DateTimeOffset?)null)
        };

        CreatedAfter = after;
        CreatedBefore = before;
    }

    private void ApplyWorkflowPreset(string preset)
    {
        // Reset all filters first
        ResetFiltersToDefaults();

        switch (preset)
        {
            case "Team Lead Overview":
                StatusFilter = "Active";
                SelectedDateRangePreset = "Last 7 Days";
                break;
            case "My Pull Requests":
                ShowMyPullRequestsOnly = true;
                StatusFilter = "Active";
                break;
            case "Need My Review":
                ShowNeedsMyReviewOnly = true;
                StatusFilter = "Active";
                ReviewerVoteFilter = "No vote";
                break;
            case "Ready for QA":
                StatusFilter = "Active";
                ReviewerVoteFilter = "Approved";
                break;
            case "Awaiting Author":
                StatusFilter = "Active";
                ReviewerVoteFilter = "Waiting for author";
                break;
            case "Recently Updated":
                SelectedDateRangePreset = "Last 7 Days";
                SelectedSortPreset = "Most Recent";
                break;
            case "High Priority":
                StatusFilter = "Active";
                SelectedSortPreset = "Needs Attention";
                break;
        }

        FilterCriteria.ApplyWorkflowPreset(preset);
    }

    private void ResetFiltersToDefaults()
    {
        ShowMyPullRequestsOnly = false;
        ShowAssignedToMeOnly = false;
        ShowNeedsMyReviewOnly = false;
        StatusFilter = "All";
        ReviewerVoteFilter = "All";
        DraftFilter = "All";
        SelectedDateRangePreset = "All Time";
        TitleFilter = string.Empty;
        CreatorFilter = string.Empty;
        ReviewerFilter = string.Empty;
        SourceBranchFilter = string.Empty;
        TargetBranchFilter = string.Empty;
        GlobalSearchText = string.Empty;
        EnableGroupFiltering = false;
        EnableGroupsWithoutVoteFilter = false;
    }

    private string GetActiveFiltersCount()
    {
        var count = 0;
        
        if (ShowMyPullRequestsOnly) count++;
        if (ShowAssignedToMeOnly) count++;
        if (ShowNeedsMyReviewOnly) count++;
        if (StatusFilter != "All") count++;
        if (ReviewerVoteFilter != "All") count++;
        if (DraftFilter != "All") count++;
        if (!string.IsNullOrWhiteSpace(TitleFilter)) count++;
        if (!string.IsNullOrWhiteSpace(CreatorFilter)) count++;
        if (!string.IsNullOrWhiteSpace(ReviewerFilter)) count++;
        if (!string.IsNullOrWhiteSpace(SourceBranchFilter)) count++;
        if (!string.IsNullOrWhiteSpace(TargetBranchFilter)) count++;
        if (!string.IsNullOrWhiteSpace(GlobalSearchText)) count++;
        if (CreatedAfter.HasValue || CreatedBefore.HasValue) count++;
        if (EnableGroupFiltering) count++;
        if (EnableGroupsWithoutVoteFilter) count++;

        return count == 0 ? "No filters active" : $"{count} filter{(count == 1 ? "" : "s")} active";
    }

    private string GetFilterSummary()
    {
        var summary = new List<string>();
        
        if (ShowMyPullRequestsOnly) summary.Add("My PRs");
        if (ShowNeedsMyReviewOnly) summary.Add("Needs Review");
        if (StatusFilter != "All") summary.Add($"Status: {StatusFilter}");
        if (!string.IsNullOrWhiteSpace(TitleFilter)) summary.Add($"Title: {TitleFilter}");
        if (CreatedAfter.HasValue || CreatedBefore.HasValue) summary.Add("Date Range");

        return summary.Any() ? string.Join(", ", summary) : "No active filters";
    }

    private string GetWorkflowPresetTooltip(string preset)
    {
        return preset switch
        {
            "All Pull Requests" => "Show all pull requests without any filters",
            "Team Lead Overview" => "Active PRs from the last 7 days for team oversight",
            "My Pull Requests" => "Show only pull requests created by you",
            "Need My Review" => "Active PRs where you haven't voted yet",
            "Ready for QA" => "Active PRs that have been approved and are ready for QA",
            "Awaiting Author" => "PRs waiting for author response",
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
                _logger.LogWarning("Cannot retrieve user information: Organization or Personal Access Token is missing");
                // Don't redirect to login here - the user just successfully logged in
                // If there's really an issue, other operations will catch it
                return;
            }

            _logger.LogInformation("Automatically retrieving current user information from Azure DevOps...");
            
            // Try to get user info without aggressive PAT validation that could cause redirect loops
            var userInfo = await _client.GetCurrentUserAsync(_settings.Organization, personalAccessToken);
            
            if (userInfo != UserInfo.Empty && !string.IsNullOrEmpty(userInfo.Id) && !string.IsNullOrEmpty(userInfo.DisplayName))
            {
                // Update filter criteria with automatically retrieved user information
                FilterCriteria.CurrentUserId = userInfo.Id;
                FilterCriteria.UserDisplayName = userInfo.DisplayName;
                
                _logger.LogInformation("Successfully retrieved user information: {DisplayName} (ID: {UserId})", 
                    userInfo.DisplayName, userInfo.Id);

                // Update connection settings to store the retrieved user information for future use
                var updatedSettings = _settings with 
                { 
                    ReviewerId = userInfo.Id, 
                    UserDisplayName = userInfo.DisplayName 
                };
                
                // Save the updated settings so the user doesn't need to enter this information manually
                ConnectionSettingsStorage.Save(updatedSettings);
                
                _logger.LogInformation("User information has been automatically saved to settings");
            }
            else
            {
                _logger.LogWarning("Could not retrieve user information from Azure DevOps. Using existing settings if available.");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve user information - unauthorized. Will continue with existing settings.");
            // Don't redirect to login during initialization - this could cause redirect loops
            // The user just successfully logged in, so let other operations handle auth issues if they persist
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
        {
            _logger.LogWarning(ex, "Failed to retrieve user information - HTTP auth error. Will continue with existing settings.");
            // Don't redirect to login during initialization - this could cause redirect loops
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to automatically retrieve user information from Azure DevOps");
            // Continue with existing settings - this is not a critical failure
        }
    }
}
