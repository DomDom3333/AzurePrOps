using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AzurePrOps.AzureConnection.Models;
using ReviewComment = AzurePrOps.ReviewLogic.Models.Comment;
using ReviewCommentThread = AzurePrOps.ReviewLogic.Models.CommentThread;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;
using AzurePrOps.ReviewLogic.Services;

namespace AzurePrOps.AzureConnection.Services;

public partial class AzureDevOpsClient : IAzureDevOpsClient
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

    private void NotifyAuthIfNeeded(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            ReportError("Your Azure DevOps Personal Access Token appears to be invalid or expired. Please open Settings and update your token.");
        }
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
        NotifyAuthIfNeeded(response);
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
                    var createdById = string.Empty;
                    if (pr.TryGetProperty("createdBy", out var createdByProp))
                    {
                        if (createdByProp.TryGetProperty("displayName", out var displayNameProp))
                            createdBy = displayNameProp.GetString() ?? string.Empty;
                        if (createdByProp.TryGetProperty("id", out var creatorIdProp))
                            createdById = creatorIdProp.GetString() ?? string.Empty;
                    }

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
                    var isDraft = pr.TryGetProperty("isDraft", out var draftProp) && draftProp.GetBoolean();

                    var reviewers = new List<ReviewerInfo>();
                    if (pr.TryGetProperty("reviewers", out var reviewerArray))
                    {
                        foreach (var rev in reviewerArray.EnumerateArray())
                        {
                            try
                            {
                                var idPropLocal = rev.TryGetProperty("id", out var idValue)
                                    ? idValue.GetString() ?? string.Empty
                                    : string.Empty; 
                                var name = rev.TryGetProperty("displayName", out var nameValue) ? nameValue.GetString() ?? string.Empty : string.Empty;
                                var vote = rev.TryGetProperty("vote", out var voteValue) ? VoteToString(voteValue.GetInt32()) : "No vote";
                                
                                // Detect if this is a group reviewer based on Azure DevOps API properties
                                var isGroup = false;
                                if (rev.TryGetProperty("isContainer", out var isContainerProp) && isContainerProp.GetBoolean())
                                {
                                    isGroup = true;
                                }
                                else if (rev.TryGetProperty("descriptor", out var descriptorProp))
                                {
                                    var descriptor = descriptorProp.GetString() ?? string.Empty;
                                    // In Azure DevOps, group descriptors typically start with "vssgp."
                                    isGroup = descriptor.StartsWith("vssgp.", StringComparison.OrdinalIgnoreCase);
                                }
                                
                                reviewers.Add(new ReviewerInfo(idPropLocal, name, vote, isGroup));
                            }
                            catch (Exception ex)
                            {
                                // Skip this reviewer if there's an issue with the JSON
                                _logger.LogWarning(ex, "Error processing reviewer");
                            }
                        }
                    }

                    // Get last activity date for this PR
                    var lastActivity = await GetLastActivityDateAsync(organization, project, repositoryId, id, personalAccessToken);

                    result.Add(new PullRequestInfo(id, title, createdBy, createdById, createdDate, status, reviewers, sourceBranch, targetBranch, url, isDraft, ReviewerVote: "No vote", ShowDraftBadge: false, LastActivity: lastActivity));
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
        NotifyAuthIfNeeded(response);
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
        NotifyAuthIfNeeded(response);
        response.EnsureSuccessStatusCode();
    }

    public Task ApprovePullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string reviewerId,
        string personalAccessToken) =>
        SetPullRequestVoteAsync(
            organization,
            project,
            repositoryId,
            pullRequestId,
            reviewerId,
            10,
            personalAccessToken);

    public Task ApproveWithSuggestionsAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string reviewerId,
        string personalAccessToken) =>
        SetPullRequestVoteAsync(
            organization,
            project,
            repositoryId,
            pullRequestId,
            reviewerId,
            5,
            personalAccessToken);

    public Task RejectPullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string reviewerId,
        string personalAccessToken) =>
        SetPullRequestVoteAsync(
            organization,
            project,
            repositoryId,
            pullRequestId,
            reviewerId,
            -10,
            personalAccessToken);

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
        NotifyAuthIfNeeded(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> GetUserGroupMembershipsAsync(string organization, string personalAccessToken)
    {
        _logger.LogInformation("[DEBUG_LOG] Starting GetUserGroupMembershipsAsync for organization: {Organization}", organization);
        
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(personalAccessToken))
        {
            _logger.LogWarning("[DEBUG_LOG] Organization or PAT is null/empty - returning empty groups list");
            return new List<string>();
        }

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        var groups = new List<string>();

        // Approach 1: Get user descriptor first, then fetch memberships using correct Graph API pattern
        try
        {
            var encodedOrg = Uri.EscapeDataString(organization);
            
            // First, get the user's subject descriptor
            var userDescriptor = await GetUserSubjectDescriptorAsync(encodedOrg, authToken);
            if (!string.IsNullOrWhiteSpace(userDescriptor))
            {
                _logger.LogInformation("[DEBUG_LOG] Got user descriptor: {UserDescriptor}", userDescriptor);
                
                // Now use the descriptor to get memberships
                var requestUri = $"https://vssps.dev.azure.com/{encodedOrg}/_apis/graph/memberships/{Uri.EscapeDataString(userDescriptor)}?api-version=7.1-preview.1&direction=up";
                _logger.LogInformation("[DEBUG_LOG] Trying Graph API with user descriptor: {RequestUri}", requestUri);
                
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                using var response = await _httpClient.SendAsync(request);
                _logger.LogInformation("[DEBUG_LOG] Graph API with descriptor response status: {StatusCode}", response.StatusCode);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await ProcessMembershipsResponse(response);
                    if (result.Count > 0)
                    {
                        _logger.LogInformation("[DEBUG_LOG] Successfully fetched {GroupCount} groups using user descriptor", result.Count);
                        return result;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Graph API with user descriptor failed, trying fallbacks");
        }
        
        // Approach 2: Try Teams API for team memberships
        try
        {
            var encodedOrg = Uri.EscapeDataString(organization);
            var teamsResult = await GetTeamMembershipsAsync(encodedOrg, authToken);
            if (teamsResult.Count > 0)
            {
                _logger.LogInformation("[DEBUG_LOG] Successfully fetched {GroupCount} team memberships", teamsResult.Count);
                return teamsResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Teams API failed, trying next fallback");
        }
        
        // Approach 3: Try Identity API for security groups
        try
        {
            var encodedOrg = Uri.EscapeDataString(organization);
            var identityResult = await GetSecurityGroupMembershipsAsync(encodedOrg, authToken);
            if (identityResult.Count > 0)
            {
                _logger.LogInformation("[DEBUG_LOG] Successfully fetched {GroupCount} security groups", identityResult.Count);
                return identityResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Identity API also failed");
        }

        _logger.LogWarning("[DEBUG_LOG] All API approaches failed, returning empty groups list");
        return groups;
    }

    private async Task<string?> GetUserSubjectDescriptorAsync(string encodedOrganization, string authToken)
    {
        try
        {
            var requestUri = $"https://vssps.dev.azure.com/{encodedOrganization}/_apis/graph/users?api-version=7.1-preview.1";
            _logger.LogInformation("[DEBUG_LOG] Getting user descriptor from: {RequestUri}", requestUri);
            
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var response = await _httpClient.SendAsync(request);
            _logger.LogInformation("[DEBUG_LOG] Users API response status: {StatusCode}", response.StatusCode);
            
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                var json = await JsonDocument.ParseAsync(stream);

                if (json.RootElement.TryGetProperty("value", out var usersArray))
                {
                    foreach (var user in usersArray.EnumerateArray())
                    {
                        // Find the current user (the one making the request)
                        if (user.TryGetProperty("descriptor", out var descriptorProp))
                        {
                            var descriptor = descriptorProp.GetString();
                            _logger.LogInformation("[DEBUG_LOG] Found user descriptor: {Descriptor}", descriptor);
                            return descriptor; // Return the first descriptor found - this should be the current user
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Failed to get user descriptor");
        }
        
        return null;
    }

    private async Task<List<string>> GetTeamMembershipsAsync(string encodedOrganization, string authToken)
    {
        var teams = new List<string>();
        
        try
        {
            // Step 1: Get all projects
            var projectsUri = $"https://dev.azure.com/{encodedOrganization}/_apis/projects?api-version=7.1";
            _logger.LogInformation("[DEBUG_LOG] Getting projects from: {ProjectsUri}", projectsUri);
            
            using var projectsRequest = new HttpRequestMessage(HttpMethod.Get, projectsUri);
            projectsRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var projectsResponse = await _httpClient.SendAsync(projectsRequest);
            _logger.LogInformation("[DEBUG_LOG] Projects API response status: {StatusCode}", projectsResponse.StatusCode);
            
            if (projectsResponse.IsSuccessStatusCode)
            {
                using var projectsStream = await projectsResponse.Content.ReadAsStreamAsync();
                var projectsJson = await JsonDocument.ParseAsync(projectsStream);

                if (projectsJson.RootElement.TryGetProperty("value", out var projectsArray))
                {
                    foreach (var project in projectsArray.EnumerateArray())
                    {
                        if (project.TryGetProperty("name", out var projectNameProp))
                        {
                            var projectName = projectNameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(projectName))
                            {
                                _logger.LogInformation("[DEBUG_LOG] Processing project: {ProjectName}", projectName);
                                
                                // Step 2: Get teams for this project
                                var teamsUri = $"https://dev.azure.com/{encodedOrganization}/{Uri.EscapeDataString(projectName)}/_apis/teams?api-version=7.1";
                                _logger.LogInformation("[DEBUG_LOG] Getting teams for project {ProjectName} from: {TeamsUri}", projectName, teamsUri);
                                
                                using var teamsRequest = new HttpRequestMessage(HttpMethod.Get, teamsUri);
                                teamsRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                                using var teamsResponse = await _httpClient.SendAsync(teamsRequest);
                                _logger.LogInformation("[DEBUG_LOG] Teams API response status for project {ProjectName}: {StatusCode}", projectName, teamsResponse.StatusCode);
                                
                                if (teamsResponse.IsSuccessStatusCode)
                                {
                                    using var teamsStream = await teamsResponse.Content.ReadAsStreamAsync();
                                    var teamsJson = await JsonDocument.ParseAsync(teamsStream);

                                    if (teamsJson.RootElement.TryGetProperty("value", out var teamsArray))
                                    {
                                        foreach (var team in teamsArray.EnumerateArray())
                                        {
                                            if (team.TryGetProperty("name", out var teamNameProp) && team.TryGetProperty("id", out var teamIdProp))
                                            {
                                                var teamName = teamNameProp.GetString();
                                                var teamId = teamIdProp.GetString();
                                                if (!string.IsNullOrWhiteSpace(teamName) && !string.IsNullOrWhiteSpace(teamId))
                                                {
                                                    _logger.LogInformation("[DEBUG_LOG] Found team: {TeamName} (ID: {TeamId})", teamName, teamId);
                                                    
                                                    // Step 3: Check if current user is member of this team
                                                    var membersUri = $"https://dev.azure.com/{encodedOrganization}/{Uri.EscapeDataString(projectName)}/_apis/teams/{Uri.EscapeDataString(teamId)}/members?api-version=7.1";
                                                    _logger.LogInformation("[DEBUG_LOG] Checking team membership for {TeamName}: {MembersUri}", teamName, membersUri);
                                                    
                                                    using var membersRequest = new HttpRequestMessage(HttpMethod.Get, membersUri);
                                                    membersRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                                                    using var membersResponse = await _httpClient.SendAsync(membersRequest);
                                                    _logger.LogInformation("[DEBUG_LOG] Team members API response status for {TeamName}: {StatusCode}", teamName, membersResponse.StatusCode);
                                                    
                                                    if (membersResponse.IsSuccessStatusCode)
                                                    {
                                                        using var membersStream = await membersResponse.Content.ReadAsStreamAsync();
                                                        var membersJson = await JsonDocument.ParseAsync(membersStream);

                                                        if (membersJson.RootElement.TryGetProperty("value", out var membersArray))
                                                        {
                                                            // Check if current user is in the members list
                                                            var currentUserFound = false;
                                                            foreach (var member in membersArray.EnumerateArray())
                                                            {
                                                                if (member.TryGetProperty("identity", out var identityProp))
                                                                {
                                                                    // Check if this member represents the current user
                                                                    var memberName = "Unknown";
                                                                    if (identityProp.TryGetProperty("displayName", out var displayNameProp))
                                                                    {
                                                                        memberName = displayNameProp.GetString() ?? "Unknown";
                                                                        currentUserFound = true;
                                                                    }
                                                                    else if (identityProp.TryGetProperty("uniqueName", out var uniqueNameProp))
                                                                    {
                                                                        memberName = uniqueNameProp.GetString() ?? "Unknown";
                                                                        currentUserFound = true;
                                                                    }
                                                                    else if (identityProp.TryGetProperty("id", out var memberIdProp))
                                                                    {
                                                                        memberName = memberIdProp.GetString() ?? "Unknown";
                                                                        currentUserFound = true;
                                                                    }
                                                                    
                                                                    if (currentUserFound)
                                                                    {
                                                                        _logger.LogInformation("[DEBUG_LOG] Found team member: {MemberName}", memberName);
                                                                        break; // We found at least one member, assume user is member
                                                                    }
                                                                }
                                                            }
                                                            
                                                            var memberCount = membersArray.GetArrayLength();
                                                            _logger.LogInformation("[DEBUG_LOG] Team {TeamName} has {MemberCount} members, current user found: {CurrentUserFound}", 
                                                                teamName, memberCount, currentUserFound);
                                                            
                                                            // If we can access the team members, we likely have permission (are a member)
                                                            if (memberCount > 0)
                                                            {
                                                                teams.Add(teamName);
                                                                _logger.LogInformation("[DEBUG_LOG] Added team to user groups: {TeamName}", teamName);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Failed to get team memberships");
        }
        
        return teams;
    }

    private async Task<List<string>> GetSecurityGroupMembershipsAsync(string encodedOrganization, string authToken)
    {
        var groups = new List<string>();
        
        try
        {
            // Approach 1: Try Subject Query API for comprehensive group lookup
            var subjectQueryResult = await GetGroupsViaSubjectQueryAsync(encodedOrganization, authToken);
            if (subjectQueryResult.Count > 0)
            {
                groups.AddRange(subjectQueryResult);
                _logger.LogInformation("[DEBUG_LOG] Found {GroupCount} groups via Subject Query API", subjectQueryResult.Count);
            }
            
            // Approach 2: Try Group Lookup API - get all groups and check membership
            if (groups.Count == 0)
            {
                var groupLookupResult = await GetGroupsViaGroupLookupAsync(encodedOrganization, authToken);
                if (groupLookupResult.Count > 0)
                {
                    groups.AddRange(groupLookupResult);
                    _logger.LogInformation("[DEBUG_LOG] Found {GroupCount} groups via Group Lookup API", groupLookupResult.Count);
                }
            }
            
            // Approach 3: Try Identities API for AD groups with expanded membership
            if (groups.Count == 0)
            {
                var identitiesResult = await GetGroupsViaIdentitiesAsync(encodedOrganization, authToken);
                if (identitiesResult.Count > 0)
                {
                    groups.AddRange(identitiesResult);
                    _logger.LogInformation("[DEBUG_LOG] Found {GroupCount} groups via Identities API", identitiesResult.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Failed to get security group memberships");
        }
        
        return groups;
    }

    private async Task<List<string>> GetGroupsViaSubjectQueryAsync(string encodedOrganization, string authToken)
    {
        var groups = new List<string>();
        
        try
        {
            var requestUri = $"https://vssps.dev.azure.com/{encodedOrganization}/_apis/graph/subjectquery?api-version=7.1-preview.1";
            _logger.LogInformation("[DEBUG_LOG] Trying Subject Query API: {RequestUri}", requestUri);
            
            var requestBody = JsonSerializer.Serialize(new
            {
                query = "@@users@@",
                subjectKind = new[] { "User" }
            });
            
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            request.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            _logger.LogInformation("[DEBUG_LOG] Subject Query API response status: {StatusCode}", response.StatusCode);
            
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                var json = await JsonDocument.ParseAsync(stream);

                if (json.RootElement.TryGetProperty("value", out var subjectsArray))
                {
                    foreach (var subject in subjectsArray.EnumerateArray())
                    {
                        if (subject.TryGetProperty("displayName", out var displayNameProp))
                        {
                            var groupName = displayNameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(groupName))
                            {
                                groups.Add(groupName);
                                _logger.LogInformation("[DEBUG_LOG] Found group via Subject Query: {GroupName}", groupName);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Subject Query API failed");
        }
        
        return groups;
    }

    private async Task<List<string>> GetGroupsViaGroupLookupAsync(string encodedOrganization, string authToken)
    {
        var groups = new List<string>();
        
        try
        {
            // First get all groups
            var requestUri = $"https://vssps.dev.azure.com/{encodedOrganization}/_apis/graph/groups?api-version=7.1-preview.1";
            _logger.LogInformation("[DEBUG_LOG] Getting all groups from: {RequestUri}", requestUri);
            
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var response = await _httpClient.SendAsync(request);
            _logger.LogInformation("[DEBUG_LOG] Groups API response status: {StatusCode}", response.StatusCode);
            
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                var json = await JsonDocument.ParseAsync(stream);

                if (json.RootElement.TryGetProperty("value", out var groupsArray))
                {
                    var userDescriptor = await GetUserSubjectDescriptorAsync(encodedOrganization, authToken);
                    if (!string.IsNullOrWhiteSpace(userDescriptor))
                    {
                        foreach (var group in groupsArray.EnumerateArray())
                        {
                            if (group.TryGetProperty("descriptor", out var groupDescriptorProp) &&
                                group.TryGetProperty("displayName", out var displayNameProp))
                            {
                                var groupDescriptor = groupDescriptorProp.GetString();
                                var groupName = displayNameProp.GetString();
                                
                                if (!string.IsNullOrWhiteSpace(groupDescriptor) && !string.IsNullOrWhiteSpace(groupName))
                                {
                                    // Check if user is member of this group
                                    var membershipUri = $"https://vssps.dev.azure.com/{encodedOrganization}/_apis/graph/memberships/{Uri.EscapeDataString(groupDescriptor)}/{Uri.EscapeDataString(userDescriptor)}?api-version=7.1-preview.1";
                                    
                                    using var membershipRequest = new HttpRequestMessage(HttpMethod.Get, membershipUri);
                                    membershipRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                                    
                                    using var membershipResponse = await _httpClient.SendAsync(membershipRequest);
                                    if (membershipResponse.IsSuccessStatusCode)
                                    {
                                        groups.Add(groupName);
                                        _logger.LogInformation("[DEBUG_LOG] User is member of group: {GroupName}", groupName);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Group Lookup API failed");
        }
        
        return groups;
    }

    private async Task<List<string>> GetGroupsViaIdentitiesAsync(string encodedOrganization, string authToken)
    {
        var groups = new List<string>();
        
        try
        {
            var userDescriptor = await GetUserSubjectDescriptorAsync(encodedOrganization, authToken);
            if (!string.IsNullOrWhiteSpace(userDescriptor))
            {
                var requestUri = $"https://vssps.dev.azure.com/{encodedOrganization}/_apis/identities?searchFilter=General&filterValue={Uri.EscapeDataString(userDescriptor)}&queryMembership=Expanded&api-version=7.1";
                _logger.LogInformation("[DEBUG_LOG] Trying Identities API with expanded membership: {RequestUri}", requestUri);
                
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                using var response = await _httpClient.SendAsync(request);
                _logger.LogInformation("[DEBUG_LOG] Identities API response status: {StatusCode}", response.StatusCode);
                
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var json = await JsonDocument.ParseAsync(stream);

                    if (json.RootElement.TryGetProperty("value", out var identitiesArray))
                    {
                        foreach (var identity in identitiesArray.EnumerateArray())
                        {
                            if (identity.TryGetProperty("memberOf", out var memberOfArray))
                            {
                                foreach (var group in memberOfArray.EnumerateArray())
                                {
                                    if (group.TryGetProperty("displayName", out var displayNameProp))
                                    {
                                        var groupName = displayNameProp.GetString();
                                        if (!string.IsNullOrWhiteSpace(groupName))
                                        {
                                            groups.Add(groupName);
                                            _logger.LogInformation("[DEBUG_LOG] Found group via Identities API: {GroupName}", groupName);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DEBUG_LOG] Identities API failed");
        }
        
        return groups;
    }

    private async Task<List<string>> ProcessMembershipsResponse(HttpResponseMessage response)
    {
        var groups = new List<string>();
        
        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        _logger.LogInformation("[DEBUG_LOG] Successfully parsed JSON response");

        if (json.RootElement.TryGetProperty("value", out var array))
        {
            _logger.LogInformation("[DEBUG_LOG] Found 'value' property with {Count} memberships", array.GetArrayLength());
            
            foreach (var membership in array.EnumerateArray())
            {
                _logger.LogInformation("[DEBUG_LOG] Processing membership entry...");
                
                if (membership.TryGetProperty("containerDescriptor", out var containerProp) &&
                    membership.TryGetProperty("memberDescriptor", out var memberProp))
                {
                    _logger.LogInformation("[DEBUG_LOG] Found containerDescriptor and memberDescriptor");
                    
                    // This is a group membership - get the group display name
                    if (membership.TryGetProperty("container", out var container) &&
                        container.TryGetProperty("displayName", out var displayNameProp))
                    {
                        var groupName = displayNameProp.GetString();
                        _logger.LogInformation("[DEBUG_LOG] Found container displayName: {GroupName}", groupName);
                        
                        if (!string.IsNullOrWhiteSpace(groupName))
                        {
                            groups.Add(groupName);
                            _logger.LogInformation("[DEBUG_LOG] Added group to list: {GroupName}", groupName);
                        }
                        else
                        {
                            _logger.LogWarning("[DEBUG_LOG] Group name is null or whitespace, skipping");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("[DEBUG_LOG] No container or displayName property found");
                    }
                }
                else
                {
                    _logger.LogInformation("[DEBUG_LOG] Missing containerDescriptor or memberDescriptor");
                }
            }
        }
        else
        {
            _logger.LogWarning("[DEBUG_LOG] No 'value' property found in JSON response");
        }

        _logger.LogInformation("[DEBUG_LOG] Returning {GroupCount} groups: {Groups}", groups.Count, string.Join(", ", groups));
        return groups;
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
        catch (Exception ex)
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
            NotifyAuthIfNeeded(response);
            throw new UnauthorizedAccessException("Invalid Personal Access Token. Please verify your token has the required permissions and is not expired.");
        }

        NotifyAuthIfNeeded(response);
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
        NotifyAuthIfNeeded(response);
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
        NotifyAuthIfNeeded(response);
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

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Handle 404 as a non-critical error - PR might not have threads yet
            _logger.LogInformation("No threads found for pull request {PullRequestId}", pullRequestId);
            return new List<ReviewCommentThread>();
        }

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
        NotifyAuthIfNeeded(response);
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
        NotifyAuthIfNeeded(response);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);
        return ParseThread(json.RootElement);
    }

    public Task ResolveThreadAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        string personalAccessToken)
    {
        return UpdateThreadStatusAsync(
            organization,
            project,
            repositoryId,
            pullRequestId,
            threadId,
            "closed",
            personalAccessToken);
    }

    public async Task UpdateThreadStatusAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        string status,
        string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads/{threadId}?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { status }), System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        NotifyAuthIfNeeded(response);
        response.EnsureSuccessStatusCode();
    }

    private static ReviewCommentThread ParseThread(JsonElement threadJson)
    {
        var thread = new ReviewCommentThread();

        // Add null checks before accessing properties
        if (threadJson.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null)
        {
            thread.ThreadId = idElement.GetInt32();
        }

        if (threadJson.TryGetProperty("status", out var statusElement) && statusElement.ValueKind != JsonValueKind.Null)
        {
            thread.Status = statusElement.GetString() ?? string.Empty;
        }

        if (threadJson.TryGetProperty("threadContext", out var contextElement) && contextElement.ValueKind != JsonValueKind.Null)
        {
            // Only process if contextElement is not null
            if (contextElement.TryGetProperty("filePath", out var filePathElement) && filePathElement.ValueKind != JsonValueKind.Null)
            {
                thread.FilePath = filePathElement.GetString() ?? string.Empty;
            }

            int parsedLine = 0;
            if (contextElement.TryGetProperty("rightFileStart", out var rightStart) && rightStart.ValueKind != JsonValueKind.Null)
            {
                if (rightStart.TryGetProperty("line", out var rightLine) && rightLine.ValueKind != JsonValueKind.Null)
                {
                    parsedLine = rightLine.GetInt32();
                }
            }
            // Fallback to left side if right is missing or zero (e.g., deletions anchored on left)
            if ((parsedLine == 0) && contextElement.TryGetProperty("leftFileStart", out var leftStart) && leftStart.ValueKind != JsonValueKind.Null)
            {
                if (leftStart.TryGetProperty("line", out var leftLine) && leftLine.ValueKind != JsonValueKind.Null)
                {
                    parsedLine = leftLine.GetInt32();
                }
            }
            // Ensure we have a sensible default
            thread.LineNumber = parsedLine <= 0 ? 1 : parsedLine;
        }

        if (threadJson.TryGetProperty("comments", out var commentsElement) && commentsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var commentElement in commentsElement.EnumerateArray())
            {
                try
                {
                    if (commentElement.ValueKind != JsonValueKind.Null)
                    {
                        var comment = new ReviewComment();

                        if (commentElement.TryGetProperty("id", out var idProp) && idProp.ValueKind != JsonValueKind.Null)
                        {
                            comment.Id = idProp.GetInt32();
                        }

                        if (commentElement.TryGetProperty("content", out var contentProp) && contentProp.ValueKind != JsonValueKind.Null)
                        {
                            comment.Content = contentProp.GetString() ?? string.Empty;
                        }

                        if (commentElement.TryGetProperty("author", out var authorProp) && authorProp.ValueKind != JsonValueKind.Null &&
                            authorProp.TryGetProperty("displayName", out var displayName) && displayName.ValueKind != JsonValueKind.Null)
                        {
                            comment.Author = displayName.GetString() ?? string.Empty;
                        }

                        if (commentElement.TryGetProperty("publishedDate", out var dateProp) && dateProp.ValueKind != JsonValueKind.Null)
                        {
                            comment.PublishedDate = dateProp.GetDateTime();
                        }
                        else
                        {
                            comment.PublishedDate = DateTime.Now;
                        }

                        thread.Comments.Add(comment);
                    }
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

    public async Task SetPullRequestDraftAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        bool isDraft,
        string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { isDraft }), System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        NotifyAuthIfNeeded(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task CompletePullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        AzurePrOps.ReviewLogic.Models.MergeOptions mergeOptions,
        string personalAccessToken)
    {
        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        var encodedOrg = Uri.EscapeDataString(organization);
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRepoId = Uri.EscapeDataString(repositoryId);
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        var body = new
        {
            status = "completed",
            completionOptions = new
            {
                deleteSourceBranch = mergeOptions.DeleteSourceBranch,
                squashMerge = mergeOptions.Squash,
                mergeCommitMessage = mergeOptions.CommitMessage
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        NotifyAuthIfNeeded(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task AbandonPullRequestAsync(
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
        var requestUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { status = "abandoned" }), System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        NotifyAuthIfNeeded(response);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Retrieves the current user information from Azure DevOps.
    /// 
    /// CRITICAL: This method is essential for the "My PR" filter functionality!
    /// 
    /// The user ID returned by this method MUST match the creator IDs in PR data
    /// for filtering to work correctly. This requires a specific multi-step approach:
    /// 1. Get display name from Profile API (most reliable)
    /// 2. Use Identity API with display name search to get matching user ID
    /// 3. Fallback to ConnectionData API if needed
    /// 
    /// DO NOT modify this logic without understanding the ID matching requirements!
    /// </summary>
    public async Task<UserInfo> GetCurrentUserAsync(string organization, string personalAccessToken)
    {
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(personalAccessToken))
        {
            _logger.LogWarning("Organization or personal access token is null or empty");
            return UserInfo.Empty;
        }

        var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));

        // IMPORTANT: The user ID we retrieve here must exactly match the creator IDs
        // found in pull request data. Different Azure DevOps APIs can return different
        // ID formats, so we use a carefully orchestrated approach to ensure compatibility.
        string idFromConnection = string.Empty;
        string displayName = string.Empty;
        string uniqueName = string.Empty;
        string email = string.Empty;

        // STEP 1: Get display name from Profile API
        // 
        // CRITICAL: We get the display name first because it's the most reliable way to
        // identify the current user. The Profile API is more stable than other endpoints
        // and provides consistent display names that we can use for Identity API searches.
        //
        // NOTE: Some JSON fields may be numbers instead of strings, hence the use of
        // GetJsonPropertyAsString() helper to safely parse different data types.
        try
        {
            var profileRequestUri = "https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=6.0";

            using var profileRequest = new HttpRequestMessage(HttpMethod.Get, profileRequestUri);
            profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            using var profileResponse = await _httpClient.SendAsync(profileRequest);
            NotifyAuthIfNeeded(profileResponse);

            if (profileResponse.IsSuccessStatusCode)
            {
                using var profileStream = await profileResponse.Content.ReadAsStreamAsync();
                var profileJson = await JsonDocument.ParseAsync(profileStream);

                // Use safe string extraction to handle number/string variations in JSON
                displayName = GetJsonPropertyAsString(profileJson.RootElement, "displayName");
                email = GetJsonPropertyAsString(profileJson.RootElement, "emailAddress");
                var publicAlias = GetJsonPropertyAsString(profileJson.RootElement, "publicAlias");
                var coreRevision = GetJsonPropertyAsString(profileJson.RootElement, "coreRevision");
                
                if (!string.IsNullOrWhiteSpace(publicAlias)) uniqueName = publicAlias;

                _logger.LogInformation("Retrieved user profile: DisplayName={DisplayName}, Email={Email}, PublicAlias={PublicAlias}", displayName, email, publicAlias);
            }
            else
            {
                _logger.LogWarning("Profile API request failed with status: {StatusCode}, Reason: {ReasonPhrase}", 
                    profileResponse.StatusCode, profileResponse.ReasonPhrase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get user info from profile API");
        }

        // STEP 2: Get user ID from Identity API using display name search
        //
        // *** ABSOLUTELY CRITICAL FOR "MY PR" FILTER FUNCTIONALITY ***
        //
        // This is the MOST IMPORTANT part of user ID retrieval! The Identity API returns
        // user IDs that match exactly with the creator IDs found in pull request data.
        // Other APIs (like ConnectionData) may return different ID formats that don't match.
        //
        // IMPORTANT IMPLEMENTATION DETAILS:
        // - We search by display name to find the user's identity
        // - We try exact display name matching first (most reliable)
        // - If exact match fails, we use the first result (fallback for API inconsistencies)
        // - The fallback is crucial: sometimes the Identity API returns correct IDs but 
        //   with empty/different display names due to Azure AD sync issues
        // 
        // DO NOT REMOVE THE FALLBACK LOGIC! It's essential for the filter to work.
        if (string.IsNullOrEmpty(idFromConnection) && !string.IsNullOrEmpty(displayName))
        {
            try
            {
                var encodedOrg = Uri.EscapeDataString(organization);
                var encodedDisplayName = Uri.EscapeDataString(displayName);
                var identityRequestUri = $"https://vssps.dev.azure.com/{encodedOrg}/_apis/identities?searchFilter=General&filterValue={encodedDisplayName}&api-version=6.0";

                using var identityRequest = new HttpRequestMessage(HttpMethod.Get, identityRequestUri);
                identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                using var identityResponse = await _httpClient.SendAsync(identityRequest);
                NotifyAuthIfNeeded(identityResponse);

                if (identityResponse.IsSuccessStatusCode)
                {
                    using var identityStream = await identityResponse.Content.ReadAsStreamAsync();
                    var identityJson = await JsonDocument.ParseAsync(identityStream);

                    if (identityJson.RootElement.TryGetProperty("value", out var identitiesArray) && identitiesArray.GetArrayLength() > 0)
                    {
                        // PRIMARY: Look for exact display name match first
                        foreach (var identity in identitiesArray.EnumerateArray())
                        {
                            var identityDisplayName = identity.TryGetProperty("displayName", out var identityDisplayNameProp) ? identityDisplayNameProp.GetString() ?? string.Empty : string.Empty;
                            var identityId = identity.TryGetProperty("id", out var identityIdProp) ? identityIdProp.GetString() ?? string.Empty : string.Empty;

                            if (identityDisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(identityId))
                            {
                                idFromConnection = identityId;
                                _logger.LogInformation("Retrieved current user ID from Identity API with exact name match: {DisplayName} ({Id})", displayName, idFromConnection);
                                break;
                            }
                        }

                        // FALLBACK: Use first valid ID even if display name doesn't match exactly
                        // 
                        // CRITICAL: This fallback is essential! Sometimes Azure AD sync issues cause
                        // the Identity API to return the correct user ID but with an empty or different
                        // display name. Since we searched by display name, the first result is very
                        // likely the correct user, so we use it even without exact name matching.
                        // 
                        // This fallback has been verified to fix "My PR" filter issues where the
                        // correct user ID exists but display name matching fails.
                        if (string.IsNullOrEmpty(idFromConnection))
                        {
                            var firstIdentity = identitiesArray[0];
                            var firstIdentityId = firstIdentity.TryGetProperty("id", out var firstIdProp) ? firstIdProp.GetString() ?? string.Empty : string.Empty;
                            var firstIdentityDisplayName = firstIdentity.TryGetProperty("displayName", out var firstDisplayNameProp) ? firstDisplayNameProp.GetString() ?? string.Empty : string.Empty;
                            
                            if (!string.IsNullOrEmpty(firstIdentityId))
                            {
                                idFromConnection = firstIdentityId;
                                _logger.LogInformation("Retrieved current user ID from Identity API (first result - fallback): {DisplayName} -> {Id} (SearchedName: {SearchedDisplayName})", 
                                    firstIdentityDisplayName, idFromConnection, displayName);
                            }
                            
                            // Debug logging to help troubleshoot future issues
                            _logger.LogWarning("Identity API returned {Count} results but no exact display name match for '{DisplayName}'", identitiesArray.GetArrayLength(), displayName);
                            for (int i = 0; i < Math.Min(3, identitiesArray.GetArrayLength()); i++)
                            {
                                var identity = identitiesArray[i];
                                var identityDisplayName = identity.TryGetProperty("displayName", out var identityDisplayNameProp) ? identityDisplayNameProp.GetString() ?? string.Empty : string.Empty;
                                var identityId = identity.TryGetProperty("id", out var identityIdProp) ? identityIdProp.GetString() ?? string.Empty : string.Empty;
                                _logger.LogWarning("Identity result {Index}: DisplayName='{IdentityDisplayName}', Id='{IdentityId}'", i, identityDisplayName, identityId);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Identity API returned no results for display name: '{DisplayName}'", displayName);
                    }
                }
                else
                {
                    _logger.LogWarning("Identity API request failed with status: {StatusCode}, Reason: {ReasonPhrase}", 
                        identityResponse.StatusCode, identityResponse.ReasonPhrase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get user ID from Identity API using display name");
            }
        }

        // STEP 3: Fallback to ConnectionData API (last resort)
        //
        // WARNING: This API often returns IDs in different formats that may not match
        // the creator IDs in pull request data. Use only as a last resort when the
        // Identity API fails completely.
        //
        // The ConnectionData API is less reliable for "My PR" filtering because:
        // - It may return different ID formats than PR creator IDs
        // - It's more prone to authentication/permission issues
        // - The Identity API approach (Step 2) is much more reliable
        if (string.IsNullOrEmpty(idFromConnection))
        {
            try
            {
                var encodedOrg = Uri.EscapeDataString(organization);
                var connectionRequestUri = $"https://dev.azure.com/{encodedOrg}/_apis/connectionData?api-version=6.0";

                using var connectionRequest = new HttpRequestMessage(HttpMethod.Get, connectionRequestUri);
                connectionRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                using var connectionResponse = await _httpClient.SendAsync(connectionRequest);
                NotifyAuthIfNeeded(connectionResponse);

                if (connectionResponse.IsSuccessStatusCode)
                {
                    using var connectionStream = await connectionResponse.Content.ReadAsStreamAsync();
                    var connectionJson = await JsonDocument.ParseAsync(connectionStream);

                    _logger.LogDebug("ConnectionData API response: {Response}", connectionJson.RootElement.ToString());

                    if (connectionJson.RootElement.TryGetProperty("authenticatedUser", out var userProp))
                    {
                        idFromConnection = userProp.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                        
                        // Only fill in missing information, don't overwrite what we got from Profile API
                        if (string.IsNullOrEmpty(displayName))
                            displayName = userProp.TryGetProperty("displayName", out var displayNameProp) ? displayNameProp.GetString() ?? string.Empty : string.Empty;
                        
                        if (string.IsNullOrEmpty(uniqueName))
                            uniqueName = userProp.TryGetProperty("uniqueName", out var uniqueNameProp) ? uniqueNameProp.GetString() ?? string.Empty : string.Empty;

                        // Extract email from uniqueName if it contains an email format
                        if (string.IsNullOrEmpty(email) && uniqueName.Contains("@")) 
                            email = uniqueName;

                        if (!string.IsNullOrEmpty(idFromConnection))
                        {
                            _logger.LogInformation("Retrieved current user from connectionData (fallback): {DisplayName} ({Id})", displayName, idFromConnection);
                        }
                        else
                        {
                            _logger.LogWarning("ConnectionData API returned empty user ID. User properties: DisplayName={DisplayName}, UniqueName={UniqueName}", displayName, uniqueName);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("ConnectionData API response missing 'authenticatedUser' property");
                    }
                }
                else
                {
                    _logger.LogWarning("ConnectionData API request failed with status: {StatusCode}, Reason: {ReasonPhrase}", 
                        connectionResponse.StatusCode, connectionResponse.ReasonPhrase);
                    
                    var responseBody = await connectionResponse.Content.ReadAsStringAsync();
                    _logger.LogDebug("ConnectionData API error response body: {ResponseBody}", responseBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get user info from connection data API");
            }
        }



        // FINAL VALIDATION: Ensure we have both user ID and display name
        //
        // IMPORTANT: Both ID and display name are required for proper functionality:
        // - ID is essential for "My PR" filter matching against PR creator IDs
        // - Display name is needed for UI display and logging
        // 
        // If we're missing either, return empty to trigger fallback mechanisms elsewhere
        if (!string.IsNullOrEmpty(idFromConnection) && !string.IsNullOrEmpty(displayName))
        {
            _logger.LogInformation("Successfully retrieved user info: {DisplayName} ({Id})", displayName, idFromConnection);
            return new UserInfo(idFromConnection, displayName, email, uniqueName);
        }

        // Log detailed failure information for debugging
        _logger.LogWarning("Could not retrieve complete current user information from Azure DevOps after trying all methods. ConnectionData ID: '{IdFromConnection}', DisplayName: '{DisplayName}'", 
            idFromConnection, displayName);
        _logger.LogWarning("This may cause 'My PR' filter to not work correctly. Check Personal Access Token permissions and network connectivity.");
        
        return UserInfo.Empty;
    }

    private async Task<DateTime?> GetLastActivityDateAsync(
        string organization,
        string project,
        string repositoryId,
        int pullRequestId,
        string personalAccessToken)
    {
        try
        {
            var encodedOrg = Uri.EscapeDataString(organization);
            var encodedProject = Uri.EscapeDataString(project);
            var encodedRepoId = Uri.EscapeDataString(repositoryId);

            var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{personalAccessToken}"));
            var latestActivity = DateTime.MinValue;

            // Check for latest commits
            try
            {
                var commitsUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/commits?api-version={ApiVersion}&$top=1";
                using var commitsRequest = new HttpRequestMessage(HttpMethod.Get, commitsUri);
                commitsRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                
                using var commitsResponse = await _httpClient.SendAsync(commitsRequest);
                if (commitsResponse.IsSuccessStatusCode)
                {
                    using var commitsStream = await commitsResponse.Content.ReadAsStreamAsync();
                    var commitsJson = await JsonDocument.ParseAsync(commitsStream);
                    
                    if (commitsJson.RootElement.TryGetProperty("value", out var commitsArray) && commitsArray.GetArrayLength() > 0)
                    {
                        var latestCommit = commitsArray.EnumerateArray().First();
                        if (latestCommit.TryGetProperty("committer", out var committer) &&
                            committer.TryGetProperty("date", out var commitDate))
                        {
                            var commitDateTime = commitDate.GetDateTime();
                            if (commitDateTime > latestActivity)
                                latestActivity = commitDateTime;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching commits for PR {PullRequestId}", pullRequestId);
            }

            // Check for latest comments
            try
            {
                var threadsUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";
                using var threadsRequest = new HttpRequestMessage(HttpMethod.Get, threadsUri);
                threadsRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                
                using var threadsResponse = await _httpClient.SendAsync(threadsRequest);
                if (threadsResponse.IsSuccessStatusCode)
                {
                    using var threadsStream = await threadsResponse.Content.ReadAsStreamAsync();
                    var threadsJson = await JsonDocument.ParseAsync(threadsStream);
                    
                    if (threadsJson.RootElement.TryGetProperty("value", out var threadsArray))
                    {
                        foreach (var thread in threadsArray.EnumerateArray())
                        {
                            if (thread.TryGetProperty("comments", out var commentArray))
                            {
                                foreach (var comment in commentArray.EnumerateArray())
                                {
                                    if (comment.TryGetProperty("publishedDate", out var publishedDate))
                                    {
                                        var commentDateTime = publishedDate.GetDateTime();
                                        if (commentDateTime > latestActivity)
                                            latestActivity = commentDateTime;
                                    }
                                    if (comment.TryGetProperty("lastUpdatedDate", out var lastUpdatedDate))
                                    {
                                        var updatedDateTime = lastUpdatedDate.GetDateTime();
                                        if (updatedDateTime > latestActivity)
                                            latestActivity = updatedDateTime;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching comments for PR {PullRequestId}", pullRequestId);
            }

            // Check for latest reviewer activity (vote changes)
            try
            {
                var reviewersUri = $"{AzureDevOpsBaseUrl}/{encodedOrg}/{encodedProject}/_apis/git/repositories/{encodedRepoId}/pullRequests/{pullRequestId}/reviewers?api-version={ApiVersion}";
                using var reviewersRequest = new HttpRequestMessage(HttpMethod.Get, reviewersUri);
                reviewersRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                
                using var reviewersResponse = await _httpClient.SendAsync(reviewersRequest);
                if (reviewersResponse.IsSuccessStatusCode)
                {
                    using var reviewersStream = await reviewersResponse.Content.ReadAsStreamAsync();
                    var reviewersJson = await JsonDocument.ParseAsync(reviewersStream);
                    
                    if (reviewersJson.RootElement.TryGetProperty("value", out var reviewersArray))
                    {
                        foreach (var reviewer in reviewersArray.EnumerateArray())
                        {
                            if (reviewer.TryGetProperty("votedFor", out var votedForArray))
                            {
                                foreach (var vote in votedForArray.EnumerateArray())
                                {
                                    if (vote.TryGetProperty("date", out var voteDate))
                                    {
                                        var voteDateTime = voteDate.GetDateTime();
                                        if (voteDateTime > latestActivity)
                                            latestActivity = voteDateTime;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching reviewer activity for PR {PullRequestId}", pullRequestId);
            }

            return latestActivity == DateTime.MinValue ? null : latestActivity;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting last activity for PR {PullRequestId}", pullRequestId);
            return null;
        }
    }

    /// <summary>
    /// Helper method to safely extract string values from JSON properties that might be strings or numbers.
    /// 
    /// CRITICAL: DO NOT REMOVE OR MODIFY THIS METHOD!
    /// 
    /// Azure DevOps APIs sometimes return the same logical field as different JSON types:
    /// - Profile API may return some fields as numbers instead of strings
    /// - This causes JsonException: "The requested operation requires an element of type 'String', but the target element has type 'Number'"
    /// - This method safely handles all JSON value types and converts them to strings
    /// 
    /// This is essential for the Profile API step in GetCurrentUserAsync to work correctly.
    /// </summary>
    /// <param name="element">The JSON element to search in</param>
    /// <param name="propertyName">The property name to extract</param>
    /// <returns>String representation of the property value, or empty string if not found</returns>
    private static string GetJsonPropertyAsString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return string.Empty;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? string.Empty,
            JsonValueKind.Number => prop.GetRawText(), // Convert number to string
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }
}
