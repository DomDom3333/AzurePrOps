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

    [Fact]
    public void ApplyFiltersAndSorting_PersonalFilters_MatchAcrossMixedIdentityFormats()
    {
        var currentUserIdentity = " John.Doe@Contoso.com ";
        var pullRequests = new[]
        {
            new PullRequestInfo(
                1,
                "PR 1",
                "john.doe",
                "  JOHN.DOE  ",
                DateTime.UtcNow,
                "active",
                new[] { new ReviewerInfo("john.doe@contoso.com", "John Doe", "No vote", false) },
                "feature/one",
                "main",
                "https://example.local/pr/1"),
            new PullRequestInfo(
                2,
                "PR 2",
                "someone else",
                "other-user",
                DateTime.UtcNow,
                "active",
                new[] { new ReviewerInfo("DOMAIN\\John.Doe", "John Doe", "No vote", false) },
                "feature/two",
                "main",
                "https://example.local/pr/2"),
            new PullRequestInfo(
                3,
                "PR 3",
                "someone else",
                "other-user-2",
                DateTime.UtcNow,
                "active",
                new[] { new ReviewerInfo(" jane.smith@contoso.com ", "Jane Smith", "No vote", false) },
                "feature/three",
                "main",
                "https://example.local/pr/3")
        };

        var sort = new SortCriteria();

        var myPrOnly = _service.ApplyFiltersAndSorting(
                pullRequests,
                new FilterCriteria { MyPullRequestsOnly = true },
                sort,
                Array.Empty<string>(),
                currentUserIdentity)
            .Select(pr => pr.Id)
            .ToList();

        var assignedToMeOnly = _service.ApplyFiltersAndSorting(
                pullRequests,
                new FilterCriteria { AssignedToMeOnly = true },
                sort,
                Array.Empty<string>(),
                currentUserIdentity)
            .Select(pr => pr.Id)
            .ToList();

        var needsMyReviewOnly = _service.ApplyFiltersAndSorting(
                pullRequests,
                new FilterCriteria { NeedsMyReviewOnly = true },
                sort,
                Array.Empty<string>(),
                currentUserIdentity)
            .Select(pr => pr.Id)
            .ToList();

        var excludeMyPrs = _service.ApplyFiltersAndSorting(
                pullRequests,
                new FilterCriteria { ExcludeMyPullRequests = true },
                sort,
                Array.Empty<string>(),
                currentUserIdentity)
            .Select(pr => pr.Id)
            .ToList();

        Assert.Equal(new[] { 1 }, myPrOnly);
        Assert.Equal(new[] { 1, 2 }, assignedToMeOnly.OrderBy(id => id));
        Assert.Equal(new[] { 1, 2 }, needsMyReviewOnly.OrderBy(id => id));
        Assert.Equal(new[] { 2, 3 }, excludeMyPrs.OrderBy(id => id));
    }

    [Fact]
    public void ApplyFiltersAndSorting_NeedsMyReviewOnly_ExcludesWhenVoteAlreadyPresentEvenIfIdentityMatches()
    {
        var currentUserIdentity = "john.doe@contoso.com";
        var pullRequests = new[]
        {
            CreatePullRequest(1, new ReviewerInfo("DOMAIN\\john.doe", "John Doe", "Approved", false)),
            CreatePullRequest(2, new ReviewerInfo("john.doe", "John Doe", "No vote", false))
        };

        var result = _service.ApplyFiltersAndSorting(
                pullRequests,
                new FilterCriteria { NeedsMyReviewOnly = true },
                new SortCriteria(),
                Array.Empty<string>(),
                currentUserIdentity)
            .ToList();

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
