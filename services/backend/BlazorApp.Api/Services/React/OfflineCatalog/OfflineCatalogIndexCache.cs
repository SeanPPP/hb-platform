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

        OfflineCatalogDownloadLease CreateFullLease(OfflineCatalogIndex target);

        OfflineCatalogDownloadLease CreateDeltaLease(
            OfflineCatalogIndex baseline,
            OfflineCatalogIndex target,
            IReadOnlyList<OfflineCatalogDeltaOperation> operations);

        OfflineCatalogDownloadLease? GetAndTouchLease(string leaseId, string storeCode);

        int TotalRetainedItems { get; }
    }

    /// <summary>
    /// 进程内离线目录缓存（参考 Hbpos.Api 的 CatalogIndexCache 最小子集）：
    /// 每店当前版本 TTL 20 分钟；保留最近 3 个版本 2 小时供 delta 基线；
    /// 软容量超限时按最久未用淘汰其他门店旧版本，硬容量超限时拒绝新构建。
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
                    if (TotalRetainedItemsLocked() >= _hardItemCapacity)
                    {
                        throw new OfflineCatalogCapacityBusyException();
                    }

                    // 构建在锁外执行；同店并发请求共享同一 Task，避免重复扫描 40 万行。
                    store.Building = BuildAndPublishAsync(normalized, buildAsync, cancellationToken);
                }

                building = store.Building;
            }

            return await building.WaitAsync(cancellationToken);
        }

        private async Task<OfflineCatalogIndex?> BuildAndPublishAsync(
            string storeCode,
            Func<CancellationToken, Task<OfflineCatalogIndex?>> buildAsync,
            CancellationToken cancellationToken)
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
                        store.Versions.Add(new VersionEntry { Index = index, BuiltAt = now, LastAccessedAt = now });
                        while (store.Versions.Count > _maxVersionsPerStore)
                        {
                            store.Versions.RemoveAt(0);
                        }

                        EvictForSoftCapacityLocked(storeCode);
                    }
                }

                return index;
            }
            finally
            {
                lock (_gate)
                {
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

        public OfflineCatalogDownloadLease CreateFullLease(OfflineCatalogIndex target)
        {
            return RegisterLease(new OfflineCatalogDownloadLease
            {
                LeaseId = Guid.NewGuid().ToString("N"),
                StoreCode = target.StoreCode,
                Kind = "full",
                TargetCatalogVersion = target.CatalogVersion,
                LastTouchedAt = _timeProvider.GetUtcNow(),
            });
        }

        public OfflineCatalogDownloadLease CreateDeltaLease(
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
            });
        }

        public OfflineCatalogDownloadLease? GetAndTouchLease(string leaseId, string storeCode)
        {
            PruneLeases();
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

        private OfflineCatalogDownloadLease RegisterLease(OfflineCatalogDownloadLease lease)
        {
            PruneLeases();
            _leases[lease.LeaseId] = lease;
            return lease;
        }

        private void PruneLeases()
        {
            var now = _timeProvider.GetUtcNow();
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

        /// <summary>丢弃超过保留期的旧版本；当前版本（最后一个）即使过 TTL 也保留，直到新版本发布。</summary>
        private void PruneLocked(DateTimeOffset now)
        {
            foreach (var store in _stores.Values)
            {
                while (store.Versions.Count > 1 && now - store.Versions[0].BuiltAt > _retention)
                {
                    store.Versions.RemoveAt(0);
                }
            }
        }

        /// <summary>超软容量时优先淘汰其他门店的旧版本（保留其当前版本），再淘汰最久未访问门店的当前版本。</summary>
        private void EvictForSoftCapacityLocked(string protectedStoreCode)
        {
            while (TotalRetainedItemsLocked() > _softItemCapacity)
            {
                var candidate = _stores
                    .Where(pair => pair.Value.Versions.Count > 1)
                    .OrderBy(pair => pair.Value.Versions[0].LastAccessedAt)
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    candidate.Versions.RemoveAt(0);
                    continue;
                }

                var staleStore = _stores
                    .Where(pair => !string.Equals(pair.Key, protectedStoreCode, StringComparison.OrdinalIgnoreCase) && pair.Value.Versions.Count > 0)
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
