using System.Diagnostics;
using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;
using Xunit.Abstractions;

namespace Hbpos.Api.Tests;

public sealed class CatalogScaleFactAttribute : FactAttribute
{
    public CatalogScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HBPOS_CATALOG_SCALE_RUN") != "1")
        {
            Skip = "Set HBPOS_CATALOG_SCALE_RUN=1 to run the catalog scale replay.";
        }
    }
}

[Collection(CatalogSnapshotStorePerformanceCollection.CollectionName)]
public sealed class CatalogMemoryScaleTests(ITestOutputHelper output)
{
    private const int ItemsPerStore = 344_665;
    private const int FullStoreCount = 28;
    private const int SoftItemCapacity = 500_000;
    private const int HardItemCapacity = 1_200_000;
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    [CatalogScaleFact]
    [Trait("Category", "Performance")]
    public async Task Sequential_28_store_catalog_build_and_durable_restore_stays_within_memory_budget()
    {
        var storeCount = Environment.GetEnvironmentVariable("HBPOS_CATALOG_SCALE_STORES") == "1"
            ? 1
            : FullStoreCount;
        using var directory = new TemporaryDirectory();
        var snapshotStore = new GzipCatalogSnapshotStore(directory.Path);
        var cache = new CatalogIndexCache(
            TimeProvider.System,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromHours(2),
            maxSnapshotsPerStore: 3,
            snapshotStore,
            softItemCapacity: SoftItemCapacity,
            hardItemCapacity: HardItemCapacity,
            rawArtifactCapacity: 2);
        using var process = Process.GetCurrentProcess();
        using var sampleCancellation = new CancellationTokenSource();
        long sampledPeakRssBytes = 0;
        var sampleTask = Task.Run(async () =>
        {
            while (!sampleCancellation.IsCancellationRequested)
            {
                process.Refresh();
                UpdateMaximum(ref sampledPeakRssBytes, process.WorkingSet64);
                try
                {
                    await Task.Delay(50, sampleCancellation.Token);
                }
                catch (OperationCanceledException) when (sampleCancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        });

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var overallStartedAt = Stopwatch.GetTimestamp();
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        output.WriteLine(
            "gc_budget_bytes={0} server_gc={1} baseline_rss_bytes={2}",
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            System.Runtime.GCSettings.IsServerGC,
            process.WorkingSet64);
        try
        {
            for (var storeNumber = 1; storeNumber <= storeCount; storeNumber++)
            {
                var storeCode = $"S{storeNumber:D2}";
                var storeStartedAt = Stopwatch.GetTimestamp();
                var version = await BuildAndPublishStoreAsync(cache, storeCode);
                versions.Add(storeCode, version);

                process.Refresh();
                UpdateMaximum(ref sampledPeakRssBytes, process.WorkingSet64);
                var gcInfo = GC.GetGCMemoryInfo();
                output.WriteLine(
                    "store={0} elapsed_ms={1:F0} rss_bytes={2} managed_heap_bytes={3} " +
                    "pinned_items={4} pinned_versions={5} active_entries={6} raw_versions={7} " +
                    "gen2_collections={8} gc_heap_bytes={9} gc_committed_bytes={10} gc_fragmented_bytes={11}",
                    storeCode,
                    Stopwatch.GetElapsedTime(storeStartedAt).TotalMilliseconds,
                    process.WorkingSet64,
                    GC.GetTotalMemory(forceFullCollection: false),
                    cache.PinnedItemCountForTests,
                    cache.PinnedVersionCountForTests,
                    cache.ActiveEntryCountForTests,
                    cache.RawArtifactVersionCountForTests,
                    GC.CollectionCount(2),
                    gcInfo.HeapSizeBytes,
                    gcInfo.TotalCommittedBytes,
                    gcInfo.FragmentedBytes);
                Assert.InRange(cache.PinnedItemCountForTests, 0, HardItemCapacity);
                Assert.InRange(
                    cache.ActiveEntryCountForTests,
                    0,
                    cache.PinnedVersionCountForTests);
            }

            // 磁盘验证逐店读取，避免测试自身一次性持有 28 份完整反序列化正文。
            var descriptors = snapshotStore.LoadDescriptors(DateTimeOffset.UtcNow);
            Assert.Equal(storeCount, descriptors.Count);
            foreach (var (storeCode, version) in versions)
            {
                ValidateRestoredStore(snapshotStore, storeCode, version);
            }
        }
        finally
        {
            sampleCancellation.Cancel();
            await sampleTask;
        }

        process.Refresh();
        var peakRssBytes = Math.Max(sampledPeakRssBytes, process.PeakWorkingSet64);
        var compressedBytes = Directory.EnumerateFiles(directory.Path, "*.json.gz", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        output.WriteLine(
            "catalog_scale stores={0} items_per_store={1} total_items={2} elapsed_s={3:F1} " +
            "peak_rss_bytes={4} allocated_bytes={5} final_pinned_items={6} " +
            "final_pinned_versions={7} final_active_entries={8} gzip_bytes={9} restored_stores={10}",
            storeCount,
            ItemsPerStore,
            (long)storeCount * ItemsPerStore,
            Stopwatch.GetElapsedTime(overallStartedAt).TotalSeconds,
            peakRssBytes,
            allocatedBytes,
            cache.PinnedItemCountForTests,
            cache.PinnedVersionCountForTests,
            cache.ActiveEntryCountForTests,
            compressedBytes,
            versions.Count);

        // 目标容器拟限 3 GiB；Mac 进程回放也需保持在同一数量级，Linux cgroup 仍需独立验证。
        Assert.True(peakRssBytes < 3L * 1024 * 1024 * 1024,
            $"catalog replay peak RSS {peakRssBytes} bytes exceeds the 3 GiB test budget");
    }

    private static async Task<string> BuildAndPublishStoreAsync(CatalogIndexCache cache, string storeCode)
    {
        var version = $"catalog-v1:scale:{storeCode}";
        var result = await cache.ForceRefreshAndPublishAsync(
            storeCode,
            since: null,
            cancellationToken => Task.Run<CatalogIndexBuildResult?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var items = CreateItems(storeCode);
                var index = new CatalogSellableIndex(storeCode, GeneratedAt, items, version);
                return new CatalogIndexBuildResult(storeCode, GeneratedAt, items, index);
            }, cancellationToken),
            CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(ItemsPerStore, result.CatalogIndex.Items.Count);
        Assert.Equal(version, result.CatalogIndex.CatalogVersion);
        return version;
    }

    private static void ValidateRestoredStore(
        GzipCatalogSnapshotStore snapshotStore,
        string storeCode,
        string version)
    {
        var restored = snapshotStore.Load(storeCode, since: null, version);
        Assert.NotNull(restored);
        Assert.Equal(storeCode, restored.StoreCode);
        Assert.Equal(version, restored.CatalogVersion);
        Assert.Equal(ItemsPerStore, restored.SellableItems.Count);
        Assert.Equal("P0000001", restored.SellableItems[0].ProductCode);
        Assert.Equal($"P{ItemsPerStore:D7}", restored.SellableItems[^1].ProductCode);
    }

    private static SellableItemDto[] CreateItems(string storeCode)
    {
        var items = new SellableItemDto[ItemsPerStore];
        for (var index = 0; index < items.Length; index++)
        {
            var ordinal = index + 1;
            var productCode = $"P{ordinal:D7}";
            var barcode = $"93{ordinal:D11}";
            items[index] = new SellableItemDto(
                storeCode,
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

    private static void UpdateMaximum(ref long maximum, long candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (observed >= candidate ||
                Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
            {
                return;
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"hbpos-catalog-scale-{Guid.NewGuid():N}");
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
