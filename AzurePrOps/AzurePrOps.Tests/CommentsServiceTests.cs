using AzurePrOps.AzureConnection.Services;
using AzurePrOps.ReviewLogic.Models;
using AzurePrOps.ReviewLogic.Services;
using Moq;

namespace AzurePrOps.Tests;

public class CommentsServiceTests
{
    [Fact]
    public async Task GetThreadsAsync_ForwardsCall()
    {
        var expected = new List<CommentThread> { new CommentThread { ThreadId = 1 } } as IReadOnlyList<CommentThread>;
        var mockClient = new Mock<IAzureDevOpsClient>();
        mockClient.Setup(c => c.GetPullRequestThreadsAsync("org", "proj", "repo", 1, "pat"))
                  .ReturnsAsync(expected);
        var service = new CommentsService(mockClient.Object);

        var result = await service.GetThreadsAsync("org", "proj", "repo", 1, "pat");

        Assert.Equal(expected, result);
        mockClient.VerifyAll();
    }

    [Fact]
    public async Task Methods_RetryOnFailure()
    {
        var callCount = 0;
        var mockClient = new Mock<IAzureDevOpsClient>();
        mockClient.Setup(c => c.UpdateThreadStatusAsync("org", "proj", "repo", 1, 2, "closed", "pat"))
                  .Returns(() =>
                  {
                      callCount++;
                      if (callCount == 1) throw new Exception("fail");
                      return Task.CompletedTask;
                  });

        mockClient.Setup(c => c.UpdateThreadStatusAsync("org", "proj", "repo", 1, 2, "active", "pat"))
                  .Returns(() =>
                  {
                      callCount++;
                      if (callCount == 2) throw new Exception("fail2");
                      return Task.CompletedTask;
                  });

        var service = new CommentsService(mockClient.Object);
        await service.ResolveThreadAsync("org", "proj", "repo", 1, 2, "pat");
        await service.UpdateThreadStatusAsync("org", "proj", "repo", 1, 2, false, "pat");

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task UpdateThreadStatusAsync_ForwardsCall()
    {
        var mockClient = new Mock<IAzureDevOpsClient>();
        mockClient.Setup(c => c.UpdateThreadStatusAsync("org", "proj", "repo", 1, 2, "active", "pat"))
                  .Returns(Task.CompletedTask)
                  .Verifiable();

        var service = new CommentsService(mockClient.Object);
        await service.UpdateThreadStatusAsync("org", "proj", "repo", 1, 2, false, "pat");

        mockClient.VerifyAll();
    }
}
