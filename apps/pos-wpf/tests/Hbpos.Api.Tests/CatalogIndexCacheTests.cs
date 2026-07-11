using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class CatalogIndexCacheTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task GetOrBuildAsync_ReusesSameStoreIndexWithinTtl()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = new CatalogIndexCache(timeProvider, TimeSpan.FromMinutes(2));
        var buildCount = 0;

        var first = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        var second = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, buildCount);

        Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken _)
        {
            buildCount++;
            return Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01"));
        }
    }

    [Fact]
    public async Task GetOrBuildAsync_RebuildsAfterTtlExpires()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = new CatalogIndexCache(timeProvider, TimeSpan.FromMinutes(2));
        var buildCount = 0;

        await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(3));
        await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.Equal(2, buildCount);

        Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken _)
        {
            buildCount++;
            return Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01"));
        }
    }

    [Fact]
    public async Task GetOrBuildAsync_DoesNotReuseAcrossStores()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = new CatalogIndexCache(timeProvider, TimeSpan.FromMinutes(2));
        var buildCount = 0;

        await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        await cache.GetOrBuildAsync("S02", since: null, BuildAsync, CancellationToken.None);

        Assert.Equal(2, buildCount);

        Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken _)
        {
            buildCount++;
            return Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01"));
        }
    }

    [Fact]
    public async Task GetOrBuildAsync_CoalescesConcurrentBuildsForSameStore()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = new CatalogIndexCache(timeProvider, TimeSpan.FromMinutes(2));
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBuildToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var buildCount = 0;

        var first = cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        await buildStarted.Task;
        var second = cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        allowBuildToFinish.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, buildCount);

        async Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken _)
        {
            buildCount++;
            buildStarted.SetResult();
            await allowBuildToFinish.Task;
            return CreateResult("S01");
        }
    }

    [Fact]
    public async Task GetOrBuildAsync_OwnerCancellationKeepsSharedBuildAliveUntilCompletion()
    {
        var cache = new CatalogIndexCache(new MutableTimeProvider(GeneratedAt), TimeSpan.FromMinutes(2));
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBuildToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstCancellation = new CancellationTokenSource();
        var buildCount = 0;

        var first = cache.GetOrBuildAsync("S01", since: null, BuildAsync, firstCancellation.Token);
        await buildStarted.Task;
        var second = cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        firstCancellation.Cancel();
        Assert.False(first.IsCompleted);
        allowBuildToFinish.SetResult();

        var results = await Task.WhenAll(first, second);
        var cached = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.Same(results[0], results[1]);
        Assert.Same(results[0], cached);
        Assert.Equal(1, buildCount);

        async Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken cancellationToken)
        {
            buildCount++;
            buildStarted.SetResult();
            await allowBuildToFinish.Task.WaitAsync(cancellationToken);
            return CreateResult("S01");
        }
    }

    private static CatalogIndexBuildResult CreateResult(string storeCode)
    {
        return new CatalogIndexBuildResult(
            storeCode,
            GeneratedAt,
            [],
            new CatalogSellableIndex(storeCode, GeneratedAt, []));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }
}
