using BlazorApp.Api.Services.Logging;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ApplicationLogQueueTests
{
    [Fact]
    public async Task TryEnqueue_队列满时丢弃最旧日志并累加可观测计数()
    {
        var queue = new ApplicationLogQueue(capacity: 2);

        Assert.True(queue.TryEnqueue(CreateItem("first")));
        Assert.True(queue.TryEnqueue(CreateItem("second")));
        Assert.True(queue.TryEnqueue(CreateItem("third")));

        var snapshot = queue.GetRuntimeSnapshot();

        Assert.Equal(1, snapshot.DroppedOldestCount);
        Assert.Equal(0, snapshot.EnqueueFailureCount);

        var firstRead = await queue.ReadAsync(CancellationToken.None);
        var secondRead = await queue.ReadAsync(CancellationToken.None);

        Assert.Equal("second", firstRead.Message);
        Assert.Equal("third", secondRead.Message);
    }

    private static ApplicationLogIngestItemDto CreateItem(string message)
    {
        return new ApplicationLogIngestItemDto
        {
            Level = "Error",
            Message = message,
            TimestampUtc = DateTime.UtcNow,
            ProjectCode = "HBBBackend",
            Environment = "test",
            SourceType = "Backend",
        };
    }
}
