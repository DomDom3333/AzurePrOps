using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace AzurePrOps.Models.FilteringAndSorting;

public class FilterCriteria : INotifyPropertyChanged
{
    // Text-based filters
    private string _titleFilter = string.Empty;
    private string _creatorFilter = string.Empty;
    private string _sourceBranchFilter = string.Empty;
    private string _targetBranchFilter = string.Empty;
    private string _reviewerFilter = string.Empty;
    private string _globalSearchText = string.Empty;

    // Status and vote filters
    private List<string> _selectedStatuses = new();
    private List<string> _selectedReviewerVotes = new();
    
    // Date filters
    private DateTimeOffset? _createdAfter;
    private DateTimeOffset? _createdBefore;
    
    // Draft filter
    private bool? _isDraft; // null = all, true = drafts only, false = non-drafts only
    
    // Advanced filters
    private bool _myPullRequestsOnly;
    private bool _assignedToMeOnly;
    private bool _needsMyReviewOnly;
    private bool _excludeMyPullRequests;
    private string _currentUserId = string.Empty;
    private string _userDisplayName = string.Empty;

    // Group filtering for "no vote" scenario
    private bool _enableGroupsWithoutVoteFilter;
    private List<string> _selectedGroupsWithoutVote = new();

    // Filter presets for different roles/workflows
    private string _workflowPreset = "All"; // All, TeamLead, Developer, Reviewer, QA

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string GlobalSearchText
    {
        get => _globalSearchText;
        set
        {
            _globalSearchText = value;
            OnPropertyChanged();
        }
    }

    public string TitleFilter
    {
        get => _titleFilter;
        set
        {
            _titleFilter = value;
            OnPropertyChanged();
        }
    }

    public string CreatorFilter
    {
        get => _creatorFilter;
        set
        {
            _creatorFilter = value;
            OnPropertyChanged();
        }
    }

    public string SourceBranchFilter
    {
        get => _sourceBranchFilter;
        set
        {
            _sourceBranchFilter = value;
            OnPropertyChanged();
        }
    }

    public string TargetBranchFilter
    {
        get => _targetBranchFilter;
        set
        {
            _targetBranchFilter = value;
            OnPropertyChanged();
        }
    }

    public string ReviewerFilter
    {
        get => _reviewerFilter;
        set
        {
            _reviewerFilter = value;
            OnPropertyChanged();
        }
    }

    public List<string> SelectedStatuses
    {
        get => _selectedStatuses;
        set
        {
            _selectedStatuses = value;
            OnPropertyChanged();
        }
    }

    public List<string> SelectedReviewerVotes
    {
        get => _selectedReviewerVotes;
        set
        {
            _selectedReviewerVotes = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset? CreatedAfter
    {
        get => _createdAfter;
        set
        {
            _createdAfter = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset? CreatedBefore
    {
        get => _createdBefore;
        set
        {
            _createdBefore = value;
            OnPropertyChanged();
        }
    }

    public bool? IsDraft
    {
        get => _isDraft;
        set
        {
            _isDraft = value;
            OnPropertyChanged();
        }
    }

    public bool MyPullRequestsOnly
    {
        get => _myPullRequestsOnly;
        set
        {
            _myPullRequestsOnly = value;
            OnPropertyChanged();
        }
    }

    public bool AssignedToMeOnly
    {
        get => _assignedToMeOnly;
        set
        {
            _assignedToMeOnly = value;
            OnPropertyChanged();
        }
    }

    public bool NeedsMyReviewOnly
    {
        get => _needsMyReviewOnly;
        set
        {
            _needsMyReviewOnly = value;
            OnPropertyChanged();
        }
    }

    public bool ExcludeMyPullRequests
    {
        get => _excludeMyPullRequests;
        set
        {
            _excludeMyPullRequests = value;
            OnPropertyChanged();
        }
    }

    public string CurrentUserId
    {
        get => _currentUserId;
        set
        {
            _currentUserId = value;
            OnPropertyChanged();
        }
    }

    public string UserDisplayName
    {
        get => _userDisplayName;
        set
        {
            _userDisplayName = value;
            OnPropertyChanged();
        }
    }

    public bool EnableGroupsWithoutVoteFilter
    {
        get => _enableGroupsWithoutVoteFilter;
        set
        {
            _enableGroupsWithoutVoteFilter = value;
            OnPropertyChanged();
        }
    }

    public List<string> SelectedGroupsWithoutVote
    {
        get => _selectedGroupsWithoutVote;
        set
        {
            _selectedGroupsWithoutVote = value;
            OnPropertyChanged();
        }
    }

    public string WorkflowPreset
    {
        get => _workflowPreset;
        set
        {
            _workflowPreset = value;
            OnPropertyChanged();
        }
    }

    public void Reset()
    {
        TitleFilter = string.Empty;
        CreatorFilter = string.Empty;
        SourceBranchFilter = string.Empty;
        TargetBranchFilter = string.Empty;
        ReviewerFilter = string.Empty;
        GlobalSearchText = string.Empty;
        SelectedStatuses.Clear();
        SelectedReviewerVotes.Clear();
        CreatedAfter = null;
        CreatedBefore = null;
        IsDraft = null;
        MyPullRequestsOnly = false;
        AssignedToMeOnly = false;
        NeedsMyReviewOnly = false;
        ExcludeMyPullRequests = false;
        EnableGroupsWithoutVoteFilter = false;
        SelectedGroupsWithoutVote.Clear();
        WorkflowPreset = "All";
    }

    public void ApplyWorkflowPreset(string preset)
    {
        WorkflowPreset = preset;
        
        // The actual implementation of workflow logic is handled in the ViewModel
        // This method just stores the preset selection
    }

    public static string GetWorkflowPresetTooltip(string preset)
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

    /// <summary>
    /// Checks if the filter criteria matches the given pull request
    /// </summary>
    public bool Matches(AzurePrOps.AzureConnection.Models.PullRequestInfo pr)
    {
        // Global search - searches across title, creator, and branch names
        if (!string.IsNullOrWhiteSpace(GlobalSearchText))
        {
            var searchLower = GlobalSearchText.ToLowerInvariant();
            var matchesGlobal = pr.Title.ToLowerInvariant().Contains(searchLower) ||
                               pr.Creator.ToLowerInvariant().Contains(searchLower) ||
                               pr.SourceBranch.ToLowerInvariant().Contains(searchLower) ||
                               pr.TargetBranch.ToLowerInvariant().Contains(searchLower);
            
            if (!matchesGlobal) return false;
        }

        // Text filters
        if (!string.IsNullOrWhiteSpace(TitleFilter) && 
            !pr.Title.Contains(TitleFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(CreatorFilter) && 
            !pr.Creator.Contains(CreatorFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(SourceBranchFilter) && 
            !pr.SourceBranch.Contains(SourceBranchFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(TargetBranchFilter) && 
            !pr.TargetBranch.Contains(TargetBranchFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(ReviewerFilter))
        {
            var hasMatchingReviewer = pr.Reviewers.Any(r => 
                r.DisplayName.Contains(ReviewerFilter, StringComparison.OrdinalIgnoreCase));
            if (!hasMatchingReviewer) return false;
        }

        // Status filter
        if (SelectedStatuses.Any() && !SelectedStatuses.Contains(pr.Status))
            return false;

        // Reviewer vote filter
        if (SelectedReviewerVotes.Any())
        {
            var currentUserReviewer = pr.Reviewers.FirstOrDefault(r => r.Id == CurrentUserId);
            var currentVote = currentUserReviewer?.Vote ?? "No vote";
            
            if (!SelectedReviewerVotes.Contains(currentVote))
                return false;
        }

        // Date filters
        if (CreatedAfter.HasValue && pr.Created < CreatedAfter.Value)
            return false;

        if (CreatedBefore.HasValue && pr.Created > CreatedBefore.Value)
            return false;

        // Draft filter
        if (IsDraft.HasValue && pr.IsDraft != IsDraft.Value)
            return false;

        // Personal filters
        if (MyPullRequestsOnly && pr.Creator != CurrentUserId)
            return false;

        if (AssignedToMeOnly && !pr.Reviewers.Any(r => r.Id == CurrentUserId))
            return false;

        if (NeedsMyReviewOnly)
        {
            var myReviewer = pr.Reviewers.FirstOrDefault(r => r.Id == CurrentUserId);
            if (myReviewer == null || 
                (!string.IsNullOrWhiteSpace(myReviewer.Vote) && myReviewer.Vote != "No vote"))
                return false;
        }

        // Exclude my pull requests filter
        if (ExcludeMyPullRequests && pr.Creator == CurrentUserId)
            return false;

        // Groups without vote filter
        if (EnableGroupsWithoutVoteFilter && SelectedGroupsWithoutVote.Any())
        {
            var hasMatchingGroupWithoutVote = pr.Reviewers.Any(r => 
                r.IsGroup && 
                SelectedGroupsWithoutVote.Contains(r.DisplayName) &&
                (string.IsNullOrWhiteSpace(r.Vote) || r.Vote == "No vote"));
            
            if (!hasMatchingGroupWithoutVote) return false;
        }

        return true;
    }
}