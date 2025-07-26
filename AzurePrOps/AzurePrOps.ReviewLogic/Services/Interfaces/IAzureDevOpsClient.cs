using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface IAzureDevOpsClient
{
    Task<IReadOnlyList<CommentThread>> GetPullRequestThreadsAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken);

    Task<CommentThread> CreatePullRequestThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string filePath,
        int lineNumber,
        string content,
        string personalAccessToken);

    Task<CommentThread> AddCommentToThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        int parentCommentId,
        string content,
        string personalAccessToken);

    Task ResolveThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        string personalAccessToken);
}
