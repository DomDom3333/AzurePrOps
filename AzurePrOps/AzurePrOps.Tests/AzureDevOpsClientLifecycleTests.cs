using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using AzurePrOps.AzureConnection.Services;
using AzurePrOps.ReviewLogic.Models;
using Moq;
using Moq.Protected;
using Xunit;

namespace AzurePrOps.Tests;

public class AzureDevOpsClientLifecycleTests
{
    private static AzureDevOpsClient CreateClient(Mock<HttpMessageHandler> handler, Action<HttpRequestMessage> onSend)
    {
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => onSend(req))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });

        var httpClient = new HttpClient(handler.Object);
        return new AzureDevOpsClient(httpClient);
    }

    private static string BasicAuth(string pat) => Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":" + pat));

    [Fact]
    public async Task ApprovePullRequestAsync_SendsVote10()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new Mock<HttpMessageHandler>();
        var client = CreateClient(handler, r => { captured = r; body = r.Content?.ReadAsStringAsync().Result; });

        await client.ApprovePullRequestAsync("org", "proj", "repo", 1, "rev", "pat");

        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
        Assert.Equal("https://dev.azure.com/org/proj/_apis/git/repositories/repo/pullRequests/1/reviewers/rev?api-version=7.1", captured.RequestUri!.ToString());
        Assert.Equal("Basic", captured.Headers.Authorization?.Scheme);
        Assert.Equal(BasicAuth("pat"), captured.Headers.Authorization?.Parameter);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(10, json.RootElement.GetProperty("vote").GetInt32());
    }

    [Fact]
    public async Task ApproveWithSuggestionsAsync_SendsVote5()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new Mock<HttpMessageHandler>();
        var client = CreateClient(handler, r => { captured = r; body = r.Content?.ReadAsStringAsync().Result; });

        await client.ApproveWithSuggestionsAsync("org", "proj", "repo", 1, "rev", "pat");

        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        Assert.NotNull(captured);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(5, json.RootElement.GetProperty("vote").GetInt32());
    }

    [Fact]
    public async Task RejectPullRequestAsync_SendsVoteMinus10()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new Mock<HttpMessageHandler>();
        var client = CreateClient(handler, r => { captured = r; body = r.Content?.ReadAsStringAsync().Result; });

        await client.RejectPullRequestAsync("org", "proj", "repo", 1, "rev", "pat");

        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        Assert.NotNull(captured);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(-10, json.RootElement.GetProperty("vote").GetInt32());
    }

    [Fact]
    public async Task SetPullRequestDraftAsync_SendsIsDraft()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new Mock<HttpMessageHandler>();
        var client = CreateClient(handler, r => { captured = r; body = r.Content?.ReadAsStringAsync().Result; });

        await client.SetPullRequestDraftAsync("org", "proj", "repo", 2, true, "pat");

        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
        Assert.Equal("https://dev.azure.com/org/proj/_apis/git/repositories/repo/pullRequests/2?api-version=7.1", captured.RequestUri!.ToString());
        using var json = JsonDocument.Parse(body!);
        Assert.True(json.RootElement.GetProperty("isDraft").GetBoolean());
    }

    [Fact]
    public async Task CompletePullRequestAsync_SendsMergeOptions()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new Mock<HttpMessageHandler>();
        var client = CreateClient(handler, r => { captured = r; body = r.Content?.ReadAsStringAsync().Result; });
        var options = new MergeOptions(true, true, "msg");

        await client.CompletePullRequestAsync("org", "proj", "repo", 3, options, "pat");

        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("completed", root.GetProperty("status").GetString());
        var opts = root.GetProperty("completionOptions");
        Assert.True(opts.GetProperty("deleteSourceBranch").GetBoolean());
        Assert.True(opts.GetProperty("squashMerge").GetBoolean());
        Assert.Equal("msg", opts.GetProperty("mergeCommitMessage").GetString());
    }

    [Fact]
    public async Task AbandonPullRequestAsync_SendsStatusAbandoned()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new Mock<HttpMessageHandler>();
        var client = CreateClient(handler, r => { captured = r; body = r.Content?.ReadAsStringAsync().Result; });

        await client.AbandonPullRequestAsync("org", "proj", "repo", 4, "pat");

        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("abandoned", json.RootElement.GetProperty("status").GetString());
    }
}
