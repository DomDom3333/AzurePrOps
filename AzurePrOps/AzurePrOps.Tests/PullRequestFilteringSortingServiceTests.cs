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


    [Fact]
    public void ApplyFiltersAndSorting_LargeSequenceWithMultipleFilters_EnumeratesSourceOnce()
    {
        var criteria = new FilterCriteria
        {
            MyPullRequestsOnly = true,
            AssignedToMeOnly = true,
            NeedsMyReviewOnly = true,
            GlobalSearchText = "PR"
        };

        var sort = new SortCriteria();
        const string currentUserId = "user-1";

        var baseData = Enumerable.Range(1, 20000)
            .Select(i => new PullRequestInfo(
                i,
                $"PR {i}",
                i % 2 == 0 ? "Creator" : null!,
                i % 2 == 0 ? currentUserId : $"other-{i}",
                DateTime.UtcNow.AddMinutes(-i),
                "active",
                new[]
                {
                    new ReviewerInfo(currentUserId, "Current User", "No vote", false)
                },
                i % 3 == 0 ? "feature/x" : null!,
                "main",
                $"https://example.local/pr/{i}"))
            .ToList();

        var countingEnumerable = new CountingEnumerable<PullRequestInfo>(baseData);

        var result = _service.ApplyFiltersAndSorting(
                countingEnumerable,
                criteria,
                sort,
                Array.Empty<string>(),
                currentUserId)
            .ToList();

        Assert.NotEmpty(result);
        Assert.Equal(1, countingEnumerable.EnumerationCount);
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


internal sealed class CountingEnumerable<T> : IEnumerable<T>
{
    private readonly IEnumerable<T> _inner;

    public CountingEnumerable(IEnumerable<T> inner)
    {
        _inner = inner;
    }

    public int EnumerationCount { get; private set; }

    public IEnumerator<T> GetEnumerator()
    {
        EnumerationCount++;
        return _inner.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
