using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzurePrOps.Models.FilteringAndSorting;

public class FilterSortPreferences
{
    public FilterCriteriaData FilterCriteria { get; set; } = new();
    public SortCriteriaData SortCriteria { get; set; } = new();
    public List<SavedFilterView> SavedViews { get; set; } = new();
    public string? LastSelectedView { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

public class FilterCriteriaData
{
    // Text-based filters
    public string TitleFilter { get; set; } = string.Empty;
    public string CreatorFilter { get; set; } = string.Empty;
    public string SourceBranchFilter { get; set; } = string.Empty;
    public string TargetBranchFilter { get; set; } = string.Empty;
    public string ReviewerFilter { get; set; } = string.Empty;

    // Multi-select filters
    public List<string> SelectedStatuses { get; set; } = new();
    public List<string> SelectedReviewerVotes { get; set; } = new();
    
    // Date filters
    public DateTimeOffset? CreatedAfter { get; set; }
    public DateTimeOffset? CreatedBefore { get; set; }
    
    // Draft filter
    public bool? IsDraft { get; set; }
    
    // Advanced filters
    public bool MyPullRequestsOnly { get; set; }
    public bool AssignedToMeOnly { get; set; }
    public bool NeedsMyReviewOnly { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;

    // Workflow preset
    public string WorkflowPreset { get; set; } = "All";
}

public class SortCriteriaData
{
    public SortField PrimarySortField { get; set; } = SortField.Created;
    public SortDirection PrimarySortDirection { get; set; } = SortDirection.Descending;
    public SortField? SecondarySortField { get; set; }
    public SortDirection SecondarySortDirection { get; set; } = SortDirection.Ascending;
    public SortField? TertiarySortField { get; set; }
    public SortDirection TertiarySortDirection { get; set; } = SortDirection.Ascending;
    public string CurrentPreset { get; set; } = "Newest First";
}

public class SavedFilterView
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FilterCriteriaData FilterCriteria { get; set; } = new();
    public SortCriteriaData SortCriteria { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime LastUsed { get; set; } = DateTime.Now;
    public bool IsDefault { get; set; }
    public string UserRole { get; set; } = "All"; // All, TeamLead, Developer, Reviewer, QA

    public SavedFilterView() { }

    public SavedFilterView(string name, FilterCriteriaData filterCriteria, SortCriteriaData sortCriteria, string description = "", string userRole = "All")
    {
        Name = name;
        Description = description;
        FilterCriteria = filterCriteria;
        SortCriteria = sortCriteria;
        UserRole = userRole;
    }
}

// Extension methods for easy conversion between runtime and data models
public static class FilterSortExtensions
{
    public static FilterCriteriaData ToData(this FilterCriteria criteria)
    {
        return new FilterCriteriaData
        {
            TitleFilter = criteria.TitleFilter,
            CreatorFilter = criteria.CreatorFilter,
            SourceBranchFilter = criteria.SourceBranchFilter,
            TargetBranchFilter = criteria.TargetBranchFilter,
            ReviewerFilter = criteria.ReviewerFilter,
            SelectedStatuses = new List<string>(criteria.SelectedStatuses),
            SelectedReviewerVotes = new List<string>(criteria.SelectedReviewerVotes),
            CreatedAfter = criteria.CreatedAfter,
            CreatedBefore = criteria.CreatedBefore,
            IsDraft = criteria.IsDraft,
            MyPullRequestsOnly = criteria.MyPullRequestsOnly,
            AssignedToMeOnly = criteria.AssignedToMeOnly,
            NeedsMyReviewOnly = criteria.NeedsMyReviewOnly,
            CurrentUserId = criteria.CurrentUserId,
            WorkflowPreset = criteria.WorkflowPreset
        };
    }

    public static void FromData(this FilterCriteria criteria, FilterCriteriaData data)
    {
        criteria.TitleFilter = data.TitleFilter;
        criteria.CreatorFilter = data.CreatorFilter;
        criteria.SourceBranchFilter = data.SourceBranchFilter;
        criteria.TargetBranchFilter = data.TargetBranchFilter;
        criteria.ReviewerFilter = data.ReviewerFilter;
        criteria.SelectedStatuses = new List<string>(data.SelectedStatuses);
        criteria.SelectedReviewerVotes = new List<string>(data.SelectedReviewerVotes);
        criteria.CreatedAfter = data.CreatedAfter;
        criteria.CreatedBefore = data.CreatedBefore;
        criteria.IsDraft = data.IsDraft;
        criteria.MyPullRequestsOnly = data.MyPullRequestsOnly;
        criteria.AssignedToMeOnly = data.AssignedToMeOnly;
        criteria.NeedsMyReviewOnly = data.NeedsMyReviewOnly;
        criteria.CurrentUserId = data.CurrentUserId;
        criteria.WorkflowPreset = data.WorkflowPreset;
    }

    public static SortCriteriaData ToData(this SortCriteria criteria)
    {
        return new SortCriteriaData
        {
            PrimarySortField = criteria.PrimarySortField,
            PrimarySortDirection = criteria.PrimarySortDirection,
            SecondarySortField = criteria.SecondarySortField,
            SecondarySortDirection = criteria.SecondarySortDirection,
            TertiarySortField = criteria.TertiarySortField,
            TertiarySortDirection = criteria.TertiarySortDirection
        };
    }

    public static void FromData(this SortCriteria criteria, SortCriteriaData data)
    {
        criteria.PrimarySortField = data.PrimarySortField;
        criteria.PrimarySortDirection = data.PrimarySortDirection;
        criteria.SecondarySortField = data.SecondarySortField;
        criteria.SecondarySortDirection = data.SecondarySortDirection;
        criteria.TertiarySortField = data.TertiarySortField;
        criteria.TertiarySortDirection = data.TertiarySortDirection;
    }
}