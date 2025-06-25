using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface IPullRequestService
{
    (string oldText, string newText) LoadDiff(string repositoryPath, int pullRequestId);
    Task<IReadOnlyList<FileDiff>> GetPullRequestDiffAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken,
        string? baseCommit = null,
        string? diffCommit = null);

    Task<IReadOnlyList<FileDiff>> GetPullRequestDiffByFilesAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken,
        string? baseCommit = null,
        string? diffCommit = null);
    Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(string organization, string project, string repositoryId, string personalAccessToken);
    void SetErrorHandler(Action<string> handler);
}