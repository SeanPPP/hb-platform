using System.Collections.Concurrent;
using System.Text.Json;
using Hbpos.Contracts.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hbpos.Api.Services;

public interface ICatalogIndexCache
{
    Task<CatalogIndexBuildResult?> GetOrBuildAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken);

    Task<CatalogIndexBuildResult?> GetOrBuildFreshAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken);

    Task<CatalogIndexBuildResult?> ForceRefreshAndPublishAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken);

    CatalogIndexBuildResult? GetByVersion(
        string storeCode,
        DateTimeOffset? since,
        string catalogVersion);

    void InvalidateStore(string storeCode);

    CatalogDownloadLeaseRegistry DownloadLeases { get; }

    void EnsurePlanCapacity(CatalogIndexBuildResult target);

    CatalogDownloadLease CreateFullLease(CatalogIndexBuildResult target);

    CatalogDownloadLease CreateDeltaLease(CatalogIndexBuildResult baseline, CatalogIndexBuildResult target, IReadOnlyList<CatalogDeltaOperation> operations);
}

public sealed record CatalogIndexBuildResult(
    string StoreCode,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SellableItemDto> SellableItems,
    CatalogSellableIndex CatalogIndex,
    DateTimeOffset? SourceValidUntil = null,
    PriceIndexInput? RawPriceIndexInput = null);

public sealed class CatalogIndexCache : ICatalogIndexCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultSnapshotTtl = TimeSpan.FromHours(2);
    // 每个门店默认保留八个完整版本，覆盖短时间内的多台 iPad 下载与回退窗口。
    private const int DefaultMaxSnapshotsPerStore = 8;
    private const int RawArtifactCapacity = 2;
    private const int DefaultSoftItemCapacity = 1_500_000;
    private const int DefaultHardItemCapacity = 2_500_000;
    private readonly ConcurrentDictionary<CatalogIndexCacheKey, CacheEntry> _entries = new();
    private readonly Dictionary<CatalogSnapshotCacheKey, SnapshotEntry> _snapshots = [];
    private readonly Dictionary<CatalogSnapshotCacheKey, PendingSnapshotEntry> _pendingSnapshots = [];
    private readonly Dictionary<CatalogSnapshotCacheKey, CatalogSnapshotDescriptor> _lazySnapshotDescriptors = [];
    private readonly Dictionary<CatalogSnapshotCacheKey, RawArtifactEntry> _rawArtifacts = [];
    private readonly HashSet<CatalogSnapshotCacheKey> _rawArtifactVersions = [];
    private readonly LinkedList<CatalogSnapshotCacheKey> _rawArtifactLru = [];
    private readonly object _snapshotGate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _snapshotTtl;
    private readonly int _maxSnapshotsPerStore;
    private readonly int _softItemCapacity;
    private readonly int _hardItemCapacity;
    private readonly ICatalogSnapshotStore? _snapshotStore;
    private readonly ICatalogBackgroundRefreshScheduler? _backgroundRefreshScheduler;
    private readonly CatalogDownloadLeaseRegistry _downloadLeases;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IHostApplicationLifetime? _applicationLifetime;
    private long _snapshotSequence;

    public CatalogIndexCache()
        : this(
            TimeProvider.System,
            DefaultTtl,
            DefaultSnapshotTtl,
            DefaultMaxSnapshotsPerStore,
            snapshotStore: null,
            backgroundRefreshScheduler: null)
    {
    }

    public CatalogIndexCache(TimeProvider timeProvider, TimeSpan ttl)
        : this(
            timeProvider,
            ttl,
            DefaultSnapshotTtl,
            DefaultMaxSnapshotsPerStore,
            snapshotStore: null,
            backgroundRefreshScheduler: null)
    {
    }

    public CatalogIndexCache(
        TimeProvider timeProvider,
        TimeSpan ttl,
        TimeSpan snapshotTtl,
        int maxSnapshotsPerStore)
        : this(
            timeProvider,
            ttl,
            snapshotTtl,
            maxSnapshotsPerStore,
            snapshotStore: null,
            backgroundRefreshScheduler: null)
    {
    }

    internal CatalogIndexCache(
        TimeProvider timeProvider,
        TimeSpan ttl,
        TimeSpan snapshotTtl,
        int maxSnapshotsPerStore,
        ICatalogBackgroundRefreshScheduler backgroundRefreshScheduler)
        : this(
            timeProvider,
            ttl,
            snapshotTtl,
            maxSnapshotsPerStore,
            snapshotStore: null,
            backgroundRefreshScheduler)
    {
    }

    internal CatalogIndexCache(
        TimeProvider timeProvider,
        TimeSpan ttl,
        TimeSpan snapshotTtl,
        int maxSnapshotsPerStore,
        ICatalogSnapshotStore snapshotStore,
        int softItemCapacity,
        int hardItemCapacity)
        : this(timeProvider, ttl, snapshotTtl, maxSnapshotsPerStore, snapshotStore, null,
            softItemCapacity: softItemCapacity, hardItemCapacity: hardItemCapacity)
    {
    }

    internal CatalogIndexCache(
        TimeProvider timeProvider,
        TimeSpan ttl,
        TimeSpan snapshotTtl,
        int maxSnapshotsPerStore,
        int softItemCapacity,
        int hardItemCapacity)
        : this(
            timeProvider,
            ttl,
            snapshotTtl,
            maxSnapshotsPerStore,
            snapshotStore: null,
            backgroundRefreshScheduler: null,
            softItemCapacity: softItemCapacity,
            hardItemCapacity: hardItemCapacity)
    {
    }

    public CatalogIndexCache(ICatalogSnapshotStore snapshotStore)
        : this(
            TimeProvider.System,
            DefaultTtl,
            DefaultSnapshotTtl,
            DefaultMaxSnapshotsPerStore,
            snapshotStore,
            backgroundRefreshScheduler: null)
    {
    }

    public CatalogIndexCache(
        ICatalogSnapshotStore snapshotStore,
        ICatalogBackgroundRefreshScheduler backgroundRefreshScheduler)
        : this(
            TimeProvider.System,
            DefaultTtl,
            DefaultSnapshotTtl,
            DefaultMaxSnapshotsPerStore,
            snapshotStore,
            backgroundRefreshScheduler)
    {
    }

    public CatalogIndexCache(
        ICatalogSnapshotStore snapshotStore,
        ICatalogBackgroundRefreshScheduler backgroundRefreshScheduler,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime applicationLifetime)
        : this(
            TimeProvider.System,
            DefaultTtl,
            DefaultSnapshotTtl,
            DefaultMaxSnapshotsPerStore,
            snapshotStore,
            backgroundRefreshScheduler,
            scopeFactory,
            applicationLifetime)
    {
    }

    private CatalogIndexCache(
        TimeProvider timeProvider,
        TimeSpan ttl,
        TimeSpan snapshotTtl,
        int maxSnapshotsPerStore,
        ICatalogSnapshotStore? snapshotStore,
        ICatalogBackgroundRefreshScheduler? backgroundRefreshScheduler,
        IServiceScopeFactory? scopeFactory = null,
        IHostApplicationLifetime? applicationLifetime = null,
        int softItemCapacity = DefaultSoftItemCapacity,
        int hardItemCapacity = DefaultHardItemCapacity)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl));
        }

        if (snapshotTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotTtl));
        }

        if (maxSnapshotsPerStore <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSnapshotsPerStore));
        }

        if (softItemCapacity <= 0 || hardItemCapacity < softItemCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(softItemCapacity));
        }

        _timeProvider = timeProvider;
        _ttl = ttl;
        _snapshotTtl = snapshotTtl;
        _maxSnapshotsPerStore = maxSnapshotsPerStore;
        _softItemCapacity = softItemCapacity;
        _hardItemCapacity = hardItemCapacity;
        _snapshotStore = snapshotStore;
        _backgroundRefreshScheduler = backgroundRefreshScheduler;
        _downloadLeases = new CatalogDownloadLeaseRegistry(timeProvider);
        _scopeFactory = scopeFactory;
        _applicationLifetime = applicationLifetime;
        RestorePersistedSnapshots();
    }

    public CatalogDownloadLeaseRegistry DownloadLeases => _downloadLeases;

    internal long PinnedItemCountForTests
    {
        get
        {
            lock (_snapshotGate)
            {
                return _snapshots.Values.Sum(entry => (long)entry.Result.CatalogIndex.Items.Count);
            }
        }
    }

    internal int RawArtifactVersionCountForTests
    {
        get
        {
            lock (_snapshotGate)
            {
                return _rawArtifactVersions.Count;
            }
        }
    }

    internal int PendingSnapshotOwnerCountForTests
    {
        get
        {
            lock (_snapshotGate)
            {
                return _pendingSnapshots.Values.Sum(entry => entry.OwnerCount);
            }
        }
    }

    public void EnsurePlanCapacity(CatalogIndexBuildResult target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_snapshotGate)
        {
            EnsurePlanCapacityLocked([target]);
        }
    }

    public CatalogDownloadLease CreateFullLease(CatalogIndexBuildResult target)
    {
        lock (_snapshotGate)
        {
            EnsurePlanCapacityLocked([target]);
            return _downloadLeases.CreateFull(target);
        }
    }

    public CatalogDownloadLease CreateDeltaLease(
        CatalogIndexBuildResult baseline,
        CatalogIndexBuildResult target,
        IReadOnlyList<CatalogDeltaOperation> operations)
    {
        lock (_snapshotGate)
        {
            EnsurePlanCapacityLocked([baseline, target]);
            return _downloadLeases.CreateDelta(baseline, target, operations);
        }
    }

    private void EnsurePlanCapacityLocked(IReadOnlyList<CatalogIndexBuildResult> protectedArtifacts)
    {
        PruneExpiredSnapshots(_timeProvider.GetUtcNow());
        if (!TryPrepareAdmissionLocked(protectedArtifacts))
        {
            throw new CatalogCapacityBusyException("Catalog snapshot capacity is busy.");
        }
    }

    public Task<CatalogIndexBuildResult?> GetOrBuildAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken)
    {
        return GetOrBuildCoreAsync(
            storeCode,
            since,
            buildAsync,
            allowStale: true,
            cancellationToken);
    }

    public Task<CatalogIndexBuildResult?> GetOrBuildFreshAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken)
    {
        return GetOrBuildCoreAsync(
            storeCode,
            since,
            buildAsync,
            allowStale: false,
            cancellationToken);
    }

    public async Task<CatalogIndexBuildResult?> ForceRefreshAndPublishAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentNullException.ThrowIfNull(buildAsync);

        var key = new CatalogIndexCacheKey(NormalizeStoreCode(storeCode), Since: null);
        while (true)
        {
            if (_entries.TryGetValue(key, out var existing) && IsBuildRunning(existing))
            {
                var sharedResult = await AwaitBuildAsync(existing, cancellationToken);
                await AwaitPublicationForCallerAsync(existing, requirePublished: true, cancellationToken);
                if (sharedResult is null)
                {
                    throw new InvalidDataException($"门店 {key.StoreCode} 目录构建没有返回可发布结果。");
                }

                return ProjectLegacySince(existing, sharedResult, since);
            }

            var staleResult = GetCompletedResult(existing) ?? GetLatestSnapshot(key, _timeProvider.GetUtcNow());
            var refreshEntry = CreateBuildingEntry(key, staleResult, buildAsync);
            var installed = existing is null
                ? _entries.TryAdd(key, refreshEntry)
                : _entries.TryUpdate(key, refreshEntry, existing);
            if (!installed)
            {
                continue;
            }

            PublishWhenCompleted(key, refreshEntry);
            var built = await AwaitBuildAsync(refreshEntry, cancellationToken);
            await AwaitPublicationForCallerAsync(refreshEntry, requirePublished: true, cancellationToken);
            if (built is null)
            {
                throw new InvalidDataException($"门店 {key.StoreCode} 目录构建没有返回可发布结果。");
            }

            return ProjectLegacySince(refreshEntry, built, since);
        }
    }

    private async Task<CatalogIndexBuildResult?> GetOrBuildCoreAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        bool allowStale,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentNullException.ThrowIfNull(buildAsync);

        // 目录实体按门店构建；旧 since 仅从同一完整工件派生，不能再触发第二次数据库构建。
        var key = new CatalogIndexCacheKey(NormalizeStoreCode(storeCode), Since: null);
        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            if (_entries.TryGetValue(key, out var existing) &&
                (existing.ExpiresAt > now || IsBuildRunning(existing)))
            {
                if (allowStale && IsBuildRunning(existing) && existing.StaleResult is not null &&
                    (since is null || !RequiresRawRebuild(existing.StaleResult)))
                {
                    // 刷新期间始终返回最后一次完整快照，避免一批 iPad 请求再次阻塞在同一构建上。
                    Log($"cache stale store={key.StoreCode} since={FormatSince(key.Since)}");
                    return ProjectLegacySince(existing, existing.StaleResult, since);
                }

                var cachedResult = await AwaitBuildAsync(existing, cancellationToken);
                await AwaitPublicationForCallerAsync(existing, requirePublished: !allowStale, cancellationToken);
                if (since is not null &&
                    cachedResult is { RawPriceIndexInput: null } &&
                    RequiresRawRebuild(cachedResult))
                {
                    // raw LRU 淘汰后 legacy since 必须重建同店完整候选，不能对最终 winner 过滤。
                    _entries.TryRemove(new KeyValuePair<CatalogIndexCacheKey, CacheEntry>(key, existing));
                    continue;
                }

                Log(IsBuildRunning(existing)
                    ? $"cache build wait store={key.StoreCode} since={FormatSince(key.Since)}"
                    : $"cache hit store={key.StoreCode} since={FormatSince(key.Since)}");
                return ProjectLegacySince(
                    existing,
                    cachedResult,
                    since);
            }

            var staleResult = GetLatestSnapshot(key, now);
            if (allowStale &&
                staleResult is not null &&
                _backgroundRefreshScheduler is not null &&
                (since is null || !RequiresRawRebuild(staleResult)))
            {
                _backgroundRefreshScheduler.QueueRefresh(key.StoreCode);
                Log($"cache stale refresh queued store={key.StoreCode} since={FormatSince(key.Since)}");
                return ProjectLegacySince(existing, staleResult, since);
            }

            var newEntry = CreateBuildingEntry(key, staleResult, buildAsync);

            if (existing is null)
            {
                if (_entries.TryAdd(key, newEntry))
                {
                    Log($"cache miss store={key.StoreCode} since={FormatSince(key.Since)} ttlSeconds={_ttl.TotalSeconds:0}");
                    PublishWhenCompleted(key, newEntry);
                    var built = await AwaitBuildAsync(newEntry, cancellationToken);
                    await AwaitPublicationForCallerAsync(newEntry, requirePublished: !allowStale, cancellationToken);
                    return ProjectLegacySince(newEntry, built, since);
                }

                continue;
            }

            if (_entries.TryUpdate(key, newEntry, existing))
            {
                Log($"cache expired store={key.StoreCode} since={FormatSince(key.Since)} ttlSeconds={_ttl.TotalSeconds:0}");
                PublishWhenCompleted(key, newEntry);
                var built = await AwaitBuildAsync(newEntry, cancellationToken);
                await AwaitPublicationForCallerAsync(newEntry, requirePublished: !allowStale, cancellationToken);
                return ProjectLegacySince(newEntry, built, since);
            }
        }
    }

    public CatalogIndexBuildResult? GetByVersion(
        string storeCode,
        DateTimeOffset? since,
        string catalogVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogVersion);

        var key = new CatalogSnapshotCacheKey(
            NormalizeStoreCode(storeCode),
            Since: null,
            catalogVersion.Trim());
        lock (_snapshotGate)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpiredSnapshots(now);
            if (_snapshots.TryGetValue(key, out var snapshot))
            {
                Log($"snapshot hit store={key.StoreCode} since={FormatSince(key.Since)}");
                return ProjectLegacySince(entry: null, snapshot.Result, since);
            }

            if (_lazySnapshotDescriptors.TryGetValue(key, out var descriptor))
            {
                try
                {
                    var loaded = _snapshotStore?.Load(descriptor.StoreCode, descriptor.Since, descriptor.CatalogVersion);
                    if (loaded is null)
                    {
                        _lazySnapshotDescriptors.Remove(key);
                        Log($"snapshot lazy body missing store={key.StoreCode}");
                        return null;
                    }

                    var result = new CatalogIndexBuildResult(key.StoreCode, loaded.GeneratedAt, loaded.SellableItems,
                        new CatalogSellableIndex(key.StoreCode, loaded.GeneratedAt, loaded.SellableItems, key.CatalogVersion));
                    if (TryAdmitLoadedSnapshotLocked(key, result, loaded.ExpiresAt))
                    {
                        _lazySnapshotDescriptors.Remove(key);
                        return ProjectLegacySince(entry: null, result, since);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    _lazySnapshotDescriptors.Remove(key);
                    Log($"snapshot lazy load failed store={key.StoreCode} error={exception.GetType().Name}");
                }
            }
        }

        Log($"snapshot miss store={key.StoreCode} since={FormatSince(key.Since)}");
        return null;
    }

    public void InvalidateStore(string storeCode)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        foreach (var key in _entries.Keys.Where(key =>
            string.Equals(key.StoreCode, normalizedStoreCode, StringComparison.OrdinalIgnoreCase)))
        {
            _entries.TryRemove(key, out _);
        }

        Log($"cache invalidated store={normalizedStoreCode}");
    }

    private CacheEntry CreateBuildingEntry(
        CatalogIndexCacheKey key,
        CatalogIndexBuildResult? staleResult,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync)
    {
        var sharedBuildCancellationToken = _applicationLifetime?.ApplicationStopping ?? CancellationToken.None;
        return new CacheEntry(
            // 构建及发布完成前使用永不过期占位，避免长构建刚完成就被判定过期。
            DateTimeOffset.MaxValue,
            new Lazy<Task<CatalogIndexBuildResult?>>(
                // 任一 HTTP waiter 取消都不能中断共享查询，只有宿主停止可以取消。
                () => BuildStableResultAsync(
                    staleResult,
                    key.StoreCode,
                    buildAsync,
                    sharedBuildCancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication),
            CreatePublication(),
            staleResult,
            new CatalogLegacySinceCache());
    }

    private static CatalogIndexBuildResult? GetCompletedResult(CacheEntry? entry)
    {
        if (entry is null ||
            !entry.PublicationCompletion.Task.IsCompletedSuccessfully ||
            !entry.BuildTask.IsValueCreated ||
            !entry.BuildTask.Value.IsCompletedSuccessfully)
        {
            return null;
        }

        return entry.BuildTask.Value.Result;
    }

    private static async Task<CatalogIndexBuildResult?> AwaitBuildAsync(
        CacheEntry entry,
        CancellationToken cancellationToken)
    {
        // 任一 HTTP 调用者均可取消自己的等待；共享构建只受宿主停止令牌控制。
        try
        {
            return await entry.BuildTask.Value.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 构建故障返回调用方前先等发布观察器移除失败 flight，下一次调用可立即重建。
            try
            {
                await entry.PublicationCompletion.Task;
            }
            catch
            {
                // 调用方继续收到原始构建异常。
            }

            throw;
        }
    }

    private static async Task AwaitPublicationForCallerAsync(
        CacheEntry entry,
        bool requirePublished,
        CancellationToken cancellationToken)
    {
        var outcome = await entry.PublicationCompletion.Task.WaitAsync(cancellationToken);
        switch (outcome.Status)
        {
            case PublicationStatus.Published:
            case PublicationStatus.Empty:
                return;
            case PublicationStatus.CapacityRejected when !requirePublished:
            case PublicationStatus.PersistenceFailed when !requirePublished:
                return;
            case PublicationStatus.CapacityRejected:
                throw new CatalogCapacityBusyException("Catalog snapshot capacity is busy.");
            case PublicationStatus.PersistenceFailed:
                throw outcome.Error ?? new IOException("Catalog snapshot persistence failed.");
            case PublicationStatus.Invalidated:
                throw new InvalidOperationException("目录构建在发布完成前已失效。");
            default:
                throw new InvalidOperationException("未知的目录发布状态。");
        }
    }

    private void PublishWhenCompleted(CatalogIndexCacheKey key, CacheEntry entry)
    {
        // 发布与 pin 不能依赖第一个 HTTP 等待者：它可能已经断开或被取消。
        _ = Task.Run(() => PublishCompletedEntryAsync(key, entry));
    }

    private async Task PublishCompletedEntryAsync(CatalogIndexCacheKey key, CacheEntry entry)
    {
        CatalogIndexBuildResult? cacheResult = null;
        try
        {
            var result = await entry.BuildTask.Value;
            if (result is null)
            {
                _entries.TryRemove(new KeyValuePair<CatalogIndexCacheKey, CacheEntry>(key, entry));
                entry.PublicationCompletion.TrySetResult(
                    new CatalogPublicationOutcome(PublicationStatus.Empty));
                return;
            }

            cacheResult = RegisterRawArtifact(result);
            if (!_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
            {
                // 构建期间已失效的 owner 仍可拿到本次结果，但不得写入持久发布点。
                lock (_snapshotGate)
                {
                    RemoveRawArtifact(CreateSnapshotKey(cacheResult));
                }

                entry.PublicationCompletion.TrySetResult(
                    new CatalogPublicationOutcome(PublicationStatus.Invalidated));
                return;
            }

            if (!PinCompletedSnapshot(key, cacheResult, entry.StaleResult))
            {
                RestoreStaleOrRemove(key, entry);
                entry.PublicationCompletion.TrySetResult(
                    new CatalogPublicationOutcome(PublicationStatus.CapacityRejected));
                return;
            }

            var completedEntry = entry with
            {
                ExpiresAt = CalculateActiveExpiry(_timeProvider.GetUtcNow(), cacheResult.SourceValidUntil),
                BuildTask = new Lazy<Task<CatalogIndexBuildResult?>>(
                    () => Task.FromResult<CatalogIndexBuildResult?>(cacheResult),
                    LazyThreadSafetyMode.ExecutionAndPublication)
            };
            if (_entries.TryUpdate(key, completedEntry, entry))
            {
                entry.PublicationCompletion.TrySetResult(
                    new CatalogPublicationOutcome(PublicationStatus.Published));
            }
            else
            {
                // 版本正文和 pinned snapshot 已完成发布；active CAS 失效只阻止其成为当前版本。
                entry.PublicationCompletion.TrySetResult(
                    new CatalogPublicationOutcome(PublicationStatus.Published));
            }
        }
        catch (CatalogSnapshotPersistenceException exception)
        {
            RestoreStaleOrRemove(key, entry);
            entry.PublicationCompletion.TrySetResult(
                new CatalogPublicationOutcome(
                    PublicationStatus.PersistenceFailed,
                    exception.InnerException ?? exception));
        }
        catch (Exception exception)
        {
            RestoreStaleOrRemove(key, entry);

            entry.PublicationCompletion.TrySetException(exception);
        }
    }

    private void RestoreStaleOrRemove(CatalogIndexCacheKey key, CacheEntry entry)
    {
        if (entry.StaleResult is null)
        {
            _entries.TryRemove(new KeyValuePair<CatalogIndexCacheKey, CacheEntry>(key, entry));
            return;
        }

        _entries.TryUpdate(
            key,
            new CacheEntry(
                _timeProvider.GetUtcNow(),
                new Lazy<Task<CatalogIndexBuildResult?>>(
                    () => Task.FromResult<CatalogIndexBuildResult?>(entry.StaleResult),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                CreateCompletedPublication(),
                entry.StaleResult,
                new CatalogLegacySinceCache()),
            entry);
    }

    private async Task<CatalogIndexBuildResult?> BuildStableResultAsync(
        CatalogIndexBuildResult? staleResult,
        string storeCode,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken sharedBuildCancellationToken)
    {
        var result = _scopeFactory is null
            ? await buildAsync(sharedBuildCancellationToken)
            : await BuildInOwnedScopeAsync(storeCode, sharedBuildCancellationToken);
        return result is null
            ? null
            : ReuseCatalogVersionWhenContentIsUnchanged(staleResult, result);
    }

    private async Task<CatalogIndexBuildResult?> BuildInOwnedScopeAsync(
        string storeCode,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory!.CreateAsyncScope();
        var loader = scope.ServiceProvider.GetRequiredService<CatalogService>();
        return await loader.BuildCatalogArtifactAsync(storeCode, cancellationToken);
    }

    private static CatalogIndexBuildResult ReuseCatalogVersionWhenContentIsUnchanged(
        CatalogIndexBuildResult? previous,
        CatalogIndexBuildResult current)
    {
        if (previous is null ||
            !string.Equals(previous.StoreCode, current.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            previous.CatalogIndex.Items.Count != current.CatalogIndex.Items.Count ||
            !previous.CatalogIndex.Items.SequenceEqual(current.CatalogIndex.Items))
        {
            return current;
        }

        var previousVersion = previous.CatalogIndex.CatalogVersion;
        if (string.IsNullOrWhiteSpace(previousVersion) ||
            string.Equals(previousVersion, current.CatalogIndex.CatalogVersion, StringComparison.Ordinal))
        {
            return current;
        }

        // 内容没有变化时沿用已发布版本，客户端可直接 noChange，避免重复全量克隆。
        return current with
        {
            CatalogIndex = new CatalogSellableIndex(
                current.StoreCode,
                current.GeneratedAt,
                current.SellableItems,
                previousVersion)
        };
    }

    private CatalogIndexBuildResult? ProjectLegacySince(
        CacheEntry? entry,
        CatalogIndexBuildResult? artifact,
        DateTimeOffset? since)
    {
        if (artifact is null || since is null)
        {
            return artifact;
        }

        var rawInput = artifact.RawPriceIndexInput ?? GetRawArtifact(artifact);
        if (rawInput is null)
        {
            // 恢复的磁盘快照仅含最终 sellable 行；若再过滤 winner 会改变旧 since 的决胜语义。
            if (_snapshotStore is not null)
            {
                throw new CatalogSnapshotExpiredException(artifact.StoreCode, artifact.CatalogIndex.CatalogVersion);
            }

            // 无持久化测试/纯内存调用方提供的简化工件从来没有原始候选；保留旧兼容行为。
            return entry?.LegacySinceCache?.GetOrAdd(artifact, since.Value)
                ?? CatalogLegacySinceCache.CreateLegacyFallback(artifact, since.Value);
        }

        return entry?.LegacySinceCache?.GetOrAdd(artifact, rawInput, since.Value)
            ?? CatalogLegacySinceCache.Create(artifact, rawInput, since.Value);
    }

    private DateTimeOffset CalculateActiveExpiry(
        DateTimeOffset completedAt,
        DateTimeOffset? sourceValidUntil)
    {
        var cacheExpiry = completedAt.Add(_ttl);
        return sourceValidUntil is { } validUntil && validUntil < cacheExpiry
            ? validUntil
            : cacheExpiry;
    }

    private bool PinCompletedSnapshot(
        CatalogIndexCacheKey activeKey,
        CatalogIndexBuildResult result,
        CatalogIndexBuildResult? previousActive)
    {
        var catalogVersion = result.CatalogIndex.CatalogVersion;
        if (string.IsNullOrWhiteSpace(catalogVersion))
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow();
        var snapshotKey = new CatalogSnapshotCacheKey(
            activeKey.StoreCode,
            activeKey.Since,
            catalogVersion.Trim());
        var shouldPersist = false;
        var shouldRefreshPersistedExpiration = false;
        var expiresAt = now.Add(_snapshotTtl);
        PendingSnapshotReservation reservation;
        lock (_snapshotGate)
        {
            PruneExpiredSnapshots(now);
            var protectedArtifacts = previousActive is null
                ? new[] { result }
                : new[] { previousActive, result };
            if (!TryPrepareAdmissionLocked(protectedArtifacts))
            {
                if (!_pendingSnapshots.ContainsKey(snapshotKey))
                {
                    RemoveRawArtifact(snapshotKey);
                }

                return false;
            }

            shouldRefreshPersistedExpiration = _snapshots.ContainsKey(snapshotKey);
            shouldPersist = !shouldRefreshPersistedExpiration;
            // pending 只参与容量 reservation；读取路径在 durable publish 前看不到新版本。
            reservation = ReservePendingSnapshotLocked(snapshotKey, result);
        }

        try
        {
            if (_snapshotStore is not null && (shouldPersist || shouldRefreshPersistedExpiration))
            {
                if (shouldPersist)
                {
                    _snapshotStore.Save(new CatalogPersistedSnapshot(
                        activeKey.StoreCode,
                        activeKey.Since,
                        result.GeneratedAt,
                        expiresAt,
                        catalogVersion.Trim(),
                        result.SellableItems));
                }
                else
                {
                    _snapshotStore.RefreshExpiration(
                        activeKey.StoreCode,
                        activeKey.Since,
                        catalogVersion.Trim(),
                        expiresAt);
                }
            }

            lock (_snapshotGate)
            {
                ReleasePendingSnapshotLocked(reservation);
                _snapshots[snapshotKey] = new SnapshotEntry(
                    result,
                    expiresAt,
                    Interlocked.Increment(ref _snapshotSequence));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log($"snapshot persist failed store={activeKey.StoreCode} error={exception.GetType().Name}");
            lock (_snapshotGate)
            {
                var wasLastOwner = ReleasePendingSnapshotLocked(reservation);
                if (shouldPersist && wasLastOwner && !_snapshots.ContainsKey(snapshotKey))
                {
                    RemoveRawArtifact(snapshotKey);
                }
            }

            throw new CatalogSnapshotPersistenceException(exception);
        }

        return true;
    }

    private CatalogIndexBuildResult? GetLatestSnapshot(CatalogIndexCacheKey key, DateTimeOffset now)
    {
        lock (_snapshotGate)
        {
            PruneExpiredSnapshots(now);
            var snapshot = _snapshots
                .Where(pair => string.Equals(pair.Key.StoreCode, key.StoreCode, StringComparison.OrdinalIgnoreCase)
                    && pair.Key.Since == key.Since)
                .OrderByDescending(pair => pair.Value.Sequence)
                .Select(pair => pair.Value.Result)
                .FirstOrDefault();
            if (snapshot is not null)
            {
                return snapshot;
            }

            var descriptors = _lazySnapshotDescriptors
                .Where(pair => string.Equals(pair.Key.StoreCode, key.StoreCode, StringComparison.OrdinalIgnoreCase)
                    && pair.Key.Since == key.Since)
                .OrderByDescending(pair => pair.Value.GeneratedAt)
                .Select(pair => (pair.Key, pair.Value))
                .ToArray();
            foreach (var (descriptorKey, descriptor) in descriptors)
            {
                // 损坏或被并发删除的最新持久化版本不能遮蔽仍可用的次新版本。
                try
                {
                    var loaded = _snapshotStore?.Load(descriptor.StoreCode, descriptor.Since, descriptor.CatalogVersion);
                    if (loaded is null)
                    {
                        _lazySnapshotDescriptors.Remove(descriptorKey);
                        continue;
                    }

                    var result = new CatalogIndexBuildResult(key.StoreCode, loaded.GeneratedAt, loaded.SellableItems,
                        new CatalogSellableIndex(key.StoreCode, loaded.GeneratedAt, loaded.SellableItems, descriptor.CatalogVersion));
                    if (TryAdmitLoadedSnapshotLocked(descriptorKey, result, loaded.ExpiresAt))
                    {
                        _lazySnapshotDescriptors.Remove(descriptorKey);
                        return result;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                {
                    _lazySnapshotDescriptors.Remove(descriptorKey);
                    Log($"snapshot lazy load failed store={key.StoreCode} error={exception.GetType().Name}");
                }
            }

            return null;
        }
    }

    private bool TryAdmitLoadedSnapshotLocked(
        CatalogSnapshotCacheKey snapshotKey,
        CatalogIndexBuildResult result,
        DateTimeOffset expiresAt)
    {
        if (!TryPrepareAdmissionLocked([result]))
        {
            return false;
        }

        _snapshots[snapshotKey] = new SnapshotEntry(
            result,
            expiresAt,
            Interlocked.Increment(ref _snapshotSequence));
        return true;
    }

    private void RestorePersistedSnapshots()
    {
        if (_snapshotStore is null)
        {
            return;
        }

        try
        {
            var now = _timeProvider.GetUtcNow();
            var restored = _snapshotStore.LoadDescriptors(now);
            lock (_snapshotGate)
            {
                foreach (var snapshot in restored
                             .OrderBy(snapshot => snapshot.GeneratedAt)
                             .ThenBy(snapshot => snapshot.CatalogVersion, StringComparer.Ordinal))
                {
                    var key = new CatalogSnapshotCacheKey(
                        NormalizeStoreCode(snapshot.StoreCode),
                        snapshot.Since,
                        snapshot.CatalogVersion.Trim());
                    if (_snapshots.ContainsKey(key) || _lazySnapshotDescriptors.ContainsKey(key))
                    {
                        continue;
                    }
                    // 启动只登记 manifest 描述，不解压正文；首次按门店/版本真正需要时再 lazy load。
                    _lazySnapshotDescriptors.Add(key, snapshot);
                }

                PruneExpiredSnapshots(now);
            }

            Log($"snapshot restore count={restored.Count}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            // 启动恢复失败只能回退为冷构建，不能阻止 API 进程启动。
            Log($"snapshot restore failed error={exception.GetType().Name}");
        }
    }

    private void PruneExpiredSnapshots(DateTimeOffset now)
    {
        // ExpiresAt 仅表示建议刷新时间。最后一次成功版本必须持续可用，
        // 直到容量/每店版本上限淘汰或新的已校验版本完成原子发布。
    }

    private CatalogIndexBuildResult RegisterRawArtifact(CatalogIndexBuildResult result)
    {
        if (result.RawPriceIndexInput is null)
        {
            return result;
        }

        var key = new CatalogSnapshotCacheKey(
            NormalizeStoreCode(result.StoreCode),
            Since: null,
            result.CatalogIndex.CatalogVersion);
        lock (_snapshotGate)
        {
            if (_rawArtifacts.Remove(key, out var existing))
            {
                _rawArtifactLru.Remove(existing.Node);
            }

            var node = _rawArtifactLru.AddFirst(key);
            _rawArtifacts.Add(key, new RawArtifactEntry(result.RawPriceIndexInput, node));
            _rawArtifactVersions.Add(key);
            while (_rawArtifacts.Count > RawArtifactCapacity)
            {
                var leastRecent = _rawArtifactLru.Last!;
                _rawArtifacts.Remove(leastRecent.Value);
                _rawArtifactLru.RemoveLast();
            }
        }

        // 固定版本与磁盘持久化不保留原始候选，避免每个版本复制大表。
        return result with { RawPriceIndexInput = null };
    }

    private PriceIndexInput? GetRawArtifact(CatalogIndexBuildResult artifact)
    {
        var key = new CatalogSnapshotCacheKey(
            NormalizeStoreCode(artifact.StoreCode),
            Since: null,
            artifact.CatalogIndex.CatalogVersion);
        lock (_snapshotGate)
        {
            if (!_rawArtifacts.TryGetValue(key, out var entry))
            {
                return null;
            }

            _rawArtifactLru.Remove(entry.Node);
            _rawArtifactLru.AddFirst(entry.Node);
            return entry.Input;
        }
    }

    private bool HasRawArtifact(CatalogIndexBuildResult artifact)
    {
        var key = CreateSnapshotKey(artifact);
        lock (_snapshotGate)
        {
            return _rawArtifacts.ContainsKey(key);
        }
    }

    private bool RequiresRawRebuild(CatalogIndexBuildResult artifact)
    {
        var key = CreateSnapshotKey(artifact);
        lock (_snapshotGate)
        {
            // 仅真实原始工件被 LRU 淘汰、或持久化恢复后才重建；测试/旧调用方的简化结果保持兼容。
            return !_rawArtifacts.ContainsKey(key) && (_rawArtifactVersions.Contains(key) || _snapshotStore is not null);
        }
    }

    private void RemoveRawArtifact(CatalogSnapshotCacheKey key)
    {
        if (_rawArtifacts.Remove(key, out var entry))
        {
            _rawArtifactLru.Remove(entry.Node);
        }

        // 此路径仅用于版本永久淘汰（TTL/LRU version cap/容量），不是 raw LRU 的临时逐出。
        _rawArtifactVersions.Remove(key);
    }

    private static CatalogSnapshotCacheKey CreateSnapshotKey(CatalogIndexBuildResult result)
    {
        return new CatalogSnapshotCacheKey(
            NormalizeStoreCode(result.StoreCode),
            Since: null,
            result.CatalogIndex.CatalogVersion);
    }

    private bool TryPrepareAdmissionLocked(
        IReadOnlyList<CatalogIndexBuildResult> protectedArtifacts)
    {
        var nonEvictable = new Dictionary<CatalogSnapshotCacheKey, CatalogIndexBuildResult>();
        foreach (var leased in _downloadLeases.GetLeasedArtifacts())
        {
            AddCapacityArtifact(nonEvictable, CreateSnapshotKey(leased), leased);
        }
        foreach (var (key, pending) in _pendingSnapshots)
        {
            AddCapacityArtifact(nonEvictable, key, pending.CapacityArtifact);
        }
        foreach (var artifact in protectedArtifacts)
        {
            AddCapacityArtifact(nonEvictable, CreateSnapshotKey(artifact), artifact);
        }

        // 不可淘汰集合自身已经超限时必须先拒绝，不能为了必然失败的请求清空旧版本。
        if (CountItems(nonEvictable) > _hardItemCapacity ||
            nonEvictable.Keys
                .GroupBy(key => key.StoreCode, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > _maxSnapshotsPerStore))
        {
            return false;
        }

        var resident = new Dictionary<CatalogSnapshotCacheKey, CatalogIndexBuildResult>();
        foreach (var (key, snapshot) in _snapshots)
        {
            AddCapacityArtifact(resident, key, snapshot.Result);
        }
        foreach (var (key, artifact) in nonEvictable)
        {
            AddCapacityArtifact(resident, key, artifact);
        }

        var evictionOrder = _snapshots
            .Where(pair => !nonEvictable.ContainsKey(pair.Key))
            .Where(pair => !_downloadLeases.IsVersionLeased(pair.Key.StoreCode, pair.Key.CatalogVersion))
            .OrderBy(pair => pair.Value.Sequence)
            .Select(pair => pair.Key)
            .ToArray();
        var selected = new HashSet<CatalogSnapshotCacheKey>();

        foreach (var storeCode in resident.Keys
                     .Select(key => key.StoreCode)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .ToArray())
        {
            while (resident.Keys.Count(key =>
                       string.Equals(key.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase)) >
                   _maxSnapshotsPerStore)
            {
                var candidate = evictionOrder.FirstOrDefault(key =>
                    !selected.Contains(key) &&
                    string.Equals(key.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase));
                if (candidate is null)
                {
                    return false;
                }

                selected.Add(candidate);
                resident.Remove(candidate);
            }
        }

        foreach (var candidate in evictionOrder)
        {
            if (CountItems(resident) <= _softItemCapacity)
            {
                break;
            }

            if (selected.Add(candidate))
            {
                resident.Remove(candidate);
            }
        }

        if (CountItems(resident) > _hardItemCapacity ||
            resident.Keys
                .GroupBy(key => key.StoreCode, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > _maxSnapshotsPerStore))
        {
            return false;
        }

        foreach (var key in selected)
        {
            RemoveSnapshotLocked(key);
        }

        return true;
    }

    private static long CountItems(
        IReadOnlyDictionary<CatalogSnapshotCacheKey, CatalogIndexBuildResult> artifacts)
    {
        return artifacts.Values.Sum(result => (long)result.CatalogIndex.Items.Count);
    }

    private static void AddCapacityArtifact(
        IDictionary<CatalogSnapshotCacheKey, CatalogIndexBuildResult> artifacts,
        CatalogSnapshotCacheKey key,
        CatalogIndexBuildResult candidate)
    {
        if (!artifacts.TryGetValue(key, out var existing) ||
            candidate.CatalogIndex.Items.Count > existing.CatalogIndex.Items.Count)
        {
            artifacts[key] = candidate;
        }
    }

    private PendingSnapshotReservation ReservePendingSnapshotLocked(
        CatalogSnapshotCacheKey key,
        CatalogIndexBuildResult result)
    {
        if (!_pendingSnapshots.TryGetValue(key, out var pending))
        {
            pending = new PendingSnapshotEntry();
            _pendingSnapshots.Add(key, pending);
        }

        var ownerToken = Guid.NewGuid();
        pending.Add(ownerToken, result);
        return new PendingSnapshotReservation(key, ownerToken);
    }

    private bool ReleasePendingSnapshotLocked(PendingSnapshotReservation reservation)
    {
        if (!_pendingSnapshots.TryGetValue(reservation.Key, out var pending) ||
            !pending.Remove(reservation.OwnerToken))
        {
            return false;
        }

        if (pending.OwnerCount > 0)
        {
            return false;
        }

        _pendingSnapshots.Remove(reservation.Key);
        return true;
    }

    private void RemoveSnapshotLocked(CatalogSnapshotCacheKey key)
    {
        _snapshots.Remove(key);
        RemoveRawArtifact(key);
        RemoveActiveReferences(key);
    }

    private void RemoveActiveReferences(CatalogSnapshotCacheKey evictedKey)
    {
        foreach (var (cacheKey, entry) in _entries)
        {
            if (!string.Equals(cacheKey.StoreCode, evictedKey.StoreCode, StringComparison.OrdinalIgnoreCase) ||
                !entry.BuildTask.IsValueCreated ||
                !entry.BuildTask.Value.IsCompletedSuccessfully)
            {
                continue;
            }

            if (string.Equals(entry.BuildTask.Value.Result?.CatalogIndex.CatalogVersion, evictedKey.CatalogVersion, StringComparison.Ordinal))
            {
                _entries.TryRemove(new KeyValuePair<CatalogIndexCacheKey, CacheEntry>(cacheKey, entry));
            }
        }
    }

    private static string NormalizeStoreCode(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static bool IsBuildRunning(CacheEntry entry)
    {
        return !entry.PublicationCompletion.Task.IsCompleted;
    }

    private static string FormatSince(DateTimeOffset? since)
    {
        return since?.ToString("O") ?? "<null>";
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[HBPOS][Api][CatalogIndexCache] {DateTimeOffset.Now:O} {message}");
    }

    private sealed record CacheEntry(
        DateTimeOffset ExpiresAt,
        Lazy<Task<CatalogIndexBuildResult?>> BuildTask,
        TaskCompletionSource<CatalogPublicationOutcome> PublicationCompletion,
        CatalogIndexBuildResult? StaleResult = null,
        CatalogLegacySinceCache? LegacySinceCache = null);

    private static TaskCompletionSource<CatalogPublicationOutcome> CreatePublication()
    {
        return new TaskCompletionSource<CatalogPublicationOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<CatalogPublicationOutcome> CreateCompletedPublication()
    {
        var publication = CreatePublication();
        publication.SetResult(new CatalogPublicationOutcome(PublicationStatus.Published));
        return publication;
    }

    private sealed record CatalogPublicationOutcome(
        PublicationStatus Status,
        Exception? Error = null);

    private enum PublicationStatus
    {
        Published,
        CapacityRejected,
        PersistenceFailed,
        Invalidated,
        Empty
    }

    private sealed class CatalogSnapshotPersistenceException(Exception innerException)
        : Exception("Catalog snapshot persistence failed.", innerException);

    /// <summary>
    /// 旧 since 接口只保留兼容用途。每个完整目录工件最多缓存八个派生视图，
    /// 防止不同调用方的时间戳把门店级完整构建重新拆回多个缓存键。
    /// </summary>
    private sealed class CatalogLegacySinceCache
    {
        private const int Capacity = 8;
        private readonly object _gate = new();
        private readonly Dictionary<long, LinkedListNode<LegacySinceEntry>> _entries = [];
        private readonly LinkedList<LegacySinceEntry> _lru = [];

        public CatalogIndexBuildResult GetOrAdd(CatalogIndexBuildResult artifact, DateTimeOffset since)
        {
            return GetOrAddCore(artifact, since, () => CreateLegacyFallback(artifact, since));
        }

        public CatalogIndexBuildResult GetOrAdd(CatalogIndexBuildResult artifact, PriceIndexInput rawInput, DateTimeOffset since)
        {
            return GetOrAddCore(artifact, since, () => Create(artifact, rawInput, since));
        }

        private CatalogIndexBuildResult GetOrAddCore(
            CatalogIndexBuildResult artifact,
            DateTimeOffset since,
            Func<CatalogIndexBuildResult> create)
        {
            var key = since.ToUniversalTime().Ticks;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var existing))
                {
                    _lru.Remove(existing);
                    _lru.AddFirst(existing);
                    return existing.Value.Result;
                }

                var result = create();
                var node = _lru.AddFirst(new LegacySinceEntry(key, result));
                _entries.Add(key, node);
                if (_entries.Count > Capacity)
                {
                    var leastRecent = _lru.Last!;
                    _entries.Remove(leastRecent.Value.SinceTicks);
                    _lru.RemoveLast();
                }

                return result;
            }
        }

        public static CatalogIndexBuildResult Create(CatalogIndexBuildResult artifact, PriceIndexInput rawInput, DateTimeOffset since)
        {
            var normalizedSince = since.ToUniversalTime();
            // legacy since 的合同是“组合各来源 candidate 后按 candidate UpdatedAt 过滤，
            // 再按 lookup 决胜”。不能对完整索引已决胜的结果二次过滤。
            var items = new PriceIndexBuilder().Build(
                artifact.StoreCode,
                rawInput with { Since = normalizedSince });
            // 同一完整工件和 since 的派生版本稳定；它不是新的数据库快照。
            var catalogVersion = string.Concat(
                artifact.CatalogIndex.CatalogVersion,
                ":legacy-since:",
                normalizedSince.Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture));
            return new CatalogIndexBuildResult(
                artifact.StoreCode,
                artifact.GeneratedAt,
                items,
                new CatalogSellableIndex(
                    artifact.StoreCode,
                    artifact.GeneratedAt,
                    items,
                    catalogVersion),
                artifact.SourceValidUntil);
        }

        public static CatalogIndexBuildResult CreateLegacyFallback(CatalogIndexBuildResult artifact, DateTimeOffset since)
        {
            var items = artifact.SellableItems
                .Where(item => item.UpdatedAt is null || item.UpdatedAt >= since.ToUniversalTime())
                .ToArray();
            return new CatalogIndexBuildResult(
                artifact.StoreCode,
                artifact.GeneratedAt,
                items,
                new CatalogSellableIndex(artifact.StoreCode, artifact.GeneratedAt, items,
                    string.Concat(artifact.CatalogIndex.CatalogVersion, ":legacy-since:", since.ToUniversalTime().Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture))),
                artifact.SourceValidUntil);
        }

        private sealed record LegacySinceEntry(long SinceTicks, CatalogIndexBuildResult Result);
    }

    private sealed record SnapshotEntry(
        CatalogIndexBuildResult Result,
        DateTimeOffset ExpiresAt,
        long Sequence);

    private sealed class PendingSnapshotEntry
    {
        private readonly Dictionary<Guid, CatalogIndexBuildResult> _owners = [];

        public int OwnerCount => _owners.Count;

        public CatalogIndexBuildResult CapacityArtifact =>
            _owners.Values.MaxBy(result => result.CatalogIndex.Items.Count)!;

        public void Add(Guid ownerToken, CatalogIndexBuildResult result)
        {
            _owners.Add(ownerToken, result);
        }

        public bool Remove(Guid ownerToken)
        {
            return _owners.Remove(ownerToken);
        }
    }

    private sealed record PendingSnapshotReservation(
        CatalogSnapshotCacheKey Key,
        Guid OwnerToken);

    private sealed record RawArtifactEntry(
        PriceIndexInput Input,
        LinkedListNode<CatalogSnapshotCacheKey> Node);

    private sealed record CatalogIndexCacheKey(
        string StoreCode,
        DateTimeOffset? Since);

    private sealed record CatalogSnapshotCacheKey(
        string StoreCode,
        DateTimeOffset? Since,
        string CatalogVersion)
    {
        public CatalogIndexCacheKey ToActiveKey()
        {
            return new CatalogIndexCacheKey(StoreCode, Since: null);
        }
    }
}
