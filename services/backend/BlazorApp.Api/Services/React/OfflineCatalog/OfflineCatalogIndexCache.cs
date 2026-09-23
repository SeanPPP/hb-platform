using System.Collections.Concurrent;

namespace BlazorApp.Api.Services.React.OfflineCatalog
{
    public sealed class OfflineCatalogCapacityBusyException : Exception
    {
        public OfflineCatalogCapacityBusyException()
            : base("Offline catalog capacity is busy.") { }
    }

    public sealed class OfflineCatalogSnapshotExpiredException : Exception
    {
        public OfflineCatalogSnapshotExpiredException(string storeCode, string catalogVersion)
            : base($"Offline catalog snapshot expired: store={storeCode} version={catalogVersion}")
        {
            StoreCode = storeCode;
            CatalogVersion = catalogVersion;
        }

        public string StoreCode { get; }

        public string CatalogVersion { get; }
    }

    /// <summary>下载租约：固定整次下载所用的目标版本（full）或 delta 操作数组。</summary>
    public sealed class OfflineCatalogDownloadLease
    {
        public required string LeaseId { get; init; }
        public required string StoreCode { get; init; }
        public required string Kind { get; init; }
        public required string TargetCatalogVersion { get; init; }
        public string? BaseCatalogVersion { get; init; }
        public IReadOnlyList<OfflineCatalogDeltaOperation>? Operations { get; init; }
        public DateTimeOffset LastTouchedAt { get; set; }
    }

    public interface IOfflineCatalogIndexCache
    {
        /// <summary>取当前版本；TTL 过期或不存在时用 buildAsync 构建并发布（并发调用共享同一构建）。</summary>
        Task<OfflineCatalogIndex?> GetOrBuildCurrentAsync(
            string storeCode,
            Func<CancellationToken, Task<OfflineCatalogIndex?>> buildAsync,
            CancellationToken cancellationToken);

        OfflineCatalogIndex? GetByVersion(string storeCode, string catalogVersion);

        OfflineCatalogDownloadLease? TryCreateFullLease(OfflineCatalogIndex target);

        OfflineCatalogDownloadLease? TryCreateDeltaLease(
            OfflineCatalogIndex baseline,
            OfflineCatalogIndex target,
            IReadOnlyList<OfflineCatalogDeltaOperation> operations);

        OfflineCatalogDownloadLease? GetAndTouchLease(string leaseId, string storeCode);

        int TotalRetainedItems { get; }
    }

    /// <summary>
    /// 进程内离线目录缓存（参考 Hbpos.Api 的 CatalogIndexCache 最小子集）：
    /// 每店当前版本 TTL 20 分钟；保留最近 3 个版本 2 小时供 delta 基线；
    /// 软容量超限时淘汰无活跃租约的旧版本和门店，硬容量超限时拒绝发布新版本。
    /// </summary>
    public sealed class OfflineCatalogIndexCache : IOfflineCatalogIndexCache
    {
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(20);
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(2);
        public static readonly TimeSpan DefaultLeaseIdleTimeout = TimeSpan.FromMinutes(30);
        public const int DefaultMaxVersionsPerStore = 3;
        public const int DefaultSoftItemCapacity = 1_500_000;
        public const int DefaultHardItemCapacity = 2_500_000;

        private sealed class StoreEntry
        {
            public readonly List<VersionEntry> Versions = new();
            public Task<OfflineCatalogIndex?>? Building;
            public DateTimeOffset LastAccessedAt;
        }

        private sealed class VersionEntry
        {
            public required OfflineCatalogIndex Index { get; init; }
            public required DateTimeOffset BuiltAt { get; init; }
            public DateTimeOffset LastAccessedAt { get; set; }
        }

        private readonly Dictionary<string, StoreEntry> _stores = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, OfflineCatalogDownloadLease> _leases = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private bool _hardCapacityBuildInProgress;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _ttl;
        private readonly TimeSpan _retention;
        private readonly TimeSpan _leaseIdleTimeout;
        private readonly int _maxVersionsPerStore;
        private readonly int _softItemCapacity;
        private readonly int _hardItemCapacity;

        public OfflineCatalogIndexCache()
            : this(TimeProvider.System, DefaultTtl, DefaultRetention, DefaultMaxVersionsPerStore, DefaultSoftItemCapacity, DefaultHardItemCapacity) { }

        public OfflineCatalogIndexCache(
            TimeProvider timeProvider,
            TimeSpan ttl,
            TimeSpan retention,
            int maxVersionsPerStore,
            int softItemCapacity,
            int hardItemCapacity,
            TimeSpan? leaseIdleTimeout = null)
        {
            _timeProvider = timeProvider;
            _ttl = ttl;
            _retention = retention;
            _maxVersionsPerStore = Math.Max(1, maxVersionsPerStore);
            _softItemCapacity = softItemCapacity;
            _hardItemCapacity = hardItemCapacity;
            _leaseIdleTimeout = leaseIdleTimeout ?? DefaultLeaseIdleTimeout;
        }

        public int TotalRetainedItems
        {
            get
            {
                lock (_gate)
                {
                    return _stores.Values.Sum(store => store.Versions.Sum(version => version.Index.Items.Count));
                }
            }
        }

        public async Task<OfflineCatalogIndex?> GetOrBuildCurrentAsync(
            string storeCode,
            Func<CancellationToken, Task<OfflineCatalogIndex?>> buildAsync,
            CancellationToken cancellationToken)
        {
            var normalized = storeCode.Trim();
            var now = _timeProvider.GetUtcNow();
            Task<OfflineCatalogIndex?> building;
            lock (_gate)
            {
                PruneLocked(now);
                var store = GetOrCreateStoreLocked(normalized, now);
                var current = store.Versions.LastOrDefault();
                if (current is not null && now - current.BuiltAt < _ttl)
                {
                    current.LastAccessedAt = now;
                    return current.Index;
                }

                if (store.Building is null)
                {
                    var atHardCapacity = TotalRetainedItemsLocked() >= _hardItemCapacity;
                    if (atHardCapacity &&
                        (_hardCapacityBuildInProgress || !HasEvictableVersionLocked(now)))
                    {
                        throw new OfflineCatalogCapacityBusyException();
                    }

                    if (atHardCapacity)
                    {
                        // 已满时只允许一店构建，避免多个昂贵构建竞争同一可淘汰版本。
                        _hardCapacityBuildInProgress = true;
                    }

                    // 构建在锁外执行；同店并发请求共享同一 Task，避免重复扫描 40 万行。
                    store.Building = BuildAndPublishAsync(normalized, buildAsync, atHardCapacity);
                }

                building = store.Building;
            }

            return await building.WaitAsync(cancellationToken);
        }

        private async Task<OfflineCatalogIndex?> BuildAndPublishAsync(
            string storeCode,
            Func<CancellationToken, Task<OfflineCatalogIndex?>> buildAsync,
            bool reservedHardCapacityBuild)
        {
            // 构建不绑定单个请求的取消令牌：等待方取消只影响自己，构建结果仍可供其他请求复用。
            await Task.Yield();
            try
            {
                var index = await buildAsync(CancellationToken.None);
                var now = _timeProvider.GetUtcNow();
                lock (_gate)
                {
                    var store = GetOrCreateStoreLocked(storeCode, now);
                    if (index is not null)
                    {
                        var previousVersionsByStore = _stores.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.Versions.ToList(),
                            StringComparer.OrdinalIgnoreCase);
                        store.Versions.Add(new VersionEntry { Index = index, BuiltAt = now, LastAccessedAt = now });
                        var pinned = GetPinnedVersionsLocked(now);
                        while (store.Versions.Count > _maxVersionsPerStore)
                        {
                            var removable = store.Versions.FindIndex(0, store.Versions.Count - 1,
                                version => !IsPinned(pinned, storeCode, version.Index.CatalogVersion));
                            if (removable < 0)
                            {
                                break;
                            }

                            store.Versions.RemoveAt(removable);
                        }

                        EvictForCapacityLocked(storeCode, _softItemCapacity, pinned);
                        if (TotalRetainedItemsLocked() > _hardItemCapacity)
                        {
                            // 发布失败时恢复所有门店；软容量淘汰不能影响已存在的目录。
                            foreach (var pair in previousVersionsByStore)
                            {
                                var versions = _stores[pair.Key].Versions;
                                versions.Clear();
                                versions.AddRange(pair.Value);
                            }
                            throw new OfflineCatalogCapacityBusyException();
                        }
                    }
                }

                return index;
            }
            finally
            {
                lock (_gate)
                {
                    if (reservedHardCapacityBuild)
                    {
                        _hardCapacityBuildInProgress = false;
                    }

                    if (_stores.TryGetValue(storeCode, out var store))
                    {
                        store.Building = null;
                    }
                }
            }
        }

        public OfflineCatalogIndex? GetByVersion(string storeCode, string catalogVersion)
        {
            var now = _timeProvider.GetUtcNow();
            lock (_gate)
            {
                PruneLocked(now);
                if (!_stores.TryGetValue(storeCode.Trim(), out var store))
                {
                    return null;
                }

                var entry = store.Versions.FirstOrDefault(version =>
                    string.Equals(version.Index.CatalogVersion, catalogVersion, StringComparison.Ordinal));
                if (entry is null)
                {
                    return null;
                }

                entry.LastAccessedAt = now;
                store.LastAccessedAt = now;
                return entry.Index;
            }
        }

        public OfflineCatalogDownloadLease? TryCreateFullLease(OfflineCatalogIndex target)
        {
            return RegisterLease(new OfflineCatalogDownloadLease
            {
                LeaseId = Guid.NewGuid().ToString("N"),
                StoreCode = target.StoreCode,
                Kind = "full",
                TargetCatalogVersion = target.CatalogVersion,
                LastTouchedAt = _timeProvider.GetUtcNow(),
            }, target);
        }

        public OfflineCatalogDownloadLease? TryCreateDeltaLease(
            OfflineCatalogIndex baseline,
            OfflineCatalogIndex target,
            IReadOnlyList<OfflineCatalogDeltaOperation> operations)
        {
            return RegisterLease(new OfflineCatalogDownloadLease
            {
                LeaseId = Guid.NewGuid().ToString("N"),
                StoreCode = target.StoreCode,
                Kind = "delta",
                TargetCatalogVersion = target.CatalogVersion,
                BaseCatalogVersion = baseline.CatalogVersion,
                Operations = operations,
                LastTouchedAt = _timeProvider.GetUtcNow(),
            }, baseline, target);
        }

        public OfflineCatalogDownloadLease? GetAndTouchLease(string leaseId, string storeCode)
        {
            lock (_gate)
            {
                PruneLeasesLocked(_timeProvider.GetUtcNow());
                if (!_leases.TryGetValue(leaseId, out var lease))
                {
                    return null;
                }

                if (!string.Equals(lease.StoreCode, storeCode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                lease.LastTouchedAt = _timeProvider.GetUtcNow();
                return lease;
            }
        }

        private OfflineCatalogDownloadLease? RegisterLease(
            OfflineCatalogDownloadLease lease,
            params OfflineCatalogIndex[] requiredIndexes)
        {
            lock (_gate)
            {
                PruneLeasesLocked(_timeProvider.GetUtcNow());
                if (!_stores.TryGetValue(lease.StoreCode, out var store) ||
                    requiredIndexes.Any(index => !store.Versions.Any(version => ReferenceEquals(version.Index, index))))
                {
                    return null;
                }

                _leases[lease.LeaseId] = lease;
                return lease;
            }
        }

        private void PruneLeasesLocked(DateTimeOffset now)
        {
            foreach (var pair in _leases)
            {
                if (now - pair.Value.LastTouchedAt > _leaseIdleTimeout)
                {
                    _leases.TryRemove(pair.Key, out _);
                }
            }
        }

        private StoreEntry GetOrCreateStoreLocked(string storeCode, DateTimeOffset now)
        {
            if (!_stores.TryGetValue(storeCode, out var store))
            {
                store = new StoreEntry();
                _stores[storeCode] = store;
            }

            store.LastAccessedAt = now;
            return store;
        }

        private int TotalRetainedItemsLocked()
        {
            return _stores.Values.Sum(store => store.Versions.Sum(version => version.Index.Items.Count));
        }

        private bool HasEvictableVersionLocked(DateTimeOffset now)
        {
            var pinned = GetPinnedVersionsLocked(now);
            return _stores.Any(pair => pair.Value.Versions.Any(version =>
                !IsPinned(pinned, pair.Key, version.Index.CatalogVersion)));
        }

        /// <summary>丢弃超过保留期的旧版本；当前版本（最后一个）即使过 TTL 也保留，直到新版本发布。</summary>
        private void PruneLocked(DateTimeOffset now)
        {
            var pinned = GetPinnedVersionsLocked(now);
            foreach (var pair in _stores)
            {
                var store = pair.Value;
                for (var i = 0; i < store.Versions.Count - 1;)
                {
                    var version = store.Versions[i];
                    if (now - version.BuiltAt > _retention &&
                        !IsPinned(pinned, pair.Key, version.Index.CatalogVersion))
                    {
                        store.Versions.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        private Dictionary<string, HashSet<string>> GetPinnedVersionsLocked(DateTimeOffset now)
        {
            PruneLeasesLocked(now);
            var pinned = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var lease in _leases.Values)
            {
                if (!pinned.TryGetValue(lease.StoreCode, out var versions))
                {
                    versions = new HashSet<string>(StringComparer.Ordinal);
                    pinned[lease.StoreCode] = versions;
                }

                versions.Add(lease.TargetCatalogVersion);
                if (lease.BaseCatalogVersion is not null)
                {
                    versions.Add(lease.BaseCatalogVersion);
                }
            }

            return pinned;
        }

        private static bool IsPinned(Dictionary<string, HashSet<string>> pinned, string storeCode, string catalogVersion) =>
            pinned.TryGetValue(storeCode, out var versions) && versions.Contains(catalogVersion);

        /// <summary>淘汰无活跃租约的旧版本及其他门店；活跃下载可暂时占用软容量，但不能越过硬容量。</summary>
        private void EvictForCapacityLocked(
            string protectedStoreCode,
            int targetItemCapacity,
            Dictionary<string, HashSet<string>> pinned)
        {
            while (TotalRetainedItemsLocked() > targetItemCapacity)
            {
                var candidate = _stores
                    .SelectMany(pair => pair.Value.Versions.Take(Math.Max(0, pair.Value.Versions.Count - 1))
                        .Where(version => !IsPinned(pinned, pair.Key, version.Index.CatalogVersion))
                        .Select(version => (Store: pair.Value, Version: version)))
                    .OrderBy(pair => pair.Version.LastAccessedAt)
                    .FirstOrDefault();
                if (candidate.Version is not null)
                {
                    candidate.Store.Versions.Remove(candidate.Version);
                    continue;
                }

                var staleStore = _stores
                    .Where(pair => !string.Equals(pair.Key, protectedStoreCode, StringComparison.OrdinalIgnoreCase) && pair.Value.Versions.Count > 0)
                    .Where(pair => pair.Value.Versions.All(version =>
                        !IsPinned(pinned, pair.Key, version.Index.CatalogVersion)))
                    .OrderBy(pair => pair.Value.LastAccessedAt)
                    .Select(pair => pair.Key)
                    .FirstOrDefault();
                if (staleStore is null)
                {
                    return;
                }

                _stores[staleStore].Versions.Clear();
            }
        }
    }
}
