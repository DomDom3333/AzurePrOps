using AzurePrOps.ReviewLogic.Models;
using LibGit2Sharp;

namespace AzurePrOps.ReviewLogic.Services;

public class GitPullRequestService : IPullRequestService
{
    private Action<string>? _errorHandler;

    public GitPullRequestService()
    {
    }

    public void SetErrorHandler(Action<string> handler)
    {
        _errorHandler = handler;
    }

    private void ReportError(string message)
    {
        _errorHandler?.Invoke(message);
    }

    public (string oldText, string newText) LoadDiff(string repositoryPath, int pullRequestId)
    {
        using Repository repo = new Repository(repositoryPath);
        // convention: PR branch named "pr/{id}"
        Branch? prBranch = repo.Branches.FirstOrDefault(b => b.FriendlyName.EndsWith($"/pr/{pullRequestId}", StringComparison.OrdinalIgnoreCase));
        if (prBranch == null)
            throw new ArgumentException($"PR branch pr/{pullRequestId} not found.");

        Commit? headCommit   = repo.Head.Tip;
        Commit? prCommit     = prBranch.Tip;
        TreeChanges? changes      = repo.Diff.Compare<TreeChanges>(headCommit.Tree, prCommit.Tree);
        TreeEntryChanges? firstChange  = changes.FirstOrDefault();
        if (firstChange == null)
            return (string.Empty, string.Empty);

        Commit parent = prCommit.Parents.FirstOrDefault() ?? headCommit;
        Blob? oldBlob = repo.Lookup<Blob>(parent[firstChange.Path]?.Target.Id);
        Blob? newBlob = repo.Lookup<Blob>(prCommit[firstChange.Path]?.Target.Id);

        string oldText = oldBlob?.GetContentText() ?? string.Empty;
        string newText = newBlob?.GetContentText() ?? string.Empty;

        return (oldText, newText);
    }

    public Task<IReadOnlyList<FileDiff>> GetPullRequestDiffAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken)
    {
        // This implementation only works with local git repository
        // See AzureDevOpsPullRequestService for a remote implementation
        ReportError("This method is not implemented in the base GitPullRequestService. Please use AzureDevOpsPullRequestService instead.");
        return Task.FromResult<IReadOnlyList<FileDiff>>(new List<FileDiff>());
    }

    public Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(
        string organization,
        string project,
        string repositoryId,
        string personalAccessToken)
    {
        // This implementation only works with local git repository
        // See AzureDevOpsPullRequestService for a remote implementation
        ReportError("This method is not implemented in the base GitPullRequestService. Please use AzureDevOpsPullRequestService instead.");
        return Task.FromResult<IReadOnlyList<PullRequestInfo>>(new List<PullRequestInfo>());
    }
}