using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AzurePrOps.AzureConnection.Models;
using ReviewComment = AzurePrOps.ReviewLogic.Models.Comment;
using ReviewCommentThread = AzurePrOps.ReviewLogic.Models.CommentThread;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.AzureConnection.Services;

public partial class AzureDevOpsClient
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<AzureDevOpsClient>();
    private const string AzureDevOpsBaseUrl = "https://dev.azure.com";
    private const string AzureDevOpsVsspsUrl = "https://vssps.dev.azure.com";
    private const string ApiVersion = "7.1";
    private readonly HttpClient _httpClient;
    private Action<string>? _errorHandler;

    public void SetErrorHandler(Action<string> handler)
    {
        _errorHandler = handler;
    }

    private void ReportError(string message)
    {
        _errorHandler?.Invoke(message);
    }

    public AzureDevOpsClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(
        string organization,
        string project,
        string repositoryId,
        string personalAccessToken)
    {
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(personalAccessToken))
        {
            throw new ArgumentException("Organization, project, repositoryId, and personalAccessToken must not be null or empty.");
        }
        
        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);

        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullrequests?api-version={ApiVersion}";

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

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
                        // Ensure URL is properly formatted
                        if (!string.IsNullOrEmpty(url) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            url = $"https://{url.TrimStart('/')}";
                        }
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
                            var idPropLocal = rev.TryGetProperty("id", out var idValue)
                                ? idValue.GetString() ?? string.Empty
                                : string.Empty;                            var name = rev.TryGetProperty("displayName", out var nameValue) ? nameValue.GetString() ?? string.Empty : string.Empty;
                            var vote = rev.TryGetProperty("vote", out var voteValue) ? VoteToString(voteValue.GetInt32()) : "No vote";
                            reviewers.Add(new ReviewerInfo(idPropLocal, name, vote));
                        }
                        catch (Exception ex)
                        {
                            // Skip this reviewer if there's an issue with the JSON
                            _logger.LogWarning(ex, "Error processing reviewer");
                        }
                    }
                }

                result.Add(new PullRequestInfo(id, title, createdBy, createdDate, status, reviewers, sourceBranch, targetBranch, url));
                            }
                            catch (Exception ex)
                            {
                // Skip this PR if there's an issue with the JSON
                _logger.LogWarning(ex, "Error processing pull request");
                            }
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken)
    {
        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);

        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        var comments = new List<PullRequestComment>();
        if (json.RootElement.TryGetProperty("value", out var threads))
        {
            foreach (var thread in threads.EnumerateArray())
            {
                if (thread.TryGetProperty("comments", out var commentArray))
                {
                    foreach (var c in commentArray.EnumerateArray())
                    {
                        var content = c.GetProperty("content").GetString() ?? string.Empty;
                        var author = c.GetProperty("author").GetProperty("displayName").GetString() ?? string.Empty;
                        var posted = c.GetProperty("publishedDate").GetDateTime();
                        comments.Add(new PullRequestComment(author, content, posted));
                    }
                }
            }
        }

        return comments;
    }

    public async Task SetPullRequestVoteAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string reviewerId,
        int vote,
        string personalAccessToken)
    {
        // Build the correct Azure DevOps API URL for setting a PR vote (update reviewer)
        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var encodedReviewerId = Uri.EscapeDataString(reviewerId);

        var url = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/reviewers/{encodedReviewerId}?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"))
        );
        request.Content = new StringContent($"{{ \"vote\": {vote} }}", System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public Task ApprovePullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string reviewerId,
        string personalAccessToken) =>
        SetPullRequestVoteAsync(organization, project, repositoryId, pullRequestId, reviewerId, 10, personalAccessToken);

    public async Task PostPullRequestCommentAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string content,
        string personalAccessToken)
    {
        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);

        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var body = JsonSerializer.Serialize(new
        {
            comments = new[] { new { parentCommentId = 0, content, commentType = 1 } },
            status = 1
        });

        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetUserIdAsync(string personalAccessToken)
    {
        if (string.IsNullOrWhiteSpace(personalAccessToken))
        {
            throw new ArgumentException("Personal Access Token must not be null or empty.", nameof(personalAccessToken));
        }
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        
        // First, try the User Profile REST API endpoint
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=6.0");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        try
        {
            using var profileResponse = await _httpClient.SendAsync(profileRequest);

            // If this succeeds, we have a valid token
            if (profileResponse.IsSuccessStatusCode)
            {
                using var profileStream = await profileResponse.Content.ReadAsStreamAsync();
                var profileJson = await JsonDocument.ParseAsync(profileStream);

                if (profileJson.RootElement.TryGetProperty("id", out var id))
                {
                    return id.GetString() ?? Guid.NewGuid().ToString();
                }

                // If we can't get the ID from the profile, the token is still valid
                return Guid.NewGuid().ToString();
            }

            // If we're here, the profile endpoint didn't work. Let's try with a specific organization (postat)
            using var orgRequest = new HttpRequestMessage(HttpMethod.Get, $"{AzureDevOpsBaseUrl}/postat/_apis/projects?api-version=6.0");
            orgRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var orgResponse = await _httpClient.SendAsync(orgRequest);

            if (orgResponse.IsSuccessStatusCode)
            {
                // Token is valid for this organization
                return Guid.NewGuid().ToString();
            }

            // Try with the default organization as a last resort
            using var defaultRequest = new HttpRequestMessage(HttpMethod.Get, $"{AzureDevOpsBaseUrl}/_apis/projects?api-version=6.0");
            defaultRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var defaultResponse = await _httpClient.SendAsync(defaultRequest);

            if (defaultResponse.IsSuccessStatusCode)
            {
                // Token is valid for the default organization
                return Guid.NewGuid().ToString();
            }

            // If we got a 401 Unauthorized from all attempts, the token is invalid
            if (orgResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                defaultResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                profileResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await orgResponse.Content.ReadAsStringAsync();
                throw new UnauthorizedAccessException($"Invalid Personal Access Token. Status: {orgResponse.StatusCode}. Please verify your token has the required permissions (Code: Full), is not expired, and was generated for the correct organization.");
            }

            // If we didn't get a success but also not a 401, generate a dummy ID as a fallback
            // The token might be valid but we just don't have access to the specific resources
            return Guid.NewGuid().ToString();
        }
        catch (HttpRequestException ex)
        {
            // This catches network errors, which are different from authorization errors
            throw new Exception($"Network error while validating Personal Access Token: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<NamedItem>> GetOrganizationsAsync(string userId, string personalAccessToken)
    {
        if (string.IsNullOrWhiteSpace(personalAccessToken))
        {
            throw new ArgumentException("Personal Access Token must not be null or empty.", nameof(personalAccessToken));
        }

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var uri = $"{AzureDevOpsVsspsUrl}/_apis/accounts?memberId={userId}&api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Invalid Personal Access Token. Please verify your token has the required permissions and is not expired.");
        }

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        var list = new List<NamedItem>();
        if (json.RootElement.TryGetProperty("value", out var items))
        {
            foreach (var i in items.EnumerateArray())
            {
                var id = i.GetProperty("accountId").GetString() ?? string.Empty;
                var name = i.GetProperty("accountName").GetString() ?? string.Empty;
                list.Add(new NamedItem(id, name));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<NamedItem>> GetProjectsAsync(string organization, string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/_apis/projects?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        var list = new List<NamedItem>();
        if (json.RootElement.TryGetProperty("value", out var items))
        {
            foreach (var p in items.EnumerateArray())
            {
                var id = p.GetProperty("id").GetString() ?? string.Empty;
                var name = p.GetProperty("name").GetString() ?? string.Empty;
                list.Add(new NamedItem(id, name));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<NamedItem>> GetRepositoriesAsync(string organization, string project, string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        var list = new List<NamedItem>();
        if (json.RootElement.TryGetProperty("value", out var items))
        {
            foreach (var r in items.EnumerateArray())
            {
                var id = r.GetProperty("id").GetString() ?? string.Empty;
                var name = r.GetProperty("name").GetString() ?? string.Empty;
                list.Add(new NamedItem(id, name));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<ReviewCommentThread>> GetPullRequestThreadsAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        var list = new List<ReviewCommentThread>();
        if (json.RootElement.TryGetProperty("value", out var threads))
        {
            foreach (var t in threads.EnumerateArray())
            {
                try
                {
                    list.Add(ParseThread(t));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing comment thread");
                }
            }
        }
        return list;
    }

    public async Task<ReviewCommentThread> CreatePullRequestThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string filePath,
        int lineNumber,
        string content,
        string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var body = new
        {
            comments = new[] { new { parentCommentId = 0, content, commentType = 1 } },
            status = "active",
            threadContext = new
            {
                filePath,
                rightFileStart = new { line = lineNumber, offset = 1 },
                rightFileEnd = new { line = lineNumber, offset = 1 }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);
        return ParseThread(json.RootElement);
    }

    public async Task<ReviewCommentThread> AddCommentToThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        int parentCommentId,
        string content,
        string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads/{threadId}/comments?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var body = new { parentCommentId, content, commentType = 1 };
        request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);
        return ParseThread(json.RootElement);
    }

    public async Task ResolveThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads/{threadId}?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { status = "closed" }), System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static ReviewCommentThread ParseThread(JsonElement threadJson)
    {
        var thread = new ReviewCommentThread
        {
            ThreadId = threadJson.GetProperty("id").GetInt32(),
            Status = threadJson.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? string.Empty : string.Empty
        };

        if (threadJson.TryGetProperty("threadContext", out var ctx))
        {
            thread.FilePath = ctx.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? string.Empty : string.Empty;
            if (ctx.TryGetProperty("rightFileStart", out var start) && start.TryGetProperty("line", out var lineProp))
                thread.LineNumber = lineProp.GetInt32();
        }

        if (threadJson.TryGetProperty("comments", out var comments))
        {
            foreach (var c in comments.EnumerateArray())
            {
                try
                {
                    var comment = new ReviewComment
                    {
                        Id = c.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0,
                        Content = c.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? string.Empty : string.Empty,
                        Author = c.TryGetProperty("author", out var authorProp) && authorProp.TryGetProperty("displayName", out var displayName) ? displayName.GetString() ?? string.Empty : string.Empty,
                        PublishedDate = c.TryGetProperty("publishedDate", out var dateProp) ? dateProp.GetDateTime() : DateTime.Now
                    };
                    thread.Comments.Add(comment);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing comment");
                }
            }
        }

        return thread;
    }

    // These methods have been moved to the partial class implementation

    private static string VoteToString(int vote) => vote switch
    {
        10 => "Approved",
        5 => "Approved with suggestions",
        -5 => "Waiting for author",
        -10 => "Rejected",
        _ => "No vote"
    };
}