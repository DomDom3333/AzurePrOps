using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AzurePrOps.AzureConnection.Models;

namespace AzurePrOps.AzureConnection.Services;
// Internal extension method for string operations
file static class StringExtensions
{
    public static string TrimStart(this string input, string prefix) =>
        input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? input[prefix.Length..] : input;
}


public partial class AzureDevOpsClient
{
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
            repoRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

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

            // Build the direct Git Diffs API URL - use exact casing and correct API path
            var diffUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/diffs/commits" + 
                          $"?baseVersion={Uri.EscapeDataString(baseVersion.TrimStart("refs/heads/"))}" + 
                          $"&baseVersionType={baseVersionType}" + 
                          $"&targetVersion={Uri.EscapeDataString(targetVersion.TrimStart("refs/heads/"))}" + 
                          $"&targetVersionType={targetVersionType}" + 
                          $"&diffCommonCommit=false" +
                          $"&$top=500" +
                          $"&api-version=7.1";
            
            using var diffRequest = new HttpRequestMessage(HttpMethod.Get, diffUri);
            diffRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var diffResponse = await _httpClient.SendAsync(diffRequest);

            if (!diffResponse.IsSuccessStatusCode)
            {
                var errorContent = await diffResponse.Content.ReadAsStringAsync();
                ReportError($"Error from Git Diffs API: {diffResponse.StatusCode} - {errorContent}");

                if (diffResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    ReportError("Diff information not found. Trying alternative approach.");
                    return await GetItemsAndComputeDiff(encodedOrg, encodedProject, encodedRepoId, sourceCommit, baseCommit, authToken);
                }
            }

            diffResponse.EnsureSuccessStatusCode();
            var jsonContent = await diffResponse.Content.ReadAsStringAsync();

            using var jsonDoc = JsonDocument.Parse(jsonContent);
            return await ProcessGitDiffsResponse(jsonDoc, encodedOrg, encodedProject, encodedRepoId, authToken);
        }
        catch (Exception ex)
        {
            ReportError($"Error in GetDirectDiffViaGitDiffsApi: {ex.Message}");

            // Fallback to commit-level changes approach
            if (!string.IsNullOrEmpty(sourceCommit))
            {
                return await GetCommitLevelChanges(encodedOrg, encodedProject, encodedRepoId, sourceCommit, authToken);
            }

            return new List<FileDiff>();
        }
    }

    private async Task<IReadOnlyList<FileDiff>> ProcessGitDiffsResponse(
        JsonDocument jsonDoc, 
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

        int totalChanges = changesArray.GetArrayLength();

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

                // Fetch the content of both versions and create a diff
                var (oldContent, newContent) = await GetFileContents(
                    encodedOrg, encodedProject, encodedRepoId, filePath, oldObjectId, newObjectId, changeType, authToken);

                // Create a unified diff
                var diffText = GenerateUnifiedDiff(filePath, changeType, oldContent, newContent);

                fileDiffs.Add(new FileDiff(filePath, diffText, oldContent, newContent));
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
                newRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

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
                oldRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

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
        var sb = new StringBuilder();

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

            case "rename":
                // For rename without content changes
                sb.AppendLine("rename from " + filePath);
                sb.AppendLine("rename to " + filePath);

                // If content also changed, add diff of content
                if (oldContent != newContent)
                {
                    GenerateSimpleDiff(sb, oldContent, newContent);
                }
                break;

            default: // edit/modify
                GenerateSimpleDiff(sb, oldContent, newContent);
                break;
        }

        return sb.ToString();
    }

    private void GenerateSimpleDiff(StringBuilder sb, string oldContent, string newContent)
    {
        sb.AppendLine("index 1234567..abcdefg 100644");
        sb.AppendLine("--- a/file");
        sb.AppendLine("+++ b/file");

        // Generate a very simple diff by comparing lines
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
            // Get list of changed files between the two commits
            var changesUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/diffs/commits" +
                           $"?baseVersion={baseCommit}&targetVersion={sourceCommit}&api-version=7.1";

            using var changesRequest = new HttpRequestMessage(HttpMethod.Get, changesUri);
            changesRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var changesResponse = await _httpClient.SendAsync(changesRequest);
            changesResponse.EnsureSuccessStatusCode();

            var changesContent = await changesResponse.Content.ReadAsStringAsync();
            using var changesJson = JsonDocument.Parse(changesContent);

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

                // Get each version's content and create a diff
                var (oldContent, newContent) = await FetchFileVersions(
                    encodedOrg, encodedProject, encodedRepoId, filePath, baseCommit, sourceCommit, changeType, authToken);

                // Create unified diff
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
        string authToken)
    {
        string oldContent = string.Empty;
        string newContent = string.Empty;

        try
        {
            var encodedPath = Uri.EscapeDataString(filePath);

            // Get old version if file wasn't added
            if (changeType.ToLowerInvariant() != "add" && !string.IsNullOrEmpty(baseCommit))
            {
                var oldVersionUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/items" +
                                  $"?path={encodedPath}" +
                                  $"&versionDescriptor.version={baseCommit}" +
                                  $"&versionDescriptor.versionType=commit" +
                                  $"&includeContent=true" +
                                  $"&api-version=7.1";

                using var oldRequest = new HttpRequestMessage(HttpMethod.Get, oldVersionUri);
                oldRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                using var oldResponse = await _httpClient.SendAsync(oldRequest);
                if (oldResponse.IsSuccessStatusCode)
                {
                    oldContent = await oldResponse.Content.ReadAsStringAsync();
                }
            }

            // Get new version if file wasn't deleted
            if (changeType.ToLowerInvariant() != "delete" && !string.IsNullOrEmpty(sourceCommit))
            {
                var newVersionUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/items" +
                                  $"?path={encodedPath}" +
                                  $"&versionDescriptor.version={sourceCommit}" +
                                  $"&versionDescriptor.versionType=commit" +
                                  $"&includeContent=true" +
                                  $"&api-version=7.1";

                using var newRequest = new HttpRequestMessage(HttpMethod.Get, newVersionUri);
                newRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

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

    private async Task<IReadOnlyList<FileDiff>> GetCommitLevelChanges(
        string encodedOrg,
        string encodedProject,
        string encodedRepoId,
        string commitId,
        string authToken)
    {
        try
        {
            var changesUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/commits/{commitId}/changes?api-version=7.1";

            using var changesRequest = new HttpRequestMessage(HttpMethod.Get, changesUri);
            changesRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var changesResponse = await _httpClient.SendAsync(changesRequest);
            changesResponse.EnsureSuccessStatusCode();

            var changesContent = await changesResponse.Content.ReadAsStringAsync();
            using var changesJson = JsonDocument.Parse(changesContent);

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

                // Get commit parent
                var parentCommitId = await GetCommitParent(encodedOrg, encodedProject, encodedRepoId, commitId, authToken);

                // Get each version's content
                var (oldContent, newContent) = await FetchFileVersions(
                    encodedOrg, encodedProject, encodedRepoId, filePath, parentCommitId, commitId, changeType, authToken);

                // Create unified diff
                var diffText = GenerateUnifiedDiff(filePath, changeType, oldContent, newContent);
                fileDiffs.Add(new FileDiff(filePath, diffText, oldContent, newContent));
            }

            return fileDiffs;
        }
        catch (Exception ex)
        {
            ReportError($"Error in GetCommitLevelChanges: {ex.Message}");
            return new List<FileDiff>();
        }
    }

    private async Task<string> GetCommitParent(
        string encodedOrg,
        string encodedProject,
        string encodedRepoId,
        string commitId,
        string authToken)
    {
        try
        {
            var commitUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/commits/{commitId}?api-version=7.1";

            using var commitRequest = new HttpRequestMessage(HttpMethod.Get, commitUri);
            commitRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var commitResponse = await _httpClient.SendAsync(commitRequest);
            commitResponse.EnsureSuccessStatusCode();

            var commitContent = await commitResponse.Content.ReadAsStringAsync();
            using var commitJson = JsonDocument.Parse(commitContent);

            if (commitJson.RootElement.TryGetProperty("parents", out var parents) && 
                parents.GetArrayLength() > 0)
            {
                return parents[0].GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            ReportError($"Error getting commit parent: {ex.Message}");
            return string.Empty;
        }
    }
}
