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
    public async Task GetOrBuildAsync_StartsTtlAfterSuccessfulLongBuild()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = new CatalogIndexCache(timeProvider, TimeSpan.FromMinutes(2));
        var buildCount = 0;

        var first = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        var immediatelyReused = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.Same(first, immediatelyReused);
        Assert.Equal(1, buildCount);

        timeProvider.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        var rebuilt = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.NotSame(first, rebuilt);
        Assert.Equal(2, buildCount);

        Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken _)
        {
            buildCount++;
            if (buildCount == 1)
            {
                // 模拟首次构建耗时超过 TTL，TTL 应从成功完成时才开始计算。
                timeProvider.Advance(TimeSpan.FromMinutes(3));
            }

            return Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01"));
        }
    }

    [Fact]
    public async Task GetOrBuildAsync_CacheHitDoesNotExtendCompletedEntryTtl()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = new CatalogIndexCache(timeProvider, TimeSpan.FromMinutes(2));
        var buildCount = 0;

        var first = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(59));
        var cacheHit = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.Same(first, cacheHit);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var rebuilt = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.NotSame(first, rebuilt);
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
    public async Task GetOrBuildAsync_WaiterCancellationDoesNotRemoveRunningSharedBuild()
    {
        var cache = new CatalogIndexCache(new MutableTimeProvider(GeneratedAt), TimeSpan.FromMinutes(2));
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBuildToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var waiterCancellation = new CancellationTokenSource();
        var buildCount = 0;

        var owner = cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        await buildStarted.Task;
        var waiter = cache.GetOrBuildAsync("S01", since: null, BuildAsync, waiterCancellation.Token);

        waiterCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.False(owner.IsCompleted);

        allowBuildToFinish.SetResult();
        var completed = await owner;
        var cached = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.Same(completed, cached);
        Assert.Equal(1, buildCount);

        async Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken cancellationToken)
        {
            buildCount++;
            buildStarted.SetResult();
            await allowBuildToFinish.Task.WaitAsync(cancellationToken);
            return CreateResult("S01");
        }
    }

    [Fact]
    public async Task GetOrBuildAsync_RebuildsAfterBuildFaults()
    {
        var cache = new CatalogIndexCache(new MutableTimeProvider(GeneratedAt), TimeSpan.FromMinutes(2));
        var buildCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None));
        var rebuilt = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.NotNull(rebuilt);
        Assert.Equal(2, buildCount);

        Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken _)
        {
            buildCount++;
            return buildCount == 1
                ? Task.FromException<CatalogIndexBuildResult?>(new InvalidOperationException("构建失败"))
                : Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01"));
        }
    }

    [Fact]
    public async Task GetOrBuildAsync_RebuildsAfterBuildReturnsNull()
    {
        var cache = new CatalogIndexCache(new MutableTimeProvider(GeneratedAt), TimeSpan.FromMinutes(2));
        var buildCount = 0;

        var first = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        var rebuilt = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.Null(first);
        Assert.NotNull(rebuilt);
        Assert.Equal(2, buildCount);

        Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken _)
        {
            buildCount++;
            return Task.FromResult(buildCount == 1 ? null : CreateResult("S01"));
        }
    }

    [Fact]
    public async Task InvalidateStore_DuringBuildPreventsOldOwnerFromRevivingEntry()
    {
        var cache = new CatalogIndexCache(new MutableTimeProvider(GeneratedAt), TimeSpan.FromMinutes(2));
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBuildToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var buildCount = 0;

        var oldOwner = cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        await buildStarted.Task;

        cache.InvalidateStore("S01");
        allowBuildToFinish.SetResult();
        var oldResult = await oldOwner;
        var rebuilt = await cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);

        Assert.NotSame(oldResult, rebuilt);
        Assert.Equal(2, buildCount);

        async Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken cancellationToken)
        {
            buildCount++;
            if (buildCount == 1)
            {
                buildStarted.SetResult();
                await allowBuildToFinish.Task.WaitAsync(cancellationToken);
            }

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

    [Fact]
    public async Task InvalidateStore_KeepsCompletedSnapshotAvailableByVersion()
    {
        var cache = CreateSnapshotCache(new MutableTimeProvider(GeneratedAt));
        var completed = await cache.GetOrBuildAsync(
            "S01",
            since: null,
            _ => Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01")),
            CancellationToken.None);

        cache.InvalidateStore("S01");
        var pinned = cache.GetByVersion(
            "S01",
            since: null,
            completed!.CatalogIndex.CatalogVersion);

        Assert.Same(completed, pinned);
    }

    [Fact]
    public async Task InvalidateStore_DuringBuildStillPinsCompletedSnapshot()
    {
        var cache = CreateSnapshotCache(new MutableTimeProvider(GeneratedAt));
        var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowBuildToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var owner = cache.GetOrBuildAsync("S01", since: null, BuildAsync, CancellationToken.None);
        await buildStarted.Task;
        cache.InvalidateStore("S01");
        allowBuildToFinish.SetResult();
        var completed = await owner;

        var pinned = cache.GetByVersion(
            "S01",
            since: null,
            completed!.CatalogIndex.CatalogVersion);

        Assert.Same(completed, pinned);

        async Task<CatalogIndexBuildResult?> BuildAsync(CancellationToken cancellationToken)
        {
            buildStarted.SetResult();
            await allowBuildToFinish.Task.WaitAsync(cancellationToken);
            return CreateResult("S01");
        }
    }

    [Fact]
    public async Task GetByVersion_ExpiresSnapshotAfterThirtyMinutes()
    {
        var timeProvider = new MutableTimeProvider(GeneratedAt);
        var cache = CreateSnapshotCache(timeProvider);
        var completed = await cache.GetOrBuildAsync(
            "S01",
            since: null,
            _ => Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01")),
            CancellationToken.None);
        var version = completed!.CatalogIndex.CatalogVersion;

        timeProvider.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
        Assert.Same(completed, cache.GetByVersion("S01", since: null, version));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(cache.GetByVersion("S01", since: null, version));
    }

    [Fact]
    public async Task CompletedSnapshots_KeepOnlyNewestTwoVersionsPerStore()
    {
        var cache = CreateSnapshotCache(new MutableTimeProvider(GeneratedAt));
        var completed = new List<CatalogIndexBuildResult>();

        for (var number = 1; number <= 3; number++)
        {
            var version = $"catalog-v1:{number}";
            var result = await cache.GetOrBuildAsync(
                "S01",
                since: null,
                _ => Task.FromResult<CatalogIndexBuildResult?>(
                    CreateResult("S01", version)),
                CancellationToken.None);
            completed.Add(result!);
            cache.InvalidateStore("S01");
        }

        Assert.Null(cache.GetByVersion("S01", since: null, "catalog-v1:1"));
        Assert.Same(completed[1], cache.GetByVersion("S01", since: null, "catalog-v1:2"));
        Assert.Same(completed[2], cache.GetByVersion("S01", since: null, "catalog-v1:3"));
    }

    [Fact]
    public async Task CompletedSnapshotLimit_IsIndependentPerStore()
    {
        var cache = CreateSnapshotCache(new MutableTimeProvider(GeneratedAt));
        var s01 = await cache.GetOrBuildAsync(
            "S01",
            since: null,
            _ => Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S01", "catalog-v1:s01")),
            CancellationToken.None);
        var s02 = await cache.GetOrBuildAsync(
            "S02",
            since: null,
            _ => Task.FromResult<CatalogIndexBuildResult?>(CreateResult("S02", "catalog-v1:s02")),
            CancellationToken.None);

        cache.InvalidateStore("S01");
        cache.InvalidateStore("S02");

        Assert.Same(s01, cache.GetByVersion("S01", since: null, "catalog-v1:s01"));
        Assert.Same(s02, cache.GetByVersion("S02", since: null, "catalog-v1:s02"));
    }

    [Fact]
    public async Task ConcurrentBuilds_PinTwoVersionsForTheSameStore()
    {
        var cache = CreateSnapshotCache(new MutableTimeProvider(GeneratedAt));
        var first = cache.GetOrBuildAsync(
            "S01",
            since: null,
            _ => Task.FromResult<CatalogIndexBuildResult?>(
                CreateResult("S01", "catalog-v1:full")),
            CancellationToken.None);
        var secondSince = GeneratedAt.AddMinutes(-1);
        var second = cache.GetOrBuildAsync(
            "S01",
            secondSince,
            _ => Task.FromResult<CatalogIndexBuildResult?>(
                CreateResult("S01", "catalog-v1:delta")),
            CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        Assert.Same(
            results[0],
            cache.GetByVersion("S01", since: null, "catalog-v1:full"));
        Assert.Same(
            results[1],
            cache.GetByVersion("S01", secondSince, "catalog-v1:delta"));
    }

    [Fact]
    public async Task SnapshotEviction_DoesNotRemoveCurrentActiveEntry()
    {
        var cache = CreateSnapshotCache(new MutableTimeProvider(GeneratedAt));
        var buildCount = 0;
        var first = await cache.GetOrBuildAsync(
            "S01",
            since: null,
            _ => Build("catalog-v1:active"),
            CancellationToken.None);
        await cache.GetOrBuildAsync(
            "S01",
            GeneratedAt.AddMinutes(-1),
            _ => Build("catalog-v1:second"),
            CancellationToken.None);
        await cache.GetOrBuildAsync(
            "S01",
            GeneratedAt.AddMinutes(-2),
            _ => Build("catalog-v1:third"),
            CancellationToken.None);

        var activeAgain = await cache.GetOrBuildAsync(
            "S01",
            since: null,
            _ => Build("catalog-v1:unexpected"),
            CancellationToken.None);

        Assert.Same(first, activeAgain);
        Assert.Equal(3, buildCount);
        Assert.Null(cache.GetByVersion("S01", since: null, "catalog-v1:active"));

        Task<CatalogIndexBuildResult?> Build(string version)
        {
            buildCount++;
            return Task.FromResult<CatalogIndexBuildResult?>(
                CreateResult("S01", version));
        }
    }

    private static CatalogIndexCache CreateSnapshotCache(MutableTimeProvider timeProvider)
    {
        return new CatalogIndexCache(
            timeProvider,
            TimeSpan.FromMinutes(2),
            snapshotTtl: TimeSpan.FromMinutes(30),
            maxSnapshotsPerStore: 2);
    }

    private static CatalogIndexBuildResult CreateResult(
        string storeCode,
        string? catalogVersion = null)
    {
        return new CatalogIndexBuildResult(
            storeCode,
            GeneratedAt,
            [],
            new CatalogSellableIndex(storeCode, GeneratedAt, [], catalogVersion));
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
