using System;
using System.Collections.Generic;
using System.Linq;
using AzurePrOps.AzureConnection.Models;
using AzurePrOps.Models.FilteringAndSorting;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.Services;

public class PullRequestFilteringSortingService
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<PullRequestFilteringSortingService>();
    public IEnumerable<PullRequestInfo> ApplyFiltersAndSorting(
        IEnumerable<PullRequestInfo> pullRequests,
        FilterCriteria filterCriteria,
        SortCriteria sortCriteria,
        IReadOnlyList<string>? userGroupMemberships = null)
    {
        var filtered = ApplyFilters(pullRequests, filterCriteria, userGroupMemberships);
        return ApplySorting(filtered, sortCriteria);
    }

    public IEnumerable<PullRequestInfo> ApplyFilters(
        IEnumerable<PullRequestInfo> pullRequests,
        FilterCriteria criteria,
        IReadOnlyList<string>? userGroupMemberships = null)
    {
        var filtered = pullRequests.AsEnumerable();

        // Global search filter - must be applied first
        if (!string.IsNullOrWhiteSpace(criteria.GlobalSearchText))
        {
            var searchLower = criteria.GlobalSearchText.ToLowerInvariant();
            filtered = filtered.Where(pr => 
                pr.Title.ToLowerInvariant().Contains(searchLower) ||
                pr.Creator.ToLowerInvariant().Contains(searchLower) ||
                pr.SourceBranch.ToLowerInvariant().Contains(searchLower) ||
                pr.TargetBranch.ToLowerInvariant().Contains(searchLower));
        }

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

        // User-specific filters with comprehensive user identification
        if (criteria.MyPullRequestsOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserId))
        {
            _logger.LogInformation("[DEBUG_LOG] MyPullRequestsOnly filter - CurrentUserId: {CurrentUserId}", criteria.CurrentUserId);
            
            // Log some sample PR creators to understand the format
            var sampleCreators = pullRequests.Take(3).Select(pr => pr.Creator).ToList();
            _logger.LogInformation("[DEBUG_LOG] Sample PR creators: {Creators}", string.Join(", ", sampleCreators));
            
            // Multiple strategies to identify current user's PRs
            filtered = filtered.Where(pr => IsCurrentUserPR(pr, criteria, pullRequests));
            
            var filteredCount = filtered.Count();
            _logger.LogInformation("[DEBUG_LOG] MyPullRequestsOnly filter result count: {Count}", filteredCount);
        }

        if (criteria.AssignedToMeOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserId))
            filtered = filtered.Where(pr => pr.Reviewers.Any(r => r.Id.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase)));

        // Exclude my pull requests filter - helps focus on PRs that need reviewing
        if (criteria.ExcludeMyPullRequests && !string.IsNullOrWhiteSpace(criteria.CurrentUserId))
        {
            _logger.LogInformation("[DEBUG_LOG] ExcludeMyPullRequests filter - CurrentUserId: {CurrentUserId}", criteria.CurrentUserId);
            
            // Multiple strategies to identify current user's PRs
            filtered = filtered.Where(pr => !IsCurrentUserPR(pr, criteria, pullRequests));
            
            var filteredCount = filtered.Count();
            _logger.LogInformation("[DEBUG_LOG] ExcludeMyPullRequests filter result count: {Count}", filteredCount);
        }

        if (criteria.NeedsMyReviewOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserId))
        {
            _logger.LogInformation("[DEBUG_LOG] Applying NeedsMyReviewOnly filter for user: {UserId}", criteria.CurrentUserId);
            
            if (userGroupMemberships != null && userGroupMemberships.Count > 0)
            {
                _logger.LogInformation("[DEBUG_LOG] User is member of {GroupCount} groups: {Groups}", 
                    userGroupMemberships.Count, string.Join(", ", userGroupMemberships));
            }
            else
            {
                _logger.LogInformation("[DEBUG_LOG] User has no group memberships or groups list is null/empty");
            }

            filtered = filtered.Where(pr => 
            {
                _logger.LogInformation("[DEBUG_LOG] Evaluating PR #{PrId}: {Title}", pr.Id, pr.Title);
                
                // Log all reviewers for this PR
                foreach (var reviewer in pr.Reviewers)
                {
                    _logger.LogInformation("[DEBUG_LOG] PR #{PrId} reviewer: {Name} (ID: {Id}, IsGroup: {IsGroup}, Vote: {Vote})", 
                        pr.Id, reviewer.DisplayName, reviewer.Id, reviewer.IsGroup, reviewer.Vote);
                }
                
                // Check if user is directly assigned as reviewer and hasn't voted
                var directReviewNeeded = pr.Reviewers.Any(r => 
                    !r.IsGroup &&
                    r.Id.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase) && 
                    (r.Vote.Equals("No vote", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(r.Vote)));
                
                _logger.LogInformation("[DEBUG_LOG] PR #{PrId} direct review needed: {DirectReviewNeeded}", pr.Id, directReviewNeeded);
                
                // Check if user is in a group that is assigned as reviewer and no one from the group has reviewed
                var groupReviewNeeded = false;
                if (userGroupMemberships != null && userGroupMemberships.Count > 0)
                {
                    foreach (var reviewer in pr.Reviewers.Where(r => r.IsGroup))
                    {
                        var userInGroup = userGroupMemberships.Contains(reviewer.DisplayName, StringComparer.OrdinalIgnoreCase);
                        var groupHasNotVoted = reviewer.Vote.Equals("No vote", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(reviewer.Vote);
                        
                        _logger.LogInformation("[DEBUG_LOG] PR #{PrId} group reviewer {GroupName}: UserInGroup={UserInGroup}, GroupHasNotVoted={GroupHasNotVoted}", 
                            pr.Id, reviewer.DisplayName, userInGroup, groupHasNotVoted);
                            
                        if (userInGroup && groupHasNotVoted)
                        {
                            groupReviewNeeded = true;
                            break;
                        }
                    }
                }
                
                _logger.LogInformation("[DEBUG_LOG] PR #{PrId} group review needed: {GroupReviewNeeded}", pr.Id, groupReviewNeeded);
                
                var needsReview = directReviewNeeded || groupReviewNeeded;
                _logger.LogInformation("[DEBUG_LOG] PR #{PrId} final decision - needs review: {NeedsReview}", pr.Id, needsReview);
                
                return needsReview;
            });
        }

        // Groups Without Vote filter - show PRs where selected groups are reviewers but haven't voted
        if (criteria.EnableGroupsWithoutVoteFilter && criteria.SelectedGroupsWithoutVote.Any())
        {
            _logger.LogInformation("[DEBUG_LOG] Applying Groups Without Vote filter for {GroupCount} groups: {Groups}", 
                criteria.SelectedGroupsWithoutVote.Count, string.Join(", ", criteria.SelectedGroupsWithoutVote));

            filtered = filtered.Where(pr => 
            {
                // Check if any of the selected groups are reviewers on this PR and haven't voted
                var hasGroupWithoutVote = pr.Reviewers.Any(reviewer =>
                    reviewer.IsGroup && 
                    criteria.SelectedGroupsWithoutVote.Contains(reviewer.DisplayName, StringComparer.OrdinalIgnoreCase) &&
                    (reviewer.Vote.Equals("No vote", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(reviewer.Vote)));

                _logger.LogInformation("[DEBUG_LOG] PR #{PrId} has group without vote: {HasGroupWithoutVote}", pr.Id, hasGroupWithoutVote);
                
                return hasGroupWithoutVote;
            });
        }

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

    /// <summary>
    /// Comprehensive method to determine if a PR belongs to the current user
    /// Uses the CreatorId field for direct ID matching with fallback strategies
    /// </summary>
    private bool IsCurrentUserPR(PullRequestInfo pr, FilterCriteria criteria, IEnumerable<PullRequestInfo> allPullRequests)
    {
        _logger.LogInformation("[DEBUG_LOG] Checking if PR #{PrId} '{Title}' belongs to user {UserId}", pr.Id, pr.Title, criteria.CurrentUserId);
        _logger.LogInformation("[DEBUG_LOG] PR Creator: '{Creator}', CreatorId: '{CreatorId}'", pr.Creator, pr.CreatorId);
        _logger.LogInformation("[DEBUG_LOG] Configured UserDisplayName: '{UserDisplayName}'", criteria.UserDisplayName);
        
        // Strategy 1: Direct CreatorId match (most reliable)
        if (!string.IsNullOrWhiteSpace(pr.CreatorId) && pr.CreatorId.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[DEBUG_LOG] Match found - Direct CreatorId match");
            return true;
        }
        
        // Strategy 2: Use configured UserDisplayName if available
        if (!string.IsNullOrWhiteSpace(criteria.UserDisplayName))
        {
            _logger.LogInformation("[DEBUG_LOG] Using configured UserDisplayName: '{UserDisplayName}'", criteria.UserDisplayName);
            if (pr.Creator.Equals(criteria.UserDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[DEBUG_LOG] Match found - Configured display name match with creator");
                return true;
            }
        }
        
        // Strategy 3: Direct ID match with creator (in case Creator field stores IDs)
        if (pr.Creator.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[DEBUG_LOG] Match found - Direct ID match with creator");
            return true;
        }
        
        // Strategy 4: Find current user's display name from reviewer data and match with creator
        var currentUserDisplayName = allPullRequests
            .SelectMany(p => p.Reviewers)
            .Where(r => !r.IsGroup && r.Id.Equals(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.DisplayName)
            .FirstOrDefault();
            
        if (!string.IsNullOrWhiteSpace(currentUserDisplayName))
        {
            _logger.LogInformation("[DEBUG_LOG] Found current user display name from reviewer data: '{DisplayName}'", currentUserDisplayName);
            if (pr.Creator.Equals(currentUserDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[DEBUG_LOG] Match found - Reviewer display name match with creator");
                return true;
            }
        }
        else
        {
            _logger.LogInformation("[DEBUG_LOG] Could not find current user display name in reviewer data - trying alternative approaches");
            
            // Log all unique reviewer IDs and names to help debug
            var allReviewers = allPullRequests
                .SelectMany(p => p.Reviewers)
                .Where(r => !r.IsGroup)
                .GroupBy(r => r.Id)
                .Select(g => new { Id = g.Key, DisplayName = g.First().DisplayName })
                .ToList();
            
            _logger.LogInformation("[DEBUG_LOG] All reviewer IDs found: {ReviewerCount} unique reviewers", allReviewers.Count);
            foreach (var reviewer in allReviewers.Take(10)) // Log first 10 to avoid spam
            {
                _logger.LogInformation("[DEBUG_LOG] Reviewer: '{DisplayName}' (ID: '{Id}')", reviewer.DisplayName, reviewer.Id);
            }
        }
        
        // Strategy 5: Check if current user ID appears anywhere in the creator string (email scenarios)
        if (criteria.CurrentUserId.Contains("@") && pr.Creator.Contains(criteria.CurrentUserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[DEBUG_LOG] Match found - Email substring match");
            return true;
        }
        
        // Strategy 6: Try to extract name parts and match
        if (criteria.CurrentUserId.Contains("@"))
        {
            var emailPrefix = criteria.CurrentUserId.Split('@')[0];
            if (pr.Creator.Contains(emailPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[DEBUG_LOG] Match found - Email prefix match with '{EmailPrefix}'", emailPrefix);
                return true;
            }
        }
        
        // Strategy 7: Enhanced partial name matching - try common name formats
        if (!string.IsNullOrWhiteSpace(criteria.UserDisplayName))
        {
            var userNameParts = criteria.UserDisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (userNameParts.Length >= 2)
            {
                var firstName = userNameParts[0];
                var lastName = userNameParts[^1]; // Last element
                
                // Check if creator contains both first and last name
                if (pr.Creator.Contains(firstName, StringComparison.OrdinalIgnoreCase) && 
                    pr.Creator.Contains(lastName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[DEBUG_LOG] Match found - Partial name match with '{FirstName}' and '{LastName}'", firstName, lastName);
                    return true;
                }
                
                // Check common enterprise format: "LASTNAME Firstname"
                var enterpriseFormat = $"{lastName.ToUpper()} {firstName}";
                if (pr.Creator.Equals(enterpriseFormat, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[DEBUG_LOG] Match found - Enterprise format match with '{EnterpriseFormat}'", enterpriseFormat);
                    return true;
                }
            }
        }
        
        _logger.LogInformation("[DEBUG_LOG] No match found for PR #{PrId} - this may indicate the user never appears as a reviewer or the PR is not theirs", pr.Id);
        return false;
    }
}