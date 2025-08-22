using System;
using System.Collections.Generic;
using System.Linq;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.Models.FilteringAndSorting;

namespace AzurePrOps.Services;

public class PullRequestFilteringSortingService
{
    public IEnumerable<PullRequestInfo> ApplyFiltersAndSorting(
        IEnumerable<PullRequestInfo> pullRequests,
        FilterCriteria filterCriteria,
        SortCriteria sortCriteria)
    {
        var filtered = ApplyFilters(pullRequests, filterCriteria);
        return ApplySorting(filtered, sortCriteria);
    }

    public IEnumerable<PullRequestInfo> ApplyFilters(
        IEnumerable<PullRequestInfo> pullRequests,
        FilterCriteria criteria)
    {
        var filtered = pullRequests.AsEnumerable();

        // Text-based filters
        if (!string.IsNullOrWhiteSpace(criteria.TitleFilter))
            filtered = filtered.Where(pr => pr.Title.Contains(criteria.TitleFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(criteria.CreatorFilter))
            filtered = filtered.Where(pr => pr.Creator.Contains(criteria.CreatorFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(criteria.SourceBranchFilter))
            filtered = filtered.Where(pr => pr.SourceBranch.Contains(criteria.SourceBranchFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(criteria.TargetBranchFilter))
            filtered = filtered.Where(pr => pr.TargetBranch.Contains(criteria.TargetBranchFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(criteria.ReviewerFilter))
            filtered = filtered.Where(pr => pr.Reviewers.Any(r => r.DisplayName.Contains(criteria.ReviewerFilter, StringComparison.OrdinalIgnoreCase)));

        // Multi-select status filter
        if (criteria.SelectedStatuses.Any())
            filtered = filtered.Where(pr => criteria.SelectedStatuses.Contains(pr.Status, StringComparer.OrdinalIgnoreCase));

        // Multi-select reviewer vote filter
        if (criteria.SelectedReviewerVotes.Any())
            filtered = filtered.Where(pr => criteria.SelectedReviewerVotes.Contains(pr.ReviewerVote, StringComparer.OrdinalIgnoreCase) ||
                                           pr.Reviewers.Any(r => criteria.SelectedReviewerVotes.Contains(r.Vote, StringComparer.OrdinalIgnoreCase)));

        // Date range filters
        if (criteria.CreatedAfter.HasValue)
            filtered = filtered.Where(pr => pr.Created.Date >= criteria.CreatedAfter.Value.Date);

        if (criteria.CreatedBefore.HasValue)
            filtered = filtered.Where(pr => pr.Created.Date <= criteria.CreatedBefore.Value.Date);

        // Draft filter
        if (criteria.IsDraft.HasValue)
            filtered = filtered.Where(pr => pr.IsDraft == criteria.IsDraft.Value);

        // User-specific filters
        if (criteria.MyPullRequestsOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserId))
            filtered = filtered.Where(pr => pr.Creator.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase));

        if (criteria.AssignedToMeOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserId))
            filtered = filtered.Where(pr => pr.Reviewers.Any(r => r.Id.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase)));

        if (criteria.NeedsMyReviewOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserId))
            filtered = filtered.Where(pr => pr.Reviewers.Any(r => 
                r.Id.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase) && 
                (r.Vote.Equals("No vote", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(r.Vote))));

        return filtered;
    }

    public IEnumerable<PullRequestInfo> ApplySorting(
        IEnumerable<PullRequestInfo> pullRequests,
        SortCriteria criteria)
    {
        var query = pullRequests.AsQueryable();

        // Apply primary sort
        query = ApplySortField(query, criteria.PrimarySortField, criteria.PrimarySortDirection, true);

        // Apply secondary sort if specified
        if (criteria.SecondarySortField.HasValue)
            query = ApplySortField(query, criteria.SecondarySortField.Value, criteria.SecondarySortDirection, false);

        // Apply tertiary sort if specified
        if (criteria.TertiarySortField.HasValue)
            query = ApplySortField(query, criteria.TertiarySortField.Value, criteria.TertiarySortDirection, false);

        return query.AsEnumerable();
    }

    private IOrderedQueryable<PullRequestInfo> ApplySortField(
        IQueryable<PullRequestInfo> query, 
        SortField field, 
        SortDirection direction, 
        bool isPrimary)
    {
        return field switch
        {
            SortField.Title => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => pr.Title) : query.OrderByDescending(pr => pr.Title))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.Title) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => pr.Title)),
            
            SortField.Creator => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => pr.Creator) : query.OrderByDescending(pr => pr.Creator))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.Creator) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => pr.Creator)),
            
            SortField.Created => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => pr.Created) : query.OrderByDescending(pr => pr.Created))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.Created) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => pr.Created)),
            
            SortField.Status => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => GetStatusPriority(pr.Status)) : query.OrderByDescending(pr => GetStatusPriority(pr.Status)))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => GetStatusPriority(pr.Status)) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => GetStatusPriority(pr.Status))),
            
            SortField.SourceBranch => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => pr.SourceBranch) : query.OrderByDescending(pr => pr.SourceBranch))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.SourceBranch) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => pr.SourceBranch)),
            
            SortField.TargetBranch => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => pr.TargetBranch) : query.OrderByDescending(pr => pr.TargetBranch))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.TargetBranch) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => pr.TargetBranch)),
            
            SortField.ReviewerVote => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => GetVotePriority(pr.ReviewerVote)) : query.OrderByDescending(pr => GetVotePriority(pr.ReviewerVote)))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => GetVotePriority(pr.ReviewerVote)) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => GetVotePriority(pr.ReviewerVote))),
            
            SortField.Id => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => pr.Id) : query.OrderByDescending(pr => pr.Id))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.Id) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => pr.Id)),
            
            SortField.ReviewerCount => isPrimary 
                ? (direction == SortDirection.Ascending ? query.OrderBy(pr => pr.Reviewers.Count) : query.OrderByDescending(pr => pr.Reviewers.Count))
                : (direction == SortDirection.Ascending ? ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.Reviewers.Count) : ((IOrderedQueryable<PullRequestInfo>)query).ThenByDescending(pr => pr.Reviewers.Count)),
            
            _ => isPrimary 
                ? query.OrderBy(pr => pr.Created)
                : ((IOrderedQueryable<PullRequestInfo>)query).ThenBy(pr => pr.Created)
        };
    }

    private int GetStatusPriority(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "active" => 1,
            "draft" => 2,
            "completed" => 3,
            "abandoned" => 4,
            _ => 5
        };
    }

    private int GetVotePriority(string vote)
    {
        return vote.ToLowerInvariant() switch
        {
            "rejected" => 1,
            "waiting for author" => 2,
            "no vote" => 3,
            "" => 3,
            "approved with suggestions" => 4,
            "approved" => 5,
            _ => 6
        };
    }

    public List<string> GetAvailableStatuses(IEnumerable<PullRequestInfo> pullRequests)
    {
        return pullRequests.Select(pr => pr.Status).Distinct().OrderBy(s => s).ToList();
    }

    public List<string> GetAvailableReviewerVotes(IEnumerable<PullRequestInfo> pullRequests)
    {
        var votes = new HashSet<string>();
        foreach (var pr in pullRequests)
        {
            if (!string.IsNullOrWhiteSpace(pr.ReviewerVote))
                votes.Add(pr.ReviewerVote);
            
            foreach (var reviewer in pr.Reviewers)
            {
                if (!string.IsNullOrWhiteSpace(reviewer.Vote))
                    votes.Add(reviewer.Vote);
            }
        }
        return votes.OrderBy(v => v).ToList();
    }

    public List<string> GetAvailableCreators(IEnumerable<PullRequestInfo> pullRequests)
    {
        return pullRequests.Select(pr => pr.Creator).Distinct().OrderBy(c => c).ToList();
    }

    public List<string> GetAvailableReviewers(IEnumerable<PullRequestInfo> pullRequests)
    {
        var reviewers = new HashSet<string>();
        foreach (var pr in pullRequests)
        {
            foreach (var reviewer in pr.Reviewers)
            {
                reviewers.Add(reviewer.DisplayName);
            }
        }
        return reviewers.OrderBy(r => r).ToList();
    }

    public List<string> GetAvailableBranches(IEnumerable<PullRequestInfo> pullRequests, bool source = true)
    {
        return source 
            ? pullRequests.Select(pr => pr.SourceBranch).Distinct().OrderBy(b => b).ToList()
            : pullRequests.Select(pr => pr.TargetBranch).Distinct().OrderBy(b => b).ToList();
    }
}