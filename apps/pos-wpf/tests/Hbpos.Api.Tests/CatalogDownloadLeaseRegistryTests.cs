using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Api.Tests;

public sealed class CatalogDownloadLeaseRegistryTests
{
    [Fact]
    public void Lease_rejects_store_or_version_mismatch_and_enforces_capacity()
    {
        var registry = new CatalogDownloadLeaseRegistry();
        var target = CreateResult("S01", "catalog-v1:target");
        var lease = registry.CreateFull(target);

        Assert.Same(target, registry.GetAndTouch(lease.LeaseId, "S01", null, "catalog-v1:target").Target);
        Assert.Throws<CatalogSnapshotExpiredException>(
            () => registry.GetAndTouch(lease.LeaseId, "S02", null, "catalog-v1:target"));

        for (var number = 0; number < 127; number++)
        {
            registry.CreateFull(CreateResult($"S{number + 10}", $"catalog-v1:{number}"));
        }

        Assert.Throws<CatalogCapacityBusyException>(
            () => registry.CreateFull(CreateResult("S-capacity", "catalog-v1:overflow")));
    }

    [Fact]
    public void Lease_enforces_store_idle_and_absolute_boundaries()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var registry = new CatalogDownloadLeaseRegistry(clock);
        var lease = registry.CreateFull(CreateResult("S01", "catalog-v1:target"));
        for (var index = 0; index < 31; index++)
        {
            registry.CreateFull(CreateResult("S01", $"catalog-v1:{index}"));
        }
        Assert.Throws<CatalogCapacityBusyException>(() => registry.CreateFull(CreateResult("S01", "catalog-v1:overflow")));

        clock.Advance(TimeSpan.FromMinutes(9));
        registry.Touch(lease.LeaseId, "S01", null, "catalog-v1:target");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Throws<CatalogSnapshotExpiredException>(() => registry.GetAndTouch(lease.LeaseId, "S01", null, "catalog-v1:target"));

        var absolute = registry.CreateFull(CreateResult("S02", "catalog-v1:absolute"));
        for (var index = 0; index < 3; index++)
        {
            clock.Advance(TimeSpan.FromMinutes(9));
            registry.Touch(absolute.LeaseId, "S02", null, "catalog-v1:absolute");
        }
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Throws<CatalogSnapshotExpiredException>(() => registry.GetAndTouch(absolute.LeaseId, "S02", null, "catalog-v1:absolute"));
    }

    private static CatalogIndexBuildResult CreateResult(string storeCode, string version)
    {
        var item = new SellableItemDto(storeCode, "P01", null, "Item", "ITEM", null, null, 1m,
            PriceSourceKind.ProductBase, "product", 1m, DateTimeOffset.UnixEpoch);
        return new CatalogIndexBuildResult(storeCode, DateTimeOffset.UnixEpoch, [item],
            new CatalogSellableIndex(storeCode, DateTimeOffset.UnixEpoch, [item], version));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
