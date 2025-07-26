using AzurePrOps.ReviewLogic.Models;

namespace AzurePrOps.ReviewLogic.Services;

public class CommentsService : ICommentsService
{
    private readonly IAzureDevOpsClient _client;
    private Action<string>? _errorHandler;

    public CommentsService(IAzureDevOpsClient client)
    {
        _client = client;
    }

    public void SetErrorHandler(Action<string> handler)
    {
        _errorHandler = handler;
        // Client may implement its own error handling separately
    }

    private void ReportError(string message)
    {
        _errorHandler?.Invoke(message);
    }

    private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> func)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await func();
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(attempt * 100);
            }
            catch (Exception ex)
            {
                ReportError(ex.Message);
                throw;
            }
        }
    }

    private async Task ExecuteWithRetry(Func<Task> func)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await func();
                return;
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(attempt * 100);
            }
            catch (Exception ex)
            {
                ReportError(ex.Message);
                throw;
            }
        }
    }

    public Task<IReadOnlyList<CommentThread>> GetThreadsAsync(string organization, string project, string repositoryId, int pullRequestId, string personalAccessToken)
        => ExecuteWithRetry(() => _client.GetPullRequestThreadsAsync(organization, project, repositoryId, pullRequestId, personalAccessToken));

    public Task<CommentThread> CreateThreadAsync(string organization, string project, string repositoryId, int pullRequestId, string filePath, int lineNumber, string content, string personalAccessToken)
        => ExecuteWithRetry(() => _client.CreatePullRequestThreadAsync(organization, project, repositoryId, pullRequestId, filePath, lineNumber, content, personalAccessToken));

    public Task<CommentThread> ReplyToThreadAsync(string organization, string project, string repositoryId, int pullRequestId, int threadId, int parentCommentId, string content, string personalAccessToken)
        => ExecuteWithRetry(() => _client.AddCommentToThreadAsync(organization, project, repositoryId, pullRequestId, threadId, parentCommentId, content, personalAccessToken));

    public Task ResolveThreadAsync(string organization, string project, string repositoryId, int pullRequestId, int threadId, string personalAccessToken)
        => UpdateThreadStatusAsync(organization, project, repositoryId, pullRequestId, threadId, true, personalAccessToken);

    public Task UpdateThreadStatusAsync(string organization, string project, string repositoryId, int pullRequestId, int threadId, bool resolved, string personalAccessToken)
        => ExecuteWithRetry(() => _client.UpdateThreadStatusAsync(
            organization,
            project,
            repositoryId,
            pullRequestId,
            threadId,
            resolved ? "closed" : "active",
            personalAccessToken));
}
