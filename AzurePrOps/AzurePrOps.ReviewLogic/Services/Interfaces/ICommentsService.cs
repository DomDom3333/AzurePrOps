using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public interface ICommentsService
{
    Task<IReadOnlyList<CommentThread>> GetThreadsAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken);

    Task<CommentThread> CreateThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string filePath,
        int lineNumber,
        string content,
        string personalAccessToken);

    Task<CommentThread> ReplyToThreadAsync(
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

    Task UpdateThreadStatusAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        bool resolved,
        string personalAccessToken);

    void SetErrorHandler(Action<string> handler);
}
