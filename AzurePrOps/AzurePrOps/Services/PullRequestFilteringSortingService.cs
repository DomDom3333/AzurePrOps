using System;
using System.Collections.Generic;
using System.Linq;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.Models.FilteringAndSorting;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.Services;

/// <summary>
/// Service responsible for filtering and sorting pull requests based on criteria
/// </summary>
public class PullRequestFilteringSortingService
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestFilteringSortingService>();
    /// <summary>
    /// Applies filters and sorting to a collection of pull requests
    /// </summary>
    /// <param name="pullRequests">The collection of pull requests to filter and sort</param>
    /// <param name="filterCriteria">The filter criteria to apply</param>
    /// <param name="sortCriteria">The sort criteria to apply</param>
    /// <param name="userGroupMemberships">User's group memberships for personal filters</param>
    /// <param name="currentUserId">Current user's ID for personal filters</param>
    /// <returns>Filtered and sorted collection of pull requests</returns>
    public IEnumerable<PullRequestInfo> ApplyFiltersAndSorting(
        IEnumerable<PullRequestInfo> pullRequests,
        FilterCriteria filterCriteria,
        SortCriteria sortCriteria,
        IReadOnlyList<string> userGroupMemberships,
        string currentUserId = "")
    {
        if (pullRequests == null)
            throw new ArgumentNullException(nameof(pullRequests));
        
        if (filterCriteria == null)
            throw new ArgumentNullException(nameof(filterCriteria));
        
        if (sortCriteria == null)
            throw new ArgumentNullException(nameof(sortCriteria));

        // Apply filters
        var filtered = ApplyFilters(pullRequests, filterCriteria, userGroupMemberships, currentUserId);

        // Apply sorting
        var sorted = ApplySorting(filtered, sortCriteria);

        return sorted;
    }

    /// <summary>
    /// Applies all filter criteria to the pull requests
    /// </summary>
    private IEnumerable<PullRequestInfo> ApplyFilters(
        IEnumerable<PullRequestInfo> pullRequests,
        FilterCriteria criteria,
        IReadOnlyList<string> userGroupMemberships,
        string currentUserId)
    {
        var prs = pullRequests as IList<PullRequestInfo> ?? pullRequests.ToList();
        var query = prs.AsQueryable();

        // Personal filters
        // Log current user context
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            _logger.LogWarning("Filter apply: CurrentUserId is empty. Filters relying on user ID may not work as expected.");
        }
        else
        {
            _logger.LogInformation("Filter apply: CurrentUserId={CurrentUserId}", currentUserId);
        }

        // Keep high-volume diagnostics in debug mode only.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            try
            {
                var distinctCreators = prs
                    .GroupBy(pr => new { pr.CreatorId, pr.Creator })
                    .Select(g => new { g.Key.CreatorId, g.Key.Creator, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                _logger.LogDebug("PR Creators Summary: TotalPRs={Total}, DistinctCreators={DistinctCount}",
                    prs.Count, distinctCreators.Count);

                foreach (var dc in distinctCreators.Take(20))
                {
                    _logger.LogDebug("Creator: Id={CreatorId}, Name={Creator}, PRs={Count}",
                        dc.CreatorId ?? "(null)", dc.Creator ?? "(null)", dc.Count);
                }

                // Log a compact PR to Creator mapping for troubleshooting (limited)
                foreach (var pr in prs.Take(50))
                {
                    _logger.LogDebug("PR Author Mapping: PR#{PrId} CreatorId={CreatorId} Creator={Creator}",
                        pr.Id, pr.CreatorId ?? "(null)", pr.Creator ?? "(null)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log PR creators summary.");
            }
        }

        var totalCount = prs.Count;

        if (criteria.MyPullRequestsOnly)
        {
            var creatorIdMatches = prs.Count(pr => StringEqualsIgnoreCase(pr.CreatorId, currentUserId));
            _logger.LogInformation("MyPullRequestsOnly active. Matching by ID only. TotalPRs={Total}, CreatorIdMatches={Matches}", totalCount, creatorIdMatches);

            query = query.Where(pr => StringEqualsIgnoreCase(pr.CreatorId, currentUserId));
        }

        if (criteria.ExcludeMyPullRequests)
        {
            var creatorIdMatches = prs.Count(pr => StringEqualsIgnoreCase(pr.CreatorId, currentUserId));
            _logger.LogInformation("ExcludeMyPullRequests active. Excluding by ID only. TotalPRs={Total}, CreatorIdMatches={Matches}", totalCount, creatorIdMatches);

            query = query.Where(pr => !StringEqualsIgnoreCase(pr.CreatorId, currentUserId));
        }

        if (criteria.AssignedToMeOnly)
        {
            var assignedMatches = prs.Count(pr => pr.Reviewers.Any(r => StringEqualsIgnoreCase(r.Id, currentUserId)));
            _logger.LogInformation("AssignedToMeOnly active. ReviewerId comparison only. TotalPRs={Total}, AssignedMatches={Matches}", totalCount, assignedMatches);

            query = query.Where(pr => pr.Reviewers.Any(r => 
                StringEqualsIgnoreCase(r.Id, currentUserId)));
        }

        if (criteria.NeedsMyReviewOnly)
        {
            var needReviewMatches = prs.Count(pr => pr.Reviewers.Any(r => StringEqualsIgnoreCase(r.Id, currentUserId) && (StringEqualsIgnoreCase(r.Vote, "No vote") || string.IsNullOrWhiteSpace(r.Vote))));
            _logger.LogInformation("NeedsMyReviewOnly active. ReviewerId + NoVote comparison. TotalPRs={Total}, NeedReviewMatches={Matches}", totalCount, needReviewMatches);

            query = query.Where(pr => pr.Reviewers.Any(r => 
                StringEqualsIgnoreCase(r.Id, currentUserId) && 
                (StringEqualsIgnoreCase(r.Vote, "No vote") || 
                 string.IsNullOrWhiteSpace(r.Vote))));
        }

        // Status filters
        if (criteria.SelectedStatuses.Any())
        {
            query = query.Where(pr => criteria.SelectedStatuses.Contains(pr.Status, StringComparer.OrdinalIgnoreCase));
        }

        if (criteria.IsDraft.HasValue)
        {
            query = query.Where(pr => pr.IsDraft == criteria.IsDraft.Value);
        }

        // Text filters
        if (!string.IsNullOrWhiteSpace(criteria.GlobalSearchText))
        {
            var searchText = criteria.GlobalSearchText.ToLowerInvariant();
            query = query.Where(pr => 
                ContainsInvariant(pr.Title, searchText) ||
                ContainsInvariant(pr.Creator, searchText) ||
                ContainsInvariant(pr.SourceBranch, searchText) ||
                ContainsInvariant(pr.TargetBranch, searchText) ||
                pr.Id.ToString().Contains(searchText));
        }

        if (!string.IsNullOrWhiteSpace(criteria.TitleFilter))
        {
            var titleText = criteria.TitleFilter.ToLowerInvariant();
            query = query.Where(pr => ContainsInvariant(pr.Title, titleText));
        }

        if (!string.IsNullOrWhiteSpace(criteria.CreatorFilter))
        {
            var creatorText = criteria.CreatorFilter.ToLowerInvariant();
            query = query.Where(pr => ContainsInvariant(pr.Creator, creatorText));
        }

        if (!string.IsNullOrWhiteSpace(criteria.ReviewerFilter))
        {
            var reviewerText = criteria.ReviewerFilter.ToLowerInvariant();
            query = query.Where(pr => pr.Reviewers.Any(r => 
                ContainsInvariant(r.DisplayName, reviewerText)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.SourceBranchFilter))
        {
            var sourceBranchText = criteria.SourceBranchFilter.ToLowerInvariant();
            query = query.Where(pr => ContainsInvariant(pr.SourceBranch, sourceBranchText));
        }

        if (!string.IsNullOrWhiteSpace(criteria.TargetBranchFilter))
        {
            var targetBranchText = criteria.TargetBranchFilter.ToLowerInvariant();
            query = query.Where(pr => ContainsInvariant(pr.TargetBranch, targetBranchText));
        }

        // Reviewer vote filters
        if (criteria.SelectedReviewerVotes.Any())
        {
            query = query.Where(pr => pr.Reviewers.Any(r => 
                criteria.SelectedReviewerVotes.Contains(r.Vote, StringComparer.OrdinalIgnoreCase)));
        }

        // Date filters
        if (criteria.CreatedAfter.HasValue)
        {
            query = query.Where(pr => pr.Created >= criteria.CreatedAfter.Value);
        }

        if (criteria.CreatedBefore.HasValue)
        {
            query = query.Where(pr => pr.Created <= criteria.CreatedBefore.Value);
        }

        if (criteria.UpdatedAfter.HasValue)
        {
            query = query.Where(pr => pr.EffectiveLastActivity >= criteria.UpdatedAfter.Value);
        }

        if (criteria.UpdatedBefore.HasValue)
        {
            query = query.Where(pr => pr.EffectiveLastActivity <= criteria.UpdatedBefore.Value);
        }

        // Group filters
        if (criteria.EnableGroupsWithoutVoteFilter && criteria.SelectedGroupsWithoutVote.Any())
        {
            query = query.Where(pr => pr.Reviewers.Any(r => 
                r.IsGroup && 
                criteria.SelectedGroupsWithoutVote.Contains(r.DisplayName, StringComparer.OrdinalIgnoreCase) &&
                (StringEqualsIgnoreCase(r.Vote, "No vote") || 
                 string.IsNullOrWhiteSpace(r.Vote))));
        }

        // Reviewer count filters
        if (criteria.MinReviewerCount.HasValue)
        {
            query = query.Where(pr => pr.Reviewers.Count >= criteria.MinReviewerCount.Value);
        }

        if (criteria.MaxReviewerCount.HasValue)
        {
            query = query.Where(pr => pr.Reviewers.Count <= criteria.MaxReviewerCount.Value);
        }

        return query;
    }

    private static bool StringEqualsIgnoreCase(string? value, string? other)
        => string.Equals(value, other, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsInvariant(string? source, string textLower)
        => !string.IsNullOrEmpty(source) && source.Contains(textLower, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies sorting criteria to the pull requests
    /// </summary>
    private IEnumerable<PullRequestInfo> ApplySorting(
        IEnumerable<PullRequestInfo> pullRequests,
        SortCriteria criteria)
    {
        var sortFields = criteria.GetSortFields();
        if (!sortFields.Any())
        {
            return pullRequests.OrderByDescending(pr => pr.Created);
        }

        var query = pullRequests.AsQueryable();
        IOrderedQueryable<PullRequestInfo>? orderedQuery = null;

        for (int i = 0; i < sortFields.Count; i++)
        {
            var (field, direction) = sortFields[i];
            var isPrimary = i == 0;

            orderedQuery = ApplySortField(query, orderedQuery, field, direction, isPrimary);
        }

        return orderedQuery ?? query;
    }

    /// <summary>
    /// Applies a single sort field to the query
    /// </summary>
    private IOrderedQueryable<PullRequestInfo> ApplySortField(
        IQueryable<PullRequestInfo> query,
        IOrderedQueryable<PullRequestInfo>? orderedQuery,
        SortField field,
        SortDirection direction,
        bool isPrimary)
    {
        return field switch
        {
            SortField.Title => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.Title) 
                    : query.OrderByDescending(pr => pr.Title))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.Title) 
                    : orderedQuery!.ThenByDescending(pr => pr.Title)),

            SortField.Creator => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.Creator) 
                    : query.OrderByDescending(pr => pr.Creator))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.Creator) 
                    : orderedQuery!.ThenByDescending(pr => pr.Creator)),

            SortField.CreatedDate => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.Created) 
                    : query.OrderByDescending(pr => pr.Created))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.Created) 
                    : orderedQuery!.ThenByDescending(pr => pr.Created)),

            SortField.UpdatedDate => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.EffectiveLastActivity) 
                    : query.OrderByDescending(pr => pr.EffectiveLastActivity))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.EffectiveLastActivity) 
                    : orderedQuery!.ThenByDescending(pr => pr.EffectiveLastActivity)),

            SortField.Status => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => GetStatusPriority(pr.Status)) 
                    : query.OrderByDescending(pr => GetStatusPriority(pr.Status)))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => GetStatusPriority(pr.Status)) 
                    : orderedQuery!.ThenByDescending(pr => GetStatusPriority(pr.Status))),

            SortField.SourceBranch => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.SourceBranch) 
                    : query.OrderByDescending(pr => pr.SourceBranch))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.SourceBranch) 
                    : orderedQuery!.ThenByDescending(pr => pr.SourceBranch)),

            SortField.TargetBranch => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.TargetBranch) 
                    : query.OrderByDescending(pr => pr.TargetBranch))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.TargetBranch) 
                    : orderedQuery!.ThenByDescending(pr => pr.TargetBranch)),

            SortField.ReviewerVote => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => GetWorstVotePriority(pr)) 
                    : query.OrderByDescending(pr => GetWorstVotePriority(pr)))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => GetWorstVotePriority(pr)) 
                    : orderedQuery!.ThenByDescending(pr => GetWorstVotePriority(pr))),

            SortField.PrId => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.Id) 
                    : query.OrderByDescending(pr => pr.Id))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.Id) 
                    : orderedQuery!.ThenByDescending(pr => pr.Id)),

            SortField.ReviewerCount => isPrimary
                ? (direction == SortDirection.Ascending 
                    ? query.OrderBy(pr => pr.Reviewers.Count) 
                    : query.OrderByDescending(pr => pr.Reviewers.Count))
                : (direction == SortDirection.Ascending 
                    ? orderedQuery!.ThenBy(pr => pr.Reviewers.Count) 
                    : orderedQuery!.ThenByDescending(pr => pr.Reviewers.Count)),

            _ => isPrimary 
                ? query.OrderByDescending(pr => pr.Created)
                : orderedQuery!.ThenByDescending(pr => pr.Created)
        };
    }

    /// <summary>
    /// Gets priority for status sorting (Active first, then Draft, Completed, Abandoned)
    /// </summary>
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

    /// <summary>
    /// Gets the worst vote priority for a PR (for sorting by most critical vote first)
    /// </summary>
    private int GetWorstVotePriority(PullRequestInfo pr)
    {
        if (!pr.Reviewers.Any())
            return 10;

        var worstPriority = int.MaxValue;
        foreach (var reviewer in pr.Reviewers)
        {
            var priority = GetVotePriority(reviewer.Vote);
            if (priority < worstPriority)
                worstPriority = priority;
        }

        return worstPriority;
    }

    /// <summary>
    /// Gets priority for vote sorting (critical votes first)
    /// </summary>
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
}
