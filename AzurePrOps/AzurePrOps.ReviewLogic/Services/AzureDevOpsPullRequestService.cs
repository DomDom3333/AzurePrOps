using AzurePrOps.ReviewLogic.Models;
using LibGit2Sharp;
using System.Diagnostics;
using System.IO;

namespace AzurePrOps.ReviewLogic.Services
{
    public class AzureDevOpsPullRequestService : IPullRequestService
    {
        private readonly HttpClient _httpClient;
        private const string AzureDevOpsBaseUrl = "https://dev.azure.com";
    private const string ApiVersion = "7.1";
    private Action<string>? _errorHandler;
    public bool UseGitClient { get; set; }

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

        private void NotifyAuthIfNeeded(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                ReportError("Your Azure DevOps Personal Access Token appears to be invalid or expired. Please open Settings and update your token.");
            }
        }

        private static string RunGit(string workingDirectory, string arguments)
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd();
            string error  = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(error))
            {
                // Include stderr content in the output for better diagnostics
                output += Environment.NewLine + error;
            }

            return output;
        }

        private string CloneRepository(string organization, string project, string repositoryId, string pat)
        {
            string cacheRoot = Path.Combine(Path.GetTempPath(), "AzurePrOpsRepoCache");
            string repoPath = Path.Combine(cacheRoot, $"{organization}_{project}_{repositoryId}");

            string repoUrl = $"{AzureDevOpsBaseUrl}/{organization}/{project}/_git/{repositoryId}";
            string safePat = Uri.EscapeDataString(pat);
            string authUrl = repoUrl.Insert(8, $"pat:{safePat}@");

            if (!Directory.Exists(repoPath) || !Directory.Exists(Path.Combine(repoPath, ".git")))
            {
                Directory.CreateDirectory(repoPath);
                RunGit(Path.GetTempPath(), $"clone --filter=blob:none --depth 1 --no-checkout \"{authUrl}\" \"{repoPath}\"");
            }
            else
            {
                // Update cached repository with latest commits
                RunGit(repoPath, "fetch --filter=blob:none --depth 1 origin");
            }

            return repoPath;
        }

        private static string GetCommitSha(string repoPath, string refName)
        {
            return RunGit(repoPath, $"rev-parse {refName}").Trim();
        }

        private IReadOnlyList<FileDiff> ComputeDiffWithGit(string repoPath, string baseCommit, string sourceCommit)
        {
            var fileDiffs = new List<FileDiff>();
            string names = RunGit(repoPath, $"diff --name-only {baseCommit} {sourceCommit}");
            foreach (var file in names.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string diff = RunGit(repoPath, $"diff {baseCommit} {sourceCommit} -- \"{file}\"");
                string oldContent = string.Empty;
                string newContent = string.Empty;
                try { oldContent = RunGit(repoPath, $"show {baseCommit}:\"{file}\""); } catch { }
                try { newContent = RunGit(repoPath, $"show {sourceCommit}:\"{file}\""); } catch { }
                fileDiffs.Add(new FileDiff(file, diff, oldContent, newContent));
            }
            return fileDiffs;
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
            string personalAccessToken,
            string? baseCommit = null,
            string? diffCommit = null)
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

                NotifyAuthIfNeeded(prResponse);
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
                string baseCommitId = prJson.RootElement
                    .TryGetProperty("lastMergeTargetCommit", out var targetCommitProp) &&
                    targetCommitProp.TryGetProperty("commitId", out var targetCommitIdProp) ?
                    targetCommitIdProp.GetString() ?? string.Empty : string.Empty;

                string sourceCommitId = prJson.RootElement
                    .TryGetProperty("lastMergeSourceCommit", out var sourceCommitProp) &&
                    sourceCommitProp.TryGetProperty("commitId", out var sourceCommitIdProp) ?
                    sourceCommitIdProp.GetString() ?? string.Empty : string.Empty;

                if (UseGitClient)
                {
                    return await Task.Run(() =>
                    {
                        var repoPath = CloneRepository(organization, project, repositoryId, personalAccessToken);
                        RunGit(repoPath, $"fetch --filter=blob:none --depth 1 origin {targetBranch} {sourceBranch}");
                        string baseSha = GetCommitSha(repoPath, baseCommit ?? baseCommitId ?? $"origin/{targetBranch}");
                        string sourceSha = GetCommitSha(repoPath, diffCommit ?? sourceCommitId ?? $"origin/{sourceBranch}");
                        IReadOnlyList<FileDiff> result = ComputeDiffWithGit(repoPath, baseSha, sourceSha);
                        return result;
                    });
                }
                else
                {
                    return await GetDirectDiffViaGitDiffsApi(
                        encodedOrg,
                        encodedProject,
                        encodedRepoId,
                        sourceBranch,
                        targetBranch,
                        diffCommit ?? sourceCommitId,
                        baseCommit ?? baseCommitId,
                        authToken);
                }
            }
            catch (Exception ex)
            {
                ReportError($"Failed to get pull request diff: {ex.Message}");
                return new List<FileDiff>();
            }
        }

        public async Task<IReadOnlyList<FileDiff>> GetPullRequestDiffByFilesAsync(
            string organization,
            string project,
            string repositoryId,
            int pullRequestId,
            string personalAccessToken,
            string? baseCommit = null,
            string? diffCommit = null)
        {
            var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

            try
            {
                var encodedOrg = Uri.EscapeDataString(organization);
                var encodedProject = Uri.EscapeDataString(project);
                var encodedRepoId = Uri.EscapeDataString(repositoryId);

                var prUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}?api-version={ApiVersion}";
                using var prRequest = new HttpRequestMessage(HttpMethod.Get, prUri);
                prRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                using var prResponse = await _httpClient.SendAsync(prRequest);
                NotifyAuthIfNeeded(prResponse);
                prResponse.EnsureSuccessStatusCode();

                using var prStream = await prResponse.Content.ReadAsStreamAsync();
                var prJson = await System.Text.Json.JsonDocument.ParseAsync(prStream);

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

                string baseCommitId = prJson.RootElement
                    .TryGetProperty("lastMergeTargetCommit", out var targetCommitProp) &&
                    targetCommitProp.TryGetProperty("commitId", out var targetCommitIdProp) ?
                    targetCommitIdProp.GetString() ?? string.Empty : string.Empty;

                string sourceCommitId = prJson.RootElement
                    .TryGetProperty("lastMergeSourceCommit", out var sourceCommitProp) &&
                    sourceCommitProp.TryGetProperty("commitId", out var sourceCommitIdProp) ?
                    sourceCommitIdProp.GetString() ?? string.Empty : string.Empty;

                if (UseGitClient)
                {
                    return await Task.Run(() =>
                    {
                        var repoPath = CloneRepository(organization, project, repositoryId, personalAccessToken);
                        RunGit(repoPath, $"fetch --filter=blob:none --depth 1 origin {targetBranch} {sourceBranch}");
                        string baseSha = GetCommitSha(repoPath, baseCommit ?? baseCommitId ?? $"origin/{targetBranch}");
                        string sourceSha = GetCommitSha(repoPath, diffCommit ?? sourceCommitId ?? $"origin/{sourceBranch}");
                        IReadOnlyList<FileDiff> result = ComputeDiffWithGit(repoPath, baseSha, sourceSha);
                        return result;
                    });
                }
                else
                {
                    return await GetDirectDiffViaGitDiffsApi(
                        encodedOrg,
                        encodedProject,
                        encodedRepoId,
                        sourceBranch,
                        targetBranch,
                        diffCommit ?? sourceCommitId,
                        baseCommit ?? baseCommitId,
                        authToken);
                }
            }
            catch (Exception ex)
            {
                ReportError($"Failed to compute diff manually: {ex.Message}");
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
                NotifyAuthIfNeeded(diffResponse);

                if (!diffResponse.IsSuccessStatusCode)
                {
                    var errorContent = await diffResponse.Content.ReadAsStringAsync();
                    ReportError($"Error from Git Diffs API: {diffResponse.StatusCode} - {errorContent}");
                    return new List<FileDiff>();
                }

                var jsonContent = await diffResponse.Content.ReadAsStringAsync();
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);

                return await ProcessGitDiffsResponse(jsonDoc, encodedOrg, encodedProject, encodedRepoId, authToken, baseCommit, sourceCommit);
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
            string authToken,
            string baseCommit,
            string sourceCommit)
        {
            var fileDiffs = new List<FileDiff>();

            if (!jsonDoc.RootElement.TryGetProperty("changes", out var changesArray))
            {
                ReportError("No changes found in the diff response.");
                return fileDiffs;
            }

            var semaphore = new SemaphoreSlim(4);
            var tasks = new List<Task<FileDiff?>>();

            foreach (var change in changesArray.EnumerateArray())
            {
                tasks.Add(ProcessChangeAsync(change, encodedOrg, encodedProject, encodedRepoId, authToken, semaphore, baseCommit, sourceCommit));
            }

            var results = await Task.WhenAll(tasks);

            foreach (var diff in results)
            {
                if (diff != null)
                    fileDiffs.Add(diff);
            }

            return fileDiffs;
        }

        private async Task<FileDiff?> ProcessChangeAsync(
            System.Text.Json.JsonElement change,
            string encodedOrg,
            string encodedProject,
            string encodedRepoId,
            string authToken,
            SemaphoreSlim semaphore,
            string baseCommit,
            string sourceCommit)
        {
            await semaphore.WaitAsync();
            try
            {
                if (!change.TryGetProperty("item", out var item) ||
                    !item.TryGetProperty("path", out var pathProp))
                {
                    return null;
                }

                // Exclude folders from the diff list — we only want actual files
                try
                {
                    // Many Azure DevOps APIs expose either item.isFolder (bool) or item.gitObjectType ("blob" for files, "tree" for folders)
                    bool isFolder = (item.TryGetProperty("isFolder", out var isFolderProp) && isFolderProp.ValueKind == System.Text.Json.JsonValueKind.True);
                    string? objectType = item.TryGetProperty("gitObjectType", out var objTypeProp) ? objTypeProp.GetString() : null;
                    if (isFolder || (!string.IsNullOrEmpty(objectType) && !string.Equals(objectType, "blob", StringComparison.OrdinalIgnoreCase)))
                    {
                        return null;
                    }
                }
                catch { /* be permissive: if we cannot determine, continue */ }

                var filePath = pathProp.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(filePath))
                {
                    return null;
                }

                // Capture original (old) path for renames/moves when provided by API
                string? originalPath = null;
                if (change.TryGetProperty("originalPath", out var originalPathProp))
                {
                    originalPath = originalPathProp.GetString();
                }

                var changeTypeRaw = change.TryGetProperty("changeType", out var changeTypeProp) ?
                    changeTypeProp.GetString() ?? "edit" : "edit";
                var changeType = changeTypeRaw.ToLowerInvariant();

                bool IsType(string token) => changeType.Contains(token, StringComparison.OrdinalIgnoreCase);

                var newObjectId = item.TryGetProperty("objectId", out var newIdProp) ?
                    newIdProp.GetString() : null;

                var oldObjectId = change.TryGetProperty("originalObjectId", out var oldIdProp)
                    ? oldIdProp.GetString()
                    : null;

                (string oldContent, string newContent) versions;
                if (!string.IsNullOrEmpty(oldObjectId) || !string.IsNullOrEmpty(newObjectId))
                {
                    versions = await GetFileContents(
                        encodedOrg, encodedProject, encodedRepoId, filePath, oldObjectId, newObjectId, changeType, authToken, baseCommit, sourceCommit);
                }
                else
                {
                    // Fallback: fetch by overall base/source commits when per-item object IDs are missing
                    // Use originalPath for the old side if present (e.g., rename/move)
                    versions = await FetchFileVersions(
                        encodedOrg,
                        encodedProject,
                        encodedRepoId,
                        filePath,
                        baseCommit,
                        sourceCommit,
                        changeType,
                        authToken,
                        originalPath,
                        filePath);
                }
                var (oldContent, newContent) = versions;

                if (string.IsNullOrEmpty(oldContent) && string.IsNullOrEmpty(newContent))
                {
                    var unifiedDiff = GenerateUnifiedDiff(filePath, changeType, oldContent, newContent, oldObjectId, newObjectId);

                    if (changeType.ToLowerInvariant() == "add")
                    {
                        oldContent = "[This is a new file]\n";
                        var newLines = new List<string>();
                        foreach (var line in unifiedDiff.Split('\n'))
                        {
                            if (line.StartsWith("+") && !line.StartsWith("+++ "))
                                newLines.Add(line.Substring(1));
                        }
                        newContent = string.Join("\n", newLines);
                        if (string.IsNullOrWhiteSpace(newContent))
                            newContent = "[Content could not be retrieved for this new file]\n";
                    }
                    else if (changeType.ToLowerInvariant() == "delete")
                    {
                        newContent = "[This file was deleted]\n";
                        var oldLines = new List<string>();
                        foreach (var line in unifiedDiff.Split('\n'))
                        {
                            if (line.StartsWith("-") && !line.StartsWith("--- "))
                                oldLines.Add(line.Substring(1));
                        }
                        oldContent = string.Join("\n", oldLines);
                        if (string.IsNullOrWhiteSpace(oldContent))
                            oldContent = "[Content could not be retrieved for this deleted file]\n";
                    }
                    else if (changeType.ToLowerInvariant() == "edit")
                    {
                        var oldLines = new List<string>();
                        var newLines = new List<string>();

                        foreach (var line in unifiedDiff.Split('\n'))
                        {
                            if (line.StartsWith("-") && !line.StartsWith("--- "))
                                oldLines.Add(line.Substring(1));
                            else if (line.StartsWith("+") && !line.StartsWith("+++ "))
                                newLines.Add(line.Substring(1));
                            else if (line.StartsWith(" "))
                            {
                                var contextLine = line.Substring(1);
                                oldLines.Add(contextLine);
                                newLines.Add(contextLine);
                            }
                        }
                    }
                    else
                    {
                        oldContent = $"[Could not retrieve original content for {filePath}]\n\n{unifiedDiff}";
                        newContent = $"[Could not retrieve modified content for {filePath}]\n\n{unifiedDiff}";
                    }
                }

                string diffText = GenerateUnifiedDiff(filePath, changeType, oldContent, newContent, oldObjectId, newObjectId);
                oldContent = string.IsNullOrEmpty(oldContent) ? "[No content available]\n" : oldContent;
                newContent = string.IsNullOrEmpty(newContent) ? "[No content available]\n" : newContent;

                return new FileDiff(filePath, diffText, oldContent, newContent);
            }
            catch (Exception ex)
            {
                ReportError($"Error processing change: {ex.Message}");
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<(string oldContent, string newContent)> GetFileContents(
            string encodedOrg,
            string encodedProject,
            string encodedRepoId,
            string filePath,
            string? oldObjectId,
            string? newObjectId,
            string changeType,
            string authToken,
            string baseCommit,
            string sourceCommit)
        {
            string oldContent = string.Empty;
            string newContent = string.Empty;

            try
            {
                // Prefer blob API when objectIds are present (objectId refers to blob, not commit)
                async Task<string> GetBlobAsync(string objectId)
                {
                    var blobUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/blobs/{objectId}?download=true&api-version=7.1";
                    using var req = new HttpRequestMessage(HttpMethod.Get, blobUri);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
                    using var resp = await _httpClient.SendAsync(req);
                    if (!resp.IsSuccessStatusCode)
                        return string.Empty;
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    // Detect binary content (contains NUL or high binary ratio)
                    bool hasNull = bytes.Any(b => b == 0);
                    if (hasNull)
                        return "[Binary content not displayed]";
                    try
                    {
                        // Try UTF8 with fallback
                        return System.Text.Encoding.UTF8.GetString(bytes);
                    }
                    catch
                    {
                        return "[Content decoding error]";
                    }
                }

                // Normalize type to handle composite values (e.g., "rename, edit")
                string ct = changeType.ToLowerInvariant();
                bool isAdd = ct.Contains("add");
                bool isDelete = ct.Contains("delete");

                // New content (skip for deletes)
                if (!isDelete && !string.IsNullOrEmpty(newObjectId))
                {
                    newContent = await GetBlobAsync(newObjectId);
                }

                // Old content (skip for adds)
                if (!isAdd && !string.IsNullOrEmpty(oldObjectId))
                {
                    oldContent = await GetBlobAsync(oldObjectId);
                }

                // Fallback by path and commits if blob retrieval failed or produced empty placeholders
                bool NeedsFallback(string s) => string.IsNullOrEmpty(s) || s.StartsWith("[Binary content", StringComparison.OrdinalIgnoreCase) || s.StartsWith("[Content decoding", StringComparison.OrdinalIgnoreCase);

                if ((!isDelete && NeedsFallback(newContent)) || (!isAdd && NeedsFallback(oldContent)))
                {
                    var byPath = await FetchFileVersions(encodedOrg, encodedProject, encodedRepoId, filePath, baseCommit, sourceCommit, changeType, authToken);
                    if (NeedsFallback(oldContent)) oldContent = byPath.oldContent;
                    if (NeedsFallback(newContent)) newContent = byPath.newContent;
                }
            }
            catch (Exception ex)
            {
                ReportError($"Error retrieving file contents for {filePath}: {ex.Message}");
            }

            return (oldContent, newContent);
        }

        private string GenerateUnifiedDiff(string filePath, string changeType, string oldContent, string newContent, string? oldId = null, string? newId = null)
        {
            return DiffHelper.GenerateUnifiedDiff(filePath, oldContent, newContent, changeType);
        }

        private static string EncodePath(string path)
        {
            // Ensure a leading slash for the Items API
            if (!path.StartsWith("/"))
                path = "/" + path;

            // Azure DevOps expects forward slashes unescaped in the query string
            var encoded = Uri.EscapeDataString(path);
            return encoded.Replace("%2F", "/");
        }


        private async Task<IReadOnlyList<FileDiff>> GetItemsAndComputeDiff(
            string encodedOrg,
            string encodedProject,
            string encodedRepoId,
            string sourceCommit,
            string baseCommit,
            string authToken)
        {
            try
            {
                var changesUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/diffs/commits" +
                               $"?baseVersion={baseCommit}&targetVersion={sourceCommit}&api-version=7.1";

                using var changesRequest = new HttpRequestMessage(HttpMethod.Get, changesUri);
                changesRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                using var changesResponse = await _httpClient.SendAsync(changesRequest);
                NotifyAuthIfNeeded(changesResponse);
                changesResponse.EnsureSuccessStatusCode();

                var changesContent = await changesResponse.Content.ReadAsStringAsync();
                using var changesJson = System.Text.Json.JsonDocument.Parse(changesContent);

                if (!changesJson.RootElement.TryGetProperty("changes", out var changesArray))
                {
                    return new List<FileDiff>();
                }

                var fileDiffs = new List<FileDiff>();

                foreach (var change in changesArray.EnumerateArray())
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

                    var (oldContent, newContent) = await FetchFileVersions(
                        encodedOrg, encodedProject, encodedRepoId, filePath, baseCommit, sourceCommit, changeType, authToken);

                    var diffText = GenerateUnifiedDiff(filePath, changeType, oldContent, newContent);
                    fileDiffs.Add(new FileDiff(filePath, diffText, oldContent, newContent));
                }

                return fileDiffs;
            }
            catch (Exception ex)
            {
                ReportError($"Error in GetItemsAndComputeDiff: {ex.Message}");
                return new List<FileDiff>();
            }
        }

        private async Task<(string oldContent, string newContent)> FetchFileVersions(
            string encodedOrg,
            string encodedProject,
            string encodedRepoId,
            string filePath,
            string baseCommit,
            string sourceCommit,
            string changeType,
            string authToken,
            string? oldFilePath = null,
            string? newFilePath = null)
        {
            string oldContent = string.Empty;
            string newContent = string.Empty;

            try
            {
                var encodedPathOld = EncodePath(oldFilePath ?? filePath);
                var encodedPathNew = EncodePath(newFilePath ?? filePath);

                string ct = changeType.ToLowerInvariant();
                bool isAdd = ct.Contains("add");
                bool isDelete = ct.Contains("delete");

                if (!isAdd && !string.IsNullOrEmpty(baseCommit))
                {
                    var oldVersionUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/items" +
                                      $"?path={encodedPathOld}" +
                                      $"&versionDescriptor.version={baseCommit}" +
                                      $"&versionDescriptor.versionType=commit" +
                                      $"&includeContent=true" +
                                      $"&download=true" +
                                      $"&api-version=7.1";

                    using var oldRequest = new HttpRequestMessage(HttpMethod.Get, oldVersionUri);
                    oldRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                    using var oldResponse = await _httpClient.SendAsync(oldRequest);
                    if (oldResponse.IsSuccessStatusCode)
                    {
                        oldContent = await oldResponse.Content.ReadAsStringAsync();
                    }
                }

                if (!isDelete && !string.IsNullOrEmpty(sourceCommit))
                {
                    var newVersionUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/items" +
                                      $"?path={encodedPathNew}" +
                                      $"&versionDescriptor.version={sourceCommit}" +
                                      $"&versionDescriptor.versionType=commit" +
                                      $"&includeContent=true" +
                                      $"&download=true" +
                                      $"&api-version=7.1";

                    using var newRequest = new HttpRequestMessage(HttpMethod.Get, newVersionUri);
                    newRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                    using var newResponse = await _httpClient.SendAsync(newRequest);
                    if (newResponse.IsSuccessStatusCode)
                    {
                        newContent = await newResponse.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                ReportError($"Error fetching file versions for {filePath}: {ex.Message}");
            }

            return (oldContent, newContent);
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
                        var isDraft = pr.TryGetProperty("isDraft", out var draftProp) && draftProp.GetBoolean();

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

                        result.Add(new PullRequestInfo(id, title, createdBy, createdDate, status, reviewers, sourceBranch, targetBranch, url, isDraft));
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
