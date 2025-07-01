using AzurePrOps.ReviewLogic.Models;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.ReviewLogic.Services;

public class GitPullRequestService : IPullRequestService
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<GitPullRequestService>();
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
        _logger.LogDebug("LoadDiff called for repo {Repo}, PR {Id}", repositoryPath, pullRequestId);

        try
        {
            using Repository repo = new Repository(repositoryPath);
            // convention: PR branch named "pr/{id}"
            Branch? prBranch = repo.Branches.FirstOrDefault(b => b.FriendlyName.EndsWith($"/pr/{pullRequestId}", StringComparison.OrdinalIgnoreCase));
            if (prBranch == null)
            {
                _logger.LogDebug("PR branch pr/{Id} not found - looking for alternate formats", pullRequestId);
                // Try alternate branch naming formats
                prBranch = repo.Branches.FirstOrDefault(b => 
                    b.FriendlyName.Contains($"PR-{pullRequestId}", StringComparison.OrdinalIgnoreCase) ||
                    b.FriendlyName.Contains($"PR{pullRequestId}", StringComparison.OrdinalIgnoreCase) ||
                    b.FriendlyName.Contains($"pull/{pullRequestId}", StringComparison.OrdinalIgnoreCase));

                if (prBranch == null)
                    throw new ArgumentException($"Could not find any branch for PR {pullRequestId}");
            }

            _logger.LogDebug("Found PR branch: {Branch}", prBranch.FriendlyName);

            Commit? headCommit = repo.Head.Tip;
            Commit? prCommit = prBranch.Tip;
            _logger.LogDebug("Head commit: {Head}, PR commit: {PR}", headCommit.Sha, prCommit.Sha);

            TreeChanges? changes = repo.Diff.Compare<TreeChanges>(headCommit.Tree, prCommit.Tree);
            _logger.LogDebug("Found {Count} changed files", changes.Count());
            Commit? parent;
            if (changes.Count() == 0)
            {
                // Try comparing with the PR branch's parent instead
                parent = prCommit.Parents.FirstOrDefault();
                if (parent != null)
                {
                    _logger.LogDebug("No changes found with head, trying PR parent: {Parent}", parent.Sha);
                    changes = repo.Diff.Compare<TreeChanges>(parent.Tree, prCommit.Tree);
                    _logger.LogDebug("Found {Count} changed files with parent", changes.Count());
                }
            }

            TreeEntryChanges? firstChange = changes.FirstOrDefault();
            if (firstChange == null)
            {
                _logger.LogDebug("No changes found in the PR");
                return ("[No changes found in this PR]\n", "[No changes found in this PR]\n");
            }

            _logger.LogDebug("First changed file: {Path}, status: {Status}", firstChange.Path, firstChange.Status);

            // Get the correct versions based on change type
            string oldText = "[Could not retrieve original content]\n";
            string newText = "[Could not retrieve modified content]\n";

            parent = prCommit.Parents.FirstOrDefault() ?? headCommit;

            // For deleted files
            if (firstChange.Status == ChangeKind.Deleted)
            {
                _logger.LogDebug("File was deleted");
                try
                {
                    // Get content from parent commit
                    var parentEntry = parent[firstChange.Path];
                    if (parentEntry != null && parentEntry.TargetType == TreeEntryTargetType.Blob)
                    {
                        Blob? oldBlob = repo.Lookup<Blob>(parentEntry.Target.Id);
                        oldText = oldBlob?.GetContentText() ?? "[Could not read file content]\n";
                        newText = "[FILE DELETED]\n";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving deleted file");
                }
            }
            // For new files
            else if (firstChange.Status == ChangeKind.Added)
            {
                _logger.LogDebug("File was added");
                try
                {
                    // Get content from PR commit
                    var newEntry = prCommit[firstChange.Path];
                    if (newEntry != null && newEntry.TargetType == TreeEntryTargetType.Blob)
                    {
                        Blob? newBlob = repo.Lookup<Blob>(newEntry.Target.Id);
                        oldText = "[FILE ADDED]\n";
                        newText = newBlob?.GetContentText() ?? "[Could not read file content]\n";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving added file");
                }
            }
            // For modified files
            else
            {
                _logger.LogDebug("File was modified");
                try
                {
                    // Get old content from parent commit
                    var parentEntry = parent[firstChange.Path];
                    if (parentEntry != null && parentEntry.TargetType == TreeEntryTargetType.Blob)
                    {
                        Blob? oldBlob = repo.Lookup<Blob>(parentEntry.Target.Id);
                        oldText = oldBlob?.GetContentText() ?? "[Could not read original file content]\n";
                    }

                    // Get new content from PR commit
                    var newEntry = prCommit[firstChange.Path];
                    if (newEntry != null && newEntry.TargetType == TreeEntryTargetType.Blob)
                    {
                        Blob? newBlob = repo.Lookup<Blob>(newEntry.Target.Id);
                        newText = newBlob?.GetContentText() ?? "[Could not read modified file content]\n";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving modified file");
                }
            }

            _logger.LogDebug("Retrieved content - Old: {OldBytes} bytes, New: {NewBytes} bytes", oldText.Length, newText.Length);
            return (oldText, newText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in LoadDiff");
            return ("[Error retrieving diff: " + ex.Message + "]\n", "[Error retrieving diff: " + ex.Message + "]\n");
        }
    }

    public Task<IReadOnlyList<FileDiff>> GetPullRequestDiffAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken,
        string? baseCommit = null,
        string? diffCommit = null)
    {
        // This implementation only works with local git repository
        // See AzureDevOpsPullRequestService for a remote implementation
        ReportError("This method is not implemented in the base GitPullRequestService. Please use AzureDevOpsPullRequestService instead.");
        return Task.FromResult<IReadOnlyList<FileDiff>>(new List<FileDiff>());
    }

    public Task<IReadOnlyList<FileDiff>> GetPullRequestDiffByFilesAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken,
        string? baseCommit = null,
        string? diffCommit = null)
    {
        var diffs = new List<FileDiff>();
        try
        {
            using var repo = new Repository(repositoryId);
            var prBranch = repo.Branches.FirstOrDefault(b => b.FriendlyName.EndsWith($"/pr/{pullRequestId}", StringComparison.OrdinalIgnoreCase))
                ?? repo.Branches.FirstOrDefault(b =>
                    b.FriendlyName.Contains($"PR-{pullRequestId}", StringComparison.OrdinalIgnoreCase) ||
                    b.FriendlyName.Contains($"PR{pullRequestId}", StringComparison.OrdinalIgnoreCase) ||
                    b.FriendlyName.Contains($"pull/{pullRequestId}", StringComparison.OrdinalIgnoreCase));
            if (prBranch == null)
            {
                ReportError($"Could not find any branch for PR {pullRequestId}");
                return Task.FromResult<IReadOnlyList<FileDiff>>(diffs);
            }

            var prCommit = prBranch.Tip;
            var parent = prCommit.Parents.FirstOrDefault() ?? repo.Head.Tip;

            var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, prCommit.Tree);
            foreach (var change in changes)
            {
                string oldContent = string.Empty;
                string newContent = string.Empty;
                string? oldId = null;
                string? newId = null;

                if (change.Status != ChangeKind.Added)
                {
                    var parentEntry = parent[change.Path];
                    if (parentEntry != null && parentEntry.TargetType == TreeEntryTargetType.Blob)
                    {
                        var blob = repo.Lookup<Blob>(parentEntry.Target.Id);
                        oldId = parentEntry.Target.Id.Sha;
                        oldContent = blob?.GetContentText() ?? string.Empty;
                    }
                }

                if (change.Status != ChangeKind.Deleted)
                {
                    var newEntry = prCommit[change.Path];
                    if (newEntry != null && newEntry.TargetType == TreeEntryTargetType.Blob)
                    {
                        var blob = repo.Lookup<Blob>(newEntry.Target.Id);
                        newId = newEntry.Target.Id.Sha;
                        newContent = blob?.GetContentText() ?? string.Empty;
                    }
                }

                var diffText = GenerateUnifiedDiff(change.Path, change.Status.ToString().ToLowerInvariant(), oldContent, newContent, oldId, newId);
                diffs.Add(new FileDiff(change.Path, diffText, oldContent, newContent));
            }
        }
        catch (Exception ex)
        {
            ReportError($"Error computing diff: {ex.Message}");
        }

        return Task.FromResult<IReadOnlyList<FileDiff>>(diffs);
    }

    private string GenerateUnifiedDiff(string filePath, string changeType, string oldContent, string newContent, string? oldId = null, string? newId = null)
    {
        return DiffHelper.GenerateUnifiedDiff(filePath, oldContent, newContent, changeType);
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