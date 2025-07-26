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

    Task UpdateThreadStatusAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        string status,
        string personalAccessToken);

    Task SetPullRequestVoteAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string reviewerId,
        int vote,
        string personalAccessToken);

    Task SetPullRequestDraftAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        bool isDraft,
        string personalAccessToken);

    Task CompletePullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        MergeOptions mergeOptions,
        string personalAccessToken);

    Task AbandonPullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken);
}
