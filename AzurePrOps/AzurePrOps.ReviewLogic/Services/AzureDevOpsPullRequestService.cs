using AzurePrOps.ReviewLogic.Models;
using LibGit2Sharp;

namespace AzurePrOps.ReviewLogic.Services
{
    public class AzureDevOpsPullRequestService : IPullRequestService
    {
        private readonly HttpClient _httpClient;
        private const string AzureDevOpsBaseUrl = "https://dev.azure.com";
        private const string ApiVersion = "7.1";
        private Action<string>? _errorHandler;

        public AzureDevOpsPullRequestService()
        {
            _httpClient = new HttpClient();
        }

        public AzureDevOpsPullRequestService(HttpClient httpClient)
        {
            _httpClient = httpClient;
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

        public async Task<IReadOnlyList<FileDiff>> GetPullRequestDiffAsync(
            string organization,
            string project,
            string repositoryId,
            int pullRequestId,
            string personalAccessToken)
        {
            var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

            try
            {
                // First get the PR information to extract branch/commit information
                var encodedOrg = Uri.EscapeDataString(organization);
                var encodedProject = Uri.EscapeDataString(project);
                var encodedRepoId = Uri.EscapeDataString(repositoryId);

                var prUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}?api-version={ApiVersion}";
                using var prRequest = new HttpRequestMessage(HttpMethod.Get, prUri);
                prRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                using var prResponse = await _httpClient.SendAsync(prRequest);
                if (prResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    ReportError($"Pull request not found. URI: {prUri}");
                    throw new Exception($"Pull request not found. Please check if the pull request still exists.");
                }

                prResponse.EnsureSuccessStatusCode();
                using var prStream = await prResponse.Content.ReadAsStreamAsync();
                var prJson = await System.Text.Json.JsonDocument.ParseAsync(prStream);

                // Extract source and target branch names - removing 'refs/heads/' prefix if present
                string sourceBranch = prJson.RootElement
                    .GetProperty("sourceRefName")
                    .GetString() ?? string.Empty;
                if (sourceBranch.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase))
                {
                    sourceBranch = sourceBranch.Substring("refs/heads/".Length);
                }

                string targetBranch = prJson.RootElement
                    .GetProperty("targetRefName")
                    .GetString() ?? string.Empty;
                if (targetBranch.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase))
                {
                    targetBranch = targetBranch.Substring("refs/heads/".Length);
                }

                // Extract commit IDs for diff comparison
                string baseCommit = prJson.RootElement
                    .TryGetProperty("lastMergeTargetCommit", out var targetCommitProp) && 
                    targetCommitProp.TryGetProperty("commitId", out var targetCommitIdProp) ?
                    targetCommitIdProp.GetString() ?? string.Empty : string.Empty;

                string sourceCommit = prJson.RootElement
                    .TryGetProperty("lastMergeSourceCommit", out var sourceCommitProp) && 
                    sourceCommitProp.TryGetProperty("commitId", out var sourceCommitIdProp) ?
                    sourceCommitIdProp.GetString() ?? string.Empty : string.Empty;

                return await GetDirectDiffViaGitDiffsApi(encodedOrg, encodedProject, encodedRepoId, sourceBranch, targetBranch, sourceCommit, baseCommit, authToken);
            }
            catch (Exception ex)
            {
                ReportError($"Failed to get pull request diff: {ex.Message}");
                return new List<FileDiff>();
            }
        }

        private async Task<IReadOnlyList<FileDiff>> GetDirectDiffViaGitDiffsApi(
            string encodedOrg,
            string encodedProject,
            string encodedRepoId,
            string sourceBranch,
            string targetBranch,
            string sourceCommit,
            string baseCommit,
            string authToken)
        {
            try
            {
                // Verify repository exists first
                var repoCheckUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}?api-version=7.1";

                using var repoRequest = new HttpRequestMessage(HttpMethod.Get, repoCheckUri);
                repoRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                using var repoResponse = await _httpClient.SendAsync(repoRequest);
                if (repoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    ReportError($"Repository not found. Please verify the repository ID: {encodedRepoId}");
                    return new List<FileDiff>();
                }

                // Determine what to use for comparison - prefer specific commits if available
                string baseVersion = !string.IsNullOrEmpty(baseCommit) ? baseCommit : targetBranch;
                string targetVersion = !string.IsNullOrEmpty(sourceCommit) ? sourceCommit : sourceBranch;
                string baseVersionType = !string.IsNullOrEmpty(baseCommit) ? "commit" : "branch";
                string targetVersionType = !string.IsNullOrEmpty(sourceCommit) ? "commit" : "branch";

                // Build the direct Git Diffs API URL
                var diffUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/diffs/commits" + 
                              $"?baseVersion={Uri.EscapeDataString(baseVersion)}" + 
                              $"&baseVersionType={baseVersionType}" + 
                              $"&targetVersion={Uri.EscapeDataString(targetVersion)}" + 
                              $"&targetVersionType={targetVersionType}" + 
                              $"&diffCommonCommit=false" +
                              $"&$top=500" +
                              $"&api-version=7.1";

                using var diffRequest = new HttpRequestMessage(HttpMethod.Get, diffUri);
                diffRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                using var diffResponse = await _httpClient.SendAsync(diffRequest);

                if (!diffResponse.IsSuccessStatusCode)
                {
                    var errorContent = await diffResponse.Content.ReadAsStringAsync();
                    ReportError($"Error from Git Diffs API: {diffResponse.StatusCode} - {errorContent}");
                    return new List<FileDiff>();
                }

                var jsonContent = await diffResponse.Content.ReadAsStringAsync();
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);

                return await ProcessGitDiffsResponse(jsonDoc, encodedOrg, encodedProject, encodedRepoId, authToken);
            }
            catch (Exception ex)
            {
                ReportError($"Error in GetDirectDiffViaGitDiffsApi: {ex.Message}");
                return new List<FileDiff>();
            }
        }

        private async Task<IReadOnlyList<FileDiff>> ProcessGitDiffsResponse(
            System.Text.Json.JsonDocument jsonDoc, 
            string encodedOrg, 
            string encodedProject, 
            string encodedRepoId, 
            string authToken)
        {
            var fileDiffs = new List<FileDiff>();

            if (!jsonDoc.RootElement.TryGetProperty("changes", out var changesArray))
            {
                ReportError("No changes found in the diff response.");
                return fileDiffs;
            }

            foreach (var change in changesArray.EnumerateArray())
            {
                try
                {
                    if (!change.TryGetProperty("item", out var item) ||
                        !item.TryGetProperty("path", out var pathProp))
                    {
                        continue;
                    }

                    var filePath = pathProp.GetString() ?? string.Empty;
                    if (string.IsNullOrEmpty(filePath))
                    {
                        continue;
                    }

                    var changeType = change.TryGetProperty("changeType", out var changeTypeProp) ? 
                        changeTypeProp.GetString() ?? "edit" : "edit";

                    // Get the blob objectIds
                    var newObjectId = item.TryGetProperty("objectId", out var newIdProp) ? 
                        newIdProp.GetString() : null;

                    var oldObjectId = change.TryGetProperty("originalPath", out var _) ?
                        (item.TryGetProperty("originalObjectId", out var oldIdProp) ? 
                            oldIdProp.GetString() : null) : null;

                    // Fetch the content of both versions
                    var (oldContent, newContent) = await GetFileContents(
                        encodedOrg, encodedProject, encodedRepoId, filePath, oldObjectId, newObjectId, changeType, authToken);

                    // Add to our collection
                    fileDiffs.Add(new FileDiff(filePath, GenerateUnifiedDiff(filePath, changeType, oldContent, newContent), oldContent, newContent));
                }
                catch (Exception ex)
                {
                    ReportError($"Error processing change: {ex.Message}");
                    continue;
                }
            }

            return fileDiffs;
        }

        private async Task<(string oldContent, string newContent)> GetFileContents(
            string encodedOrg, 
            string encodedProject, 
            string encodedRepoId, 
            string filePath, 
            string? oldObjectId, 
            string? newObjectId, 
            string changeType,
            string authToken)
        {
            string oldContent = string.Empty;
            string newContent = string.Empty;

            try
            {
                var encodedPath = Uri.EscapeDataString(filePath);

                // Get new content if file wasn't deleted
                if (changeType.ToLowerInvariant() != "delete" && !string.IsNullOrEmpty(newObjectId))
                {
                    var newContentUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/items" +
                                       $"?path={encodedPath}" +
                                       $"&versionDescriptor.version={newObjectId}" +
                                       $"&versionDescriptor.versionType=commit" +
                                       $"&includeContent=true" +
                                       $"&api-version=7.1";

                    using var newRequest = new HttpRequestMessage(HttpMethod.Get, newContentUri);
                    newRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                    using var newResponse = await _httpClient.SendAsync(newRequest);

                    if (newResponse.IsSuccessStatusCode)
                    {
                        newContent = await newResponse.Content.ReadAsStringAsync();
                    }
                }

                // Get old content if file wasn't added
                if (changeType.ToLowerInvariant() != "add" && !string.IsNullOrEmpty(oldObjectId))
                {
                    var oldContentUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/items" +
                                      $"?path={encodedPath}" +
                                      $"&versionDescriptor.version={oldObjectId}" +
                                      $"&versionDescriptor.versionType=commit" +
                                      $"&includeContent=true" +
                                      $"&api-version=7.1";

                    using var oldRequest = new HttpRequestMessage(HttpMethod.Get, oldContentUri);
                    oldRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                    using var oldResponse = await _httpClient.SendAsync(oldRequest);

                    if (oldResponse.IsSuccessStatusCode)
                    {
                        oldContent = await oldResponse.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                ReportError($"Error retrieving file contents for {filePath}: {ex.Message}");
            }

            return (oldContent, newContent);
        }

        private string GenerateUnifiedDiff(string filePath, string changeType, string oldContent, string newContent)
        {
            var sb = new System.Text.StringBuilder();

            // Add the diff header
            sb.AppendLine($"diff --git a/{filePath} b/{filePath}");

            switch (changeType.ToLowerInvariant())
            {
                case "add":
                    sb.AppendLine("new file mode 100644");
                    sb.AppendLine("index 0000000..1234567");
                    sb.AppendLine("--- /dev/null");
                    sb.AppendLine($"+++ b/{filePath}");
                    sb.AppendLine("@@ -0,0 +1," + CountLines(newContent) + " @@");

                    // Add all new content as added lines
                    foreach (var line in newContent.Split('\n'))
                    {
                        sb.AppendLine("+" + line);
                    }
                    break;

                case "delete":
                    sb.AppendLine("deleted file mode 100644");
                    sb.AppendLine("index 1234567..0000000");
                    sb.AppendLine($"--- a/{filePath}");
                    sb.AppendLine("+++ /dev/null");
                    sb.AppendLine("@@ -1," + CountLines(oldContent) + " +0,0 @@");

                    // Add all old content as removed lines
                    foreach (var line in oldContent.Split('\n'))
                    {
                        sb.AppendLine("-" + line);
                    }
                    break;

                default: // edit/modify
                    GenerateSimpleDiff(sb, oldContent, newContent);
                    break;
            }

            return sb.ToString();
        }

        private void GenerateSimpleDiff(System.Text.StringBuilder sb, string oldContent, string newContent)
        {
            sb.AppendLine("index 1234567..abcdefg 100644");
            sb.AppendLine("--- a/file");
            sb.AppendLine("+++ b/file");

            // Generate a simple diff by comparing lines
            var oldLines = oldContent.Split('\n');
            var newLines = newContent.Split('\n');

            sb.AppendLine($"@@ -1,{oldLines.Length} +1,{newLines.Length} @@");

            // Find common prefix
            int commonPrefix = 0;
            int minLength = Math.Min(oldLines.Length, newLines.Length);
            while (commonPrefix < minLength && oldLines[commonPrefix] == newLines[commonPrefix])
            {
                sb.AppendLine(" " + oldLines[commonPrefix]);
                commonPrefix++;
            }

            // Output deleted lines
            for (int i = commonPrefix; i < oldLines.Length; i++)
            {
                sb.AppendLine("-" + oldLines[i]);
            }

            // Output added lines
            for (int i = commonPrefix; i < newLines.Length; i++)
            {
                sb.AppendLine("+" + newLines[i]);
            }
        }

        private int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return text.Split('\n').Length;
        }

        public async Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(
            string organization,
            string project,
            string repositoryId,
            string personalAccessToken)
        {
            if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(project) || 
                string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(personalAccessToken))
            {
                throw new ArgumentException("Organization, project, repositoryId, and personalAccessToken must not be null or empty.");
            }

            var encodedOrg = Uri.EscapeDataString(organization);
            var encodedProject = Uri.EscapeDataString(project);
            var encodedRepoId = Uri.EscapeDataString(repositoryId);

            var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullrequests?api-version={ApiVersion}";

            var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            var json = await System.Text.Json.JsonDocument.ParseAsync(stream);

            var result = new List<PullRequestInfo>();
            if (json.RootElement.TryGetProperty("value", out var array))
            {
                foreach (var pr in array.EnumerateArray())
                {
                    try
                    {
                        if (!pr.TryGetProperty("pullRequestId", out var idProp))
                            continue;
                        var id = idProp.GetInt32();
                        var title = pr.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;

                        var createdBy = string.Empty;
                        if (pr.TryGetProperty("createdBy", out var createdByProp) && createdByProp.TryGetProperty("displayName", out var displayNameProp))
                            createdBy = displayNameProp.GetString() ?? string.Empty;

                        var sourceBranch = pr.TryGetProperty("sourceRefName", out var sourceBranchProp) ? sourceBranchProp.GetString() ?? string.Empty : string.Empty;
                        var targetBranch = pr.TryGetProperty("targetRefName", out var targetBranchProp) ? targetBranchProp.GetString() ?? string.Empty : string.Empty;

                        var url = string.Empty;
                        if (pr.TryGetProperty("_links", out var linksProp) && linksProp.TryGetProperty("web", out var webProp) && webProp.TryGetProperty("href", out var hrefProp))
                        {
                            url = hrefProp.GetString() ?? string.Empty;
                        }

                        var createdDate = pr.TryGetProperty("creationDate", out var createdDateProp) ? createdDateProp.GetDateTime() : DateTime.Now;
                        var status = pr.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? string.Empty : string.Empty;

                        var reviewers = new List<ReviewerInfo>();
                        if (pr.TryGetProperty("reviewers", out var reviewerArray))
                        {
                            foreach (var rev in reviewerArray.EnumerateArray())
                            {
                                try
                                {
                                    var reviewerId = rev.TryGetProperty("id", out var idValue)
                                        ? idValue.GetString() ?? string.Empty
                                        : string.Empty;
                                    var name = rev.TryGetProperty("displayName", out var nameValue) ? nameValue.GetString() ?? string.Empty : string.Empty;
                                    var vote = rev.TryGetProperty("vote", out var voteValue) ? VoteToString(voteValue.GetInt32()) : "No vote";
                                    reviewers.Add(new ReviewerInfo(reviewerId, name, vote));
                                }
                                catch (Exception ex)
                                {
                                    ReportError($"Error processing reviewer: {ex.Message}");
                                }
                            }
                        }

                        result.Add(new PullRequestInfo(id, title, createdBy, createdDate, status, reviewers, sourceBranch, targetBranch, url));
                    }
                    catch (Exception ex)
                    {
                        ReportError($"Error processing pull request: {ex.Message}");
                    }
                }
            }

            return result;
        }

        private static string VoteToString(int vote) => vote switch
        {
            10 => "Approved",
            5 => "Approved with suggestions",
            -5 => "Waiting for author",
            -10 => "Rejected",
            _ => "No vote"
        };
    }
}
