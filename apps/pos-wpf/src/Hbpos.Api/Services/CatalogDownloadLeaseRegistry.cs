using System.Collections.Concurrent;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Api.Services;

public sealed class CatalogCapacityBusyException : Exception
{
    public CatalogCapacityBusyException(string message) : base(message) { }
}

/// <summary>
/// 下载期间固定同一对目录版本；租约独立于 HTTP 请求作用域，避免续页读到新发布版本。
/// </summary>
public sealed class CatalogDownloadLeaseRegistry
{
    private const int GlobalCapacity = 128;
    private const int StoreCapacity = 32;
    private static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AbsoluteTtl = TimeSpan.FromMinutes(30);
    private readonly object _gate = new();
    private readonly Dictionary<string, LeaseEntry> _leases = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public CatalogDownloadLeaseRegistry(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CatalogDownloadLease CreateFull(CatalogIndexBuildResult target)
    {
        return Create(target.StoreCode, baseResult: null, target, []);
    }

    public CatalogDownloadLease CreateDelta(
        CatalogIndexBuildResult baseline,
        CatalogIndexBuildResult target,
        IReadOnlyList<CatalogDeltaOperation> operations)
    {
        return Create(target.StoreCode, baseline, target, operations);
    }

    public CatalogDownloadLease GetAndTouch(
        string leaseId,
        string storeCode,
        string? baseCatalogVersion,
        string targetCatalogVersion)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpired(now);
            if (!_leases.TryGetValue(leaseId, out var entry) ||
                !string.Equals(entry.Lease.StoreCode, storeCode.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Lease.BaseCatalogVersion, baseCatalogVersion, StringComparison.Ordinal) ||
                !string.Equals(entry.Lease.Target.CatalogIndex.CatalogVersion, targetCatalogVersion, StringComparison.Ordinal))
            {
                throw new CatalogSnapshotExpiredException(storeCode, targetCatalogVersion);
            }

            return entry.Lease;
        }
    }

    public void Touch(
        string leaseId,
        string storeCode,
        string? baseCatalogVersion,
        string targetCatalogVersion)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpired(now);
            if (!_leases.TryGetValue(leaseId, out var entry) ||
                !string.Equals(entry.Lease.StoreCode, storeCode.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Lease.BaseCatalogVersion, baseCatalogVersion, StringComparison.Ordinal) ||
                !string.Equals(entry.Lease.Target.CatalogIndex.CatalogVersion, targetCatalogVersion, StringComparison.Ordinal))
            {
                throw new CatalogSnapshotExpiredException(storeCode, targetCatalogVersion);
            }

            // 只有页已成功计算并准备返回后才续租；无效请求与失败请求不能延长下载窗口。
            entry.LastAccessAt = now;
        }
    }

    public bool IsVersionLeased(string storeCode, string catalogVersion)
    {
        lock (_gate)
        {
            PruneExpired(_timeProvider.GetUtcNow());
            return _leases.Values.Any(entry =>
                string.Equals(entry.Lease.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(entry.Lease.Target.CatalogIndex.CatalogVersion, catalogVersion, StringComparison.Ordinal) ||
                 string.Equals(entry.Lease.Baseline?.CatalogIndex.CatalogVersion, catalogVersion, StringComparison.Ordinal)));
        }
    }

    public IReadOnlyList<CatalogIndexBuildResult> GetLeasedArtifacts()
    {
        lock (_gate)
        {
            PruneExpired(_timeProvider.GetUtcNow());
            return _leases.Values
                .SelectMany(entry => entry.Lease.Baseline is null
                    ? [entry.Lease.Target]
                    : new[] { entry.Lease.Baseline, entry.Lease.Target })
                .Cast<CatalogIndexBuildResult>()
                .ToArray();
        }
    }

    private CatalogDownloadLease Create(
        string storeCode,
        CatalogIndexBuildResult? baseResult,
        CatalogIndexBuildResult target,
        IReadOnlyList<CatalogDeltaOperation> operations)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpired(now);
            var normalizedStore = storeCode.Trim();
            if (_leases.Count >= GlobalCapacity ||
                _leases.Values.Count(entry => string.Equals(entry.Lease.StoreCode, normalizedStore, StringComparison.OrdinalIgnoreCase)) >= StoreCapacity)
            {
                throw new CatalogCapacityBusyException("Catalog download lease capacity is busy.");
            }

            var lease = new CatalogDownloadLease(
                Guid.NewGuid().ToString("N"),
                normalizedStore,
                baseResult?.CatalogIndex.CatalogVersion,
                target,
                baseResult,
                operations,
                now,
                now.Add(AbsoluteTtl));
            _leases.Add(lease.LeaseId, new LeaseEntry(lease, now));
            return lease;
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var expired = _leases
            .Where(pair => pair.Value.Lease.AbsoluteExpiresAt <= now || pair.Value.LastAccessAt.Add(IdleTtl) <= now)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expired)
        {
            _leases.Remove(key);
        }
    }

    private sealed class LeaseEntry(CatalogDownloadLease lease, DateTimeOffset lastAccessAt)
    {
        public CatalogDownloadLease Lease { get; } = lease;
        public DateTimeOffset LastAccessAt { get; set; } = lastAccessAt;
    }
}

public sealed record CatalogDownloadLease(
    string LeaseId,
    string StoreCode,
    string? BaseCatalogVersion,
    CatalogIndexBuildResult Target,
    CatalogIndexBuildResult? Baseline,
    IReadOnlyList<CatalogDeltaOperation> DeltaOperations,
    DateTimeOffset CreatedAt,
    DateTimeOffset AbsoluteExpiresAt);

public sealed record CatalogDeltaOperation(
    string LookupCodeNormalized,
    CatalogLookupItemDto? Item,
    DeletedLookupDto? DeletedLookup)
{
    public static CatalogDeltaOperation Upsert(CatalogLookupItemDto item) => new(item.LookupCodeNormalized, item, null);
    public static CatalogDeltaOperation Delete(DeletedLookupDto deletedLookup) => new(deletedLookup.LookupCodeNormalized, null, deletedLookup);
}
