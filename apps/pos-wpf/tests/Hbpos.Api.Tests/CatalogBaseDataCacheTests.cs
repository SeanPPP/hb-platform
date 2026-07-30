using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class CatalogBaseDataCacheTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_requests_share_one_global_build()
    {
        var buildStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource<CatalogBaseData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var buildCount = 0;
        var cache = new CatalogBaseDataCache(
            new MutableTimeProvider(GeneratedAt),
            TimeSpan.FromMinutes(20));

        Task<CatalogBaseData> BuildAsync(CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.CanBeCanceled);
            Interlocked.Increment(ref buildCount);
            buildStarted.TrySetResult();
            return releaseBuild.Task;
        }

        var requests = Enumerable.Range(0, 32)
            .Select(_ => cache.GetOrCreateAsync(BuildAsync, CancellationToken.None))
            .ToArray();
        await buildStarted.Task;

        Assert.Equal(1, Volatile.Read(ref buildCount));

        var expected = CreateData();
        releaseBuild.SetResult(expected);
        var results = await Task.WhenAll(requests);

        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(GeneratedAt.AddMinutes(20), results[0].ValidUntil);
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_cancel_shared_build()
    {
        var releaseBuild = new TaskCompletionSource<CatalogBaseData>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var buildCount = 0;
        var cache = new CatalogBaseDataCache(
            new MutableTimeProvider(GeneratedAt),
            TimeSpan.FromMinutes(20));
        using var waiterCancellation = new CancellationTokenSource();

        Task<CatalogBaseData> BuildAsync(CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.CanBeCanceled);
            Interlocked.Increment(ref buildCount);
            return releaseBuild.Task;
        }

        var cancelledWaiter = cache.GetOrCreateAsync(
            BuildAsync,
            waiterCancellation.Token);
        var survivingWaiter = cache.GetOrCreateAsync(
            BuildAsync,
            CancellationToken.None);
        waiterCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
        var expected = CreateData();
        releaseBuild.SetResult(expected);

        var survivingResult = await survivingWaiter;
        Assert.Equal(expected.GeneratedAt, survivingResult.GeneratedAt);
        Assert.Equal(GeneratedAt.AddMinutes(20), survivingResult.ValidUntil);
        Assert.Same(
            survivingResult,
            await cache.GetOrCreateAsync(BuildAsync, CancellationToken.None));
        Assert.Equal(1, Volatile.Read(ref buildCount));
    }

    [Fact]
    public async Task Cached_value_is_rebuilt_only_after_ttl_expires()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = new CatalogBaseDataCache(
            timeProvider,
            TimeSpan.FromMinutes(20));
        var buildCount = 0;

        Task<CatalogBaseData> BuildAsync(CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.CanBeCanceled);
            Interlocked.Increment(ref buildCount);
            return Task.FromResult(CreateData(timeProvider.GetUtcNow()));
        }

        var first = await cache.GetOrCreateAsync(BuildAsync, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(19));
        var warm = await cache.GetOrCreateAsync(BuildAsync, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        var rebuilt = await cache.GetOrCreateAsync(BuildAsync, CancellationToken.None);

        Assert.Same(first, warm);
        Assert.NotSame(first, rebuilt);
        Assert.Equal(2, buildCount);
    }

    [Fact]
    public async Task Failed_build_is_not_cached()
    {
        var cache = new CatalogBaseDataCache(
            new MutableTimeProvider(GeneratedAt),
            TimeSpan.FromMinutes(20));
        var buildCount = 0;

        async Task<CatalogBaseData> BuildAsync(CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.CanBeCanceled);
            if (Interlocked.Increment(ref buildCount) == 1)
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            }

            return CreateData();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetOrCreateAsync(BuildAsync, CancellationToken.None));

        var rebuilt = await cache.GetOrCreateAsync(BuildAsync, CancellationToken.None);

        Assert.Equal(2, buildCount);
        Assert.Equal(GeneratedAt, rebuilt.GeneratedAt);
    }

    [Fact]
    public async Task Host_stop_cancels_shared_build_and_allows_a_later_retry()
    {
        var cache = new CatalogBaseDataCache(new MutableTimeProvider(GeneratedAt), TimeSpan.FromMinutes(20));
        using var hostStopping = new CancellationTokenSource();
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cancelled = cache.GetOrCreateAsync(
            async token =>
            {
                buildStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return CreateData();
            },
            waiterCancellationToken: CancellationToken.None,
            buildCancellationToken: hostStopping.Token);
        await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        hostStopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var retried = await cache.GetOrCreateAsync(_ => Task.FromResult(CreateData()), CancellationToken.None);
        Assert.Equal(GeneratedAt, retried.GeneratedAt);
    }

    private static CatalogBaseData CreateData(DateTimeOffset? generatedAt = null)
    {
        return new CatalogBaseData(
            generatedAt ?? GeneratedAt,
            Array.Empty<ProductPriceRecord>(),
            Array.Empty<ProductSetCodeRecord>());
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount)
        {
            _utcNow += amount;
        }
    }
}
