using AzurePrOps.AzureConnection.Models;
using AzurePrOps.Models.FilteringAndSorting;
using AzurePrOps.Services;

namespace AzurePrOps.Tests;

public class PullRequestFilteringSortingServiceTests
{
    private readonly PullRequestFilteringSortingService _service = new();

    [Fact]
    public void ApplyFiltersAndSorting_GroupsWithoutVoteFilter_UsesSelectedGroupsWithoutVote()
    {
        var criteria = new FilterCriteria
        {
            EnableGroupsWithoutVoteFilter = true,
            SelectedGroupsWithoutVote = new List<string> { "API Reviewers" }
        };

        var sort = new SortCriteria();
        var pullRequests = new[]
        {
            CreatePullRequest(1, new ReviewerInfo("g1", "API Reviewers", "No vote", true)),
            CreatePullRequest(2, new ReviewerInfo("g2", "UX Reviewers", "No vote", true))
        };

        var result = _service.ApplyFiltersAndSorting(pullRequests, criteria, sort, Array.Empty<string>()).ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public void ApplyFiltersAndSorting_GroupsWithoutVoteFilter_IgnoresGroupsThatAlreadyVoted()
    {
        var criteria = new FilterCriteria
        {
            EnableGroupsWithoutVoteFilter = true,
            SelectedGroupsWithoutVote = new List<string> { "API Reviewers" }
        };

        var sort = new SortCriteria();
        var pullRequests = new[]
        {
            CreatePullRequest(1, new ReviewerInfo("g1", "API Reviewers", "Approved", true)),
            CreatePullRequest(2, new ReviewerInfo("g1", "API Reviewers", "No vote", true))
        };

        var result = _service.ApplyFiltersAndSorting(pullRequests, criteria, sort, Array.Empty<string>()).ToList();

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    private static PullRequestInfo CreatePullRequest(int id, params ReviewerInfo[] reviewers)
    {
        return new PullRequestInfo(
            id,
            $"PR {id}",
            "creator",
            "creator-id",
            DateTime.UtcNow,
            "active",
            reviewers,
            "feature/branch",
            "main",
            $"https://example.local/pr/{id}");
    }
}
