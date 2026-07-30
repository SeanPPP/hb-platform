using System.Diagnostics;
using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;
using Xunit.Abstractions;

namespace Hbpos.Api.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class CatalogSnapshotStorePerformanceCollection
{
    public const string CollectionName = "CatalogSnapshotStorePerformance";
}

[Collection(CatalogSnapshotStorePerformanceCollection.CollectionName)]
public sealed class CatalogSnapshotStorePerformanceTests(ITestOutputHelper output)
{
    private const int ItemCount = 344_665;
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 7, 30, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Performance")]
    public void Save_344665_items_reports_independent_gzip_duration()
    {
        using var directory = new TemporaryDirectory();
        var dataStartedAt = Stopwatch.GetTimestamp();
        var items = CreateItems();
        var dataElapsed = Stopwatch.GetElapsedTime(dataStartedAt);
        var snapshot = new CatalogPersistedSnapshot(
            "S01",
            Since: null,
            GeneratedAt,
            GeneratedAt.AddHours(2),
            "catalog-perf-344665",
            items);
        var store = new GzipCatalogSnapshotStore(directory.Path);
        var allocatedBeforeSave = GC.GetTotalAllocatedBytes(precise: true);

        var saveStartedAt = Stopwatch.GetTimestamp();
        store.Save(snapshot);
        var saveElapsed = Stopwatch.GetElapsedTime(saveStartedAt);
        var allocatedDuringSave =
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBeforeSave;

        var gzipPath = Assert.Single(Directory.GetFiles(
            directory.Path,
            "*.json.gz",
            SearchOption.AllDirectories));
        var gzipBytes = new FileInfo(gzipPath).Length;
        var descriptor = Assert.Single(
            store.LoadDescriptors(GeneratedAt.AddMinutes(1)));

        Assert.Equal(ItemCount, items.Length);
        Assert.Equal("S01", descriptor.StoreCode);
        Assert.Equal("catalog-perf-344665", descriptor.CatalogVersion);
        Assert.True(gzipBytes > 0);
        output.WriteLine(
            "catalog_gzip_perf item_count={0} data_ms={1:F1} save_ms={2:F1} " +
            "gzip_bytes={3} bytes_per_item={4:F2} save_allocated_bytes={5} managed_heap_bytes={6}",
            ItemCount,
            dataElapsed.TotalMilliseconds,
            saveElapsed.TotalMilliseconds,
            gzipBytes,
            (double)gzipBytes / ItemCount,
            allocatedDuringSave,
            GC.GetTotalMemory(forceFullCollection: false));
    }

    private static SellableItemDto[] CreateItems()
    {
        var items = new SellableItemDto[ItemCount];
        for (var index = 0; index < items.Length; index++)
        {
            var ordinal = index + 1;
            var productCode = $"P{ordinal:D7}";
            var barcode = $"93{ordinal:D11}";
            items[index] = new SellableItemDto(
                "S01",
                productCode,
                $"REF-{ordinal:D7}",
                $"性能测试商品 {ordinal % 2_000:D4}",
                barcode,
                $"ITEM-{ordinal:D7}",
                barcode,
                (ordinal % 20_000 + 1) / 100m,
                ordinal % 8 == 0
                    ? PriceSourceKind.StoreRetailPrice
                    : PriceSourceKind.ProductBase,
                ordinal % 8 == 0 ? "门店价" : "商品基础价",
                ordinal % 20 == 0 ? 0.5m : 1m,
                GeneratedAt.AddSeconds(ordinal % 3_600),
                ordinal % 10 == 0
                    ? $"https://images.example.invalid/{productCode}.jpg"
                    : null,
                ordinal % 25 == 0 ? 0.1m : null,
                ordinal % 100 == 0);
        }

        return items;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"hbpos-catalog-gzip-perf-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
