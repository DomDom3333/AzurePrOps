using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AzurePrOps.AzureConnection.Models;
using System.Collections.Generic;

namespace AzurePrOps.AzureConnection.Services;

public class AzureDevOpsClient
{
    private readonly HttpClient _httpClient;

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
        var requestUri = $"https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/pullrequests?api-version=7.1-preview.1";

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        var result = new List<PullRequestInfo>();
        if (json.RootElement.TryGetProperty("value", out var array))
        {
            foreach (var pr in array.EnumerateArray())
            {
                var id = pr.GetProperty("pullRequestId").GetInt32();
                var title = pr.GetProperty("title").GetString() ?? string.Empty;
                var createdBy = pr.GetProperty("createdBy").GetProperty("displayName").GetString() ?? string.Empty;
                var sourceBranch = pr.GetProperty("sourceRefName").GetString() ?? string.Empty;
                var targetBranch = pr.GetProperty("targetRefName").GetString() ?? string.Empty;
                var url = pr.GetProperty("_links").GetProperty("web").GetProperty("href").GetString() ?? string.Empty;
                var createdDate = pr.GetProperty("creationDate").GetDateTime();
                var status = pr.GetProperty("status").GetString() ?? string.Empty;

                var reviewers = new List<ReviewerInfo>();
                if (pr.TryGetProperty("reviewers", out var reviewerArray))
                {
                    foreach (var rev in reviewerArray.EnumerateArray())
                    {
                        var idProp = rev.GetProperty("id").GetString() ?? string.Empty;
                        var name = rev.GetProperty("displayName").GetString() ?? string.Empty;
                        var vote = VoteToString(rev.GetProperty("vote").GetInt32());
                        reviewers.Add(new ReviewerInfo(idProp, name, vote));
                    }
                }

                result.Add(new PullRequestInfo(id, title, createdBy, createdDate, status, reviewers, sourceBranch, targetBranch, url));
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
        var requestUri = $"https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads?api-version=7.1-preview.1";

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.GetAsync(requestUri);
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
        var requestUri = $"https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/reviewers/{reviewerId}?api-version=7.1-preview.1";

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var payload = JsonSerializer.Serialize(new { vote });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUri) { Content = content };
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
        var requestUri = $"https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/threads?api-version=7.1-preview.1";

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var body = JsonSerializer.Serialize(new
        {
            comments = new[] { new { parentCommentId = 0, content, commentType = "text" } },
            status = "active"
        });

        using var httpContent = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(requestUri, httpContent);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetUserIdAsync(string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        using var response = await _httpClient.GetAsync("https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1-preview.1");
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);
        return json.RootElement.GetProperty("id").GetString() ?? string.Empty;
    }

    public async Task<IReadOnlyList<NamedItem>> GetOrganizationsAsync(string userId, string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var uri = $"https://app.vssps.visualstudio.com/_apis/accounts?memberId={userId}&api-version=7.1-preview.1";
        using var response = await _httpClient.GetAsync(uri);
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
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var requestUri = $"https://dev.azure.com/{organization}/_apis/projects?api-version=7.1-preview.1";
        using var response = await _httpClient.GetAsync(requestUri);
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
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var requestUri = $"https://dev.azure.com/{organization}/{project}/_apis/git/repositories?api-version=7.1-preview.1";
        using var response = await _httpClient.GetAsync(requestUri);
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

    private static string VoteToString(int vote) => vote switch
    {
        10 => "Approved",
        5 => "Approved with suggestions",
        -5 => "Waiting for author",
        -10 => "Rejected",
        _ => "No vote"
    };
}
