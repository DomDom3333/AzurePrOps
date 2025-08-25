using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AzurePrOps.Models.FilteringAndSorting;

public class FilterCriteria : INotifyPropertyChanged
{
    // Text-based filters
    private string _titleFilter = string.Empty;
    private string _creatorFilter = string.Empty;
    private string _sourceBranchFilter = string.Empty;
    private string _targetBranchFilter = string.Empty;
    private string _reviewerFilter = string.Empty;

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
    private string _currentUserId = string.Empty;

    // Group filtering for "no vote" scenario
    private bool _enableGroupsWithoutVoteFilter;
    private List<string> _selectedGroupsWithoutVote = new();

    // Filter presets for different roles/workflows
    private string _workflowPreset = "All"; // All, TeamLead, Developer, Reviewer, QA

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

    public string CurrentUserId
    {
        get => _currentUserId;
        set
        {
            _currentUserId = value;
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
        SelectedStatuses.Clear();
        SelectedReviewerVotes.Clear();
        CreatedAfter = null;
        CreatedBefore = null;
        IsDraft = null;
        MyPullRequestsOnly = false;
        AssignedToMeOnly = false;
        NeedsMyReviewOnly = false;
        EnableGroupsWithoutVoteFilter = false;
        SelectedGroupsWithoutVote.Clear();
        WorkflowPreset = "All Pull Requests";
    }

    public void ApplyWorkflowPreset(string preset)
    {
        Reset();
        WorkflowPreset = preset;
        
        switch (preset)
        {
            case "Team Lead Overview":
                SelectedStatuses.AddRange(new[] { "Active", "Completed" });
                CreatedAfter = DateTimeOffset.Now.AddDays(-30); // Last 30 days
                break;
            case "My Pull Requests":
                MyPullRequestsOnly = true;
                SelectedStatuses.AddRange(new[] { "Active", "Draft" });
                break;
            case "Need My Review":
                NeedsMyReviewOnly = true;
                SelectedStatuses.Add("Active");
                break;
            case "Ready for QA":
                SelectedReviewerVotes.Add("Approved");
                SelectedStatuses.Add("Active");
                break;
        }
    }

    public static string GetWorkflowPresetTooltip(string preset)
    {
        return preset switch
        {
            "All Pull Requests" => "Shows all pull requests with no filters applied.",
            "Team Lead Overview" => "Team Lead view: Shows Active & Completed PRs from the last 30 days for team oversight.",
            "My Pull Requests" => "Developer view: Shows only your own Active & Draft pull requests.",
            "Need My Review" => "Reviewer view: Shows Active pull requests that need your review.",
            "Ready for QA" => "QA view: Shows Active pull requests that have been Approved and are ready for testing.",
            _ => "Custom workflow preset"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}