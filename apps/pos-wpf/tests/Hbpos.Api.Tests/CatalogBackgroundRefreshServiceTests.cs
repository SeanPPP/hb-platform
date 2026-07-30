using Hbpos.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hbpos.Api.Tests;

public sealed class CatalogBackgroundRefreshServiceTests
{
    [Fact]
    public async Task QueueRefresh_CoalescesSameStoreAndRunsInHostedScope()
    {
        var refreshStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefresh = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workerCreatedCount = 0;
        var refreshCount = 0;
        var services = new ServiceCollection();
        services.AddScoped<ICatalogIndexRefreshWorker>(_ =>
        {
            Interlocked.Increment(ref workerCreatedCount);
            return new DelegateRefreshWorker(async cancellationToken =>
            {
                Interlocked.Increment(ref refreshCount);
                refreshStarted.TrySetResult();
                await allowRefresh.Task.WaitAsync(cancellationToken);
                refreshCompleted.TrySetResult();
            });
        });
        await using var provider = services.BuildServiceProvider();
        using var service = new CatalogBackgroundRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CatalogBackgroundRefreshService>.Instance);
        await service.StartAsync(CancellationToken.None);

        try
        {
            service.QueueRefresh("S01");
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            for (var request = 0; request < 20; request++)
            {
                service.QueueRefresh(request % 2 == 0 ? "s01" : " S01 ");
            }

            Assert.Equal(1, Volatile.Read(ref workerCreatedCount));
            Assert.Equal(1, Volatile.Read(ref refreshCount));

            allowRefresh.TrySetResult();
            await refreshCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            allowRefresh.TrySetResult();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task QueueRefreshAsync_RequeuesSameStoreFromCompletionContinuation()
    {
        var firstRefreshStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstRefreshToComplete = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCount = 0;
        var services = new ServiceCollection();
        services.AddScoped<ICatalogIndexRefreshWorker>(_ =>
            new DelegateRefreshWorker(async cancellationToken =>
            {
                if (Interlocked.Increment(ref refreshCount) == 1)
                {
                    firstRefreshStarted.TrySetResult();
                    await allowFirstRefreshToComplete.Task.WaitAsync(cancellationToken);
                }
            }));
        await using var provider = services.BuildServiceProvider();
        using var service = new CatalogBackgroundRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CatalogBackgroundRefreshService>.Instance);
        await service.StartAsync(CancellationToken.None);

        try
        {
            var firstRefresh = service.QueueRefreshAsync("S01");
            await firstRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // 内联调度器让 continuation 在首次完成的同步窗口内立即重排，稳定复现终态去重键的竞态。
            var secondRefresh = firstRefresh.ContinueWith(
                _ => service.QueueRefreshAsync(" s01 "),
                CancellationToken.None,
                TaskContinuationOptions.None,
                InlineTaskScheduler.Instance).Unwrap();

            allowFirstRefreshToComplete.TrySetResult();
            await Task.WhenAll(firstRefresh, secondRefresh).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(2, Volatile.Read(ref refreshCount));
        }
        finally
        {
            allowFirstRefreshToComplete.TrySetResult();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task QueueRefreshAsync_RetriesFailedStoreWithoutOccupyingBuildSlot()
    {
        var retryDelayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetryDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var otherStoresStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOtherStores = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStoreAttempts = 0;
        var otherStoreStarts = 0;
        var activeBuilds = 0;
        var maximumActiveBuilds = 0;
        var services = new ServiceCollection();
        services.AddScoped<ICatalogIndexRefreshWorker>(_ =>
            new StoreDelegateRefreshWorker(async (storeCode, cancellationToken) =>
            {
                if (storeCode == "S01" && Interlocked.Increment(ref firstStoreAttempts) == 1)
                {
                    throw new InvalidOperationException("first build fails");
                }

                var active = Interlocked.Increment(ref activeBuilds);
                UpdateMaximum(ref maximumActiveBuilds, active);
                try
                {
                    if (storeCode is "S02" or "S03")
                    {
                        if (Interlocked.Increment(ref otherStoreStarts) == 2)
                        {
                            otherStoresStarted.TrySetResult();
                        }

                        await releaseOtherStores.Task.WaitAsync(cancellationToken);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeBuilds);
                }
            }));
        await using var provider = services.BuildServiceProvider();
        using var service = new CatalogBackgroundRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CatalogBackgroundRefreshService>.Instance,
            async (_, cancellationToken) =>
            {
                retryDelayStarted.TrySetResult();
                await releaseRetryDelay.Task.WaitAsync(cancellationToken);
            });
        await service.StartAsync(CancellationToken.None);

        try
        {
            var firstStore = service.QueueRefreshAsync("S01");
            await retryDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var secondStore = service.QueueRefreshAsync("S02");
            var thirdStore = service.QueueRefreshAsync("S03");
            await otherStoresStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(2, Volatile.Read(ref maximumActiveBuilds));
            Assert.False(firstStore.IsCompleted);

            releaseOtherStores.TrySetResult();
            await Task.WhenAll(secondStore, thirdStore).WaitAsync(TimeSpan.FromSeconds(2));

            releaseRetryDelay.TrySetResult();
            await firstStore.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, Volatile.Read(ref firstStoreAttempts));
        }
        finally
        {
            releaseOtherStores.TrySetResult();
            releaseRetryDelay.TrySetResult();
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (observed >= candidate || Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
            {
                return;
            }
        }
    }

    private sealed class DelegateRefreshWorker(
        Func<CancellationToken, Task> refreshAsync)
        : ICatalogIndexRefreshWorker
    {
        public Task RefreshCatalogIndexAsync(
            string storeCode,
            CancellationToken cancellationToken)
        {
            return refreshAsync(cancellationToken);
        }
    }

    private sealed class StoreDelegateRefreshWorker(
        Func<string, CancellationToken, Task> refreshAsync)
        : ICatalogIndexRefreshWorker
    {
        public Task RefreshCatalogIndexAsync(
            string storeCode,
            CancellationToken cancellationToken)
        {
            return refreshAsync(storeCode, cancellationToken);
        }
    }

    private sealed class InlineTaskScheduler : TaskScheduler
    {
        public static InlineTaskScheduler Instance { get; } = new();

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return [];
        }

        protected override void QueueTask(Task task)
        {
            TryExecuteTask(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return TryExecuteTask(task);
        }
    }
}
