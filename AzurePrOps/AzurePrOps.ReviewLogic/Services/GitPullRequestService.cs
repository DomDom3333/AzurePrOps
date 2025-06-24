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
        Console.WriteLine($"LoadDiff called for repo {repositoryPath}, PR {pullRequestId}");

        try
        {
            using Repository repo = new Repository(repositoryPath);
            // convention: PR branch named "pr/{id}"
            Branch? prBranch = repo.Branches.FirstOrDefault(b => b.FriendlyName.EndsWith($"/pr/{pullRequestId}", StringComparison.OrdinalIgnoreCase));
            if (prBranch == null)
            {
                Console.WriteLine($"PR branch pr/{pullRequestId} not found - looking for alternate formats");
                // Try alternate branch naming formats
                prBranch = repo.Branches.FirstOrDefault(b => 
                    b.FriendlyName.Contains($"PR-{pullRequestId}", StringComparison.OrdinalIgnoreCase) ||
                    b.FriendlyName.Contains($"PR{pullRequestId}", StringComparison.OrdinalIgnoreCase) ||
                    b.FriendlyName.Contains($"pull/{pullRequestId}", StringComparison.OrdinalIgnoreCase));

                if (prBranch == null)
                    throw new ArgumentException($"Could not find any branch for PR {pullRequestId}");
            }

            Console.WriteLine($"Found PR branch: {prBranch.FriendlyName}");

            Commit? headCommit = repo.Head.Tip;
            Commit? prCommit = prBranch.Tip;
            Console.WriteLine($"Head commit: {headCommit.Sha}, PR commit: {prCommit.Sha}");

            TreeChanges? changes = repo.Diff.Compare<TreeChanges>(headCommit.Tree, prCommit.Tree);
            Console.WriteLine($"Found {changes.Count()} changed files");
            Commit parent;
            if (changes.Count() == 0)
            {
                // Try comparing with the PR branch's parent instead
                parent = prCommit.Parents.FirstOrDefault();
                if (parent != null)
                {
                    Console.WriteLine($"No changes found with head, trying PR parent: {parent.Sha}");
                    changes = repo.Diff.Compare<TreeChanges>(parent.Tree, prCommit.Tree);
                    Console.WriteLine($"Found {changes.Count()} changed files with parent");
                }
            }

            TreeEntryChanges? firstChange = changes.FirstOrDefault();
            if (firstChange == null)
            {
                Console.WriteLine("No changes found in the PR");
                return ("[No changes found in this PR]\n", "[No changes found in this PR]\n");
            }

            Console.WriteLine($"First changed file: {firstChange.Path}, status: {firstChange.Status}");

            // Get the correct versions based on change type
            string oldText = "[Could not retrieve original content]\n";
            string newText = "[Could not retrieve modified content]\n";

            parent = prCommit.Parents.FirstOrDefault() ?? headCommit;

            // For deleted files
            if (firstChange.Status == ChangeKind.Deleted)
            {
                Console.WriteLine("File was deleted");
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
                    Console.WriteLine($"Error retrieving deleted file: {ex.Message}");
                }
            }
            // For new files
            else if (firstChange.Status == ChangeKind.Added)
            {
                Console.WriteLine("File was added");
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
                    Console.WriteLine($"Error retrieving added file: {ex.Message}");
                }
            }
            // For modified files
            else
            {
                Console.WriteLine("File was modified");
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
                    Console.WriteLine($"Error retrieving modified file: {ex.Message}");
                }
            }

            Console.WriteLine($"Retrieved content - Old: {oldText.Length} bytes, New: {newText.Length} bytes");
            return (oldText, newText);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in LoadDiff: {ex.Message}");
            return ("[Error retrieving diff: " + ex.Message + "]\n", "[Error retrieving diff: " + ex.Message + "]\n");
        }
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

    public Task<IReadOnlyList<FileDiff>> GetPullRequestDiffByFilesAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken)
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
        var sb = new System.Text.StringBuilder();
        var sanitizedPath = filePath.TrimStart('/');
        sb.AppendLine($"diff --git a/{sanitizedPath} b/{sanitizedPath}");

        switch (changeType.ToLowerInvariant())
        {
            case "add":
                sb.AppendLine("new file mode 100644");
                sb.AppendLine($"index 0000000..{(newId ?? "0000000").Substring(0, Math.Min(7, (newId ?? "0000000").Length))}");
                sb.AppendLine("--- /dev/null");
                sb.AppendLine($"+++ b/{sanitizedPath}");
                sb.AppendLine("@@ -0,0 +1," + CountLines(newContent) + " @@");
                foreach (var line in newContent.Split('\n'))
                    sb.AppendLine("+" + line);
                break;
            case "delete":
                sb.AppendLine("deleted file mode 100644");
                sb.AppendLine($"index {(oldId ?? "1234567").Substring(0, Math.Min(7, (oldId ?? "1234567").Length))}..0000000");
                sb.AppendLine($"--- a/{sanitizedPath}");
                sb.AppendLine("+++ /dev/null");
                sb.AppendLine("@@ -1," + CountLines(oldContent) + " +0,0 @@");
                foreach (var line in oldContent.Split('\n'))
                    sb.AppendLine("-" + line);
                break;
            default:
                GenerateSimpleDiff(sb, sanitizedPath, oldContent, newContent, oldId, newId);
                break;
        }

        return sb.ToString();
    }

    private void GenerateSimpleDiff(System.Text.StringBuilder sb, string filePath, string oldContent, string newContent, string? oldId, string? newId)
    {
        var oldIndex = string.IsNullOrEmpty(oldId) ? "0000000" : oldId.Substring(0, Math.Min(7, oldId.Length));
        var newIndex = string.IsNullOrEmpty(newId) ? "0000000" : newId.Substring(0, Math.Min(7, newId.Length));

        sb.AppendLine($"index {oldIndex}..{newIndex} 100644");
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        var oldLines = oldContent.Split('\n');
        var newLines = newContent.Split('\n');

        sb.AppendLine($"@@ -1,{oldLines.Length} +1,{newLines.Length} @@");

        int commonPrefix = 0;
        int minLength = Math.Min(oldLines.Length, newLines.Length);
        while (commonPrefix < minLength && oldLines[commonPrefix] == newLines[commonPrefix])
        {
            sb.AppendLine(" " + oldLines[commonPrefix]);
            commonPrefix++;
        }
        for (int i = commonPrefix; i < oldLines.Length; i++)
            sb.AppendLine("-" + oldLines[i]);
        for (int i = commonPrefix; i < newLines.Length; i++)
            sb.AppendLine("+" + newLines[i]);
    }

    private int CountLines(string text) => string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;

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