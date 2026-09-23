using System.Text.Json;
using BlazorApp.Api.Services.React.OfflineCatalog;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>可手动推进的时间源，避免为测试引入额外 NuGet 包。</summary>
internal sealed class OfflineCatalogFakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public OfflineCatalogFakeTimeProvider(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}

public sealed class OfflineCatalogIndexTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 17, 1, 2, 3, 456, TimeSpan.Zero);

    private static OfflineCatalogItemDto Item(
        string lookupCode,
        string matchSource = "ProductBarcode",
        string productCode = "P001",
        string? codeId = null,
        decimal? retailPrice = 4.5m)
    {
        return OfflineCatalogItemFinalizer.Finalize(new OfflineCatalogItemDto
        {
            StoreCode = "S001",
            LookupCode = lookupCode,
            MatchSource = matchSource,
            ProductCode = productCode,
            ProductName = "测试商品",
            ItemNumber = "AB 123",
            Barcode = "9300000000017",
            ProductType = 1,
            Grade = "A",
            LocalSupplierCode = "200",
            LocalSupplierName = "Hotbargain",
            StoreName = "Brisbane",
            StorePriceUuid = "sp-1",
            PurchasePrice = 1.5m,
            RetailPrice = retailPrice,
            DiscountRate = 0.2m,
            IsAutoPricing = true,
            IsSpecialProduct = false,
            Rate = 3m,
            StrategySourceLabel = "全局",
            StrategyRuleLabel = "1 - 5",
            CodeId = codeId,
            CodeQuantity = codeId is null ? null : 2,
            CodeType = codeId is null ? null : 1,
            CodeIsActive = codeId is null ? null : true,
            UpdatedAt = "2026-09-17T01:02:03.456Z",
        });
    }

    [Fact]
    public void 归一化售卖码_统一空格变体并大写()
    {
        Assert.Equal("AB 123", OfflineCatalogChecksum.NormalizeLookupCode(" ab　123 "));
        Assert.Equal(string.Empty, OfflineCatalogChecksum.NormalizeLookupCode(null));
    }

    [Fact]
    public void LookupKey_以分隔符拼接四段并作为唯一键()
    {
        var item = Item("set-1", "SetBarcode", codeId: "code-1");
        Assert.Equal("SET-1SetBarcodeP001code-1", item.LookupKey);
        Assert.Equal(64, item.RowVersion.Length);
    }

    [Fact]
    public void RowVersion_只随业务字段变化()
    {
        var left = Item("A");
        var same = Item("A");
        var changed = Item("A", retailPrice: 9.9m);
        Assert.Equal(left.RowVersion, same.RowVersion);
        Assert.NotEqual(left.RowVersion, changed.RowVersion);
    }

    [Fact]
    public void 分页_按LookupKey排序_游标续页_总数一致()
    {
        var index = new OfflineCatalogIndex("S001", GeneratedAt, new[]
        {
            Item("C", productCode: "P3"),
            Item("A", productCode: "P1"),
            Item("B", productCode: "P2"),
        });
        var first = index.GetPage(null, 2);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new[] { "A", "B" }, first.Items.Select(i => i.LookupCode));
        Assert.True(first.HasMore);
        Assert.Equal(first.Items[^1].LookupKey, first.NextCursor);
        Assert.StartsWith(OfflineCatalogChecksum.PagePrefix, first.PageChecksum);

        var second = index.GetPage(first.NextCursor, 2);
        Assert.Equal(new[] { "C" }, second.Items.Select(i => i.LookupCode));
        Assert.False(second.HasMore);
        Assert.Null(second.NextCursor);
        Assert.Equal(first.NextCursor, second.Cursor);
        Assert.Equal(index.CatalogVersion, second.CatalogVersion);
    }

    [Fact]
    public void 页校验和_与移动端固定测试向量一致()
    {
        // 向量文件由移动端 offline-catalog-checksum.test.ts 生成；两端必须得到相同摘要。
        var vectorsPath = Path.Combine(AppContext.BaseDirectory, "OfflineCatalogChecksumVectors.json");
        if (!File.Exists(vectorsPath))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(vectorsPath));
        var root = document.RootElement;
        var items = root.GetProperty("items").EnumerateArray()
            .Select(element => JsonSerializer.Deserialize<OfflineCatalogItemDto>(element.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            })!)
            .ToList();
        Assert.Equal(root.GetProperty("pageChecksum").GetString(), OfflineCatalogChecksum.CreatePageChecksum(items));
        var deleted = new OfflineCatalogDeletedItemDto
        {
            StoreCode = root.GetProperty("deleted").GetProperty("storeCode").GetString()!,
            LookupKey = root.GetProperty("deleted").GetProperty("lookupKey").GetString()!,
            DeletedAt = root.GetProperty("deleted").GetProperty("deletedAt").GetString(),
        };
        var operations = items.Select(OfflineCatalogDeltaOperation.Upsert)
            .Append(OfflineCatalogDeltaOperation.Delete(deleted))
            .ToList();
        Assert.Equal(
            root.GetProperty("deltaChecksum").GetString(),
            OfflineCatalogChecksum.CreateDeltaPageChecksum(
                root.GetProperty("baseCatalogVersion").GetString()!,
                root.GetProperty("targetCatalogVersion").GetString()!,
                operations));
        Assert.Equal(root.GetProperty("rowVersion").GetString(), OfflineCatalogChecksum.CreateRowVersion(items[0]));
    }

    [Fact]
    public void FormatBinary64_与JavaScript_DataView一致()
    {
        Assert.Equal("4012000000000000", OfflineCatalogChecksum.FormatBinary64(4.5m));
        Assert.Equal("0000000000000000", OfflineCatalogChecksum.FormatBinary64(0m));
        Assert.Equal("3fc999999999999a", OfflineCatalogChecksum.FormatBinary64(0.2m));
        Assert.Equal("4008000000000000", OfflineCatalogChecksum.FormatBinary64(3));
    }

    [Fact]
    public void Delta归并_新增_删除_变更_未变()
    {
        var baseline = new OfflineCatalogIndex("S001", GeneratedAt, new[]
        {
            Item("A", productCode: "P1"),
            Item("B", productCode: "P2"),
            Item("C", productCode: "P3"),
        }, "v-base");
        var target = new OfflineCatalogIndex("S001", GeneratedAt.AddMinutes(5), new[]
        {
            Item("A", productCode: "P1"),
            Item("B", productCode: "P2", retailPrice: 9.9m),
            Item("D", productCode: "P4"),
        }, "v-target");

        var operations = target.GetDeltaOperations(baseline);
        Assert.Equal(3, operations.Count);
        Assert.Contains(operations, o => o.Item?.LookupCode == "B" && o.Item.RetailPrice == 9.9m);
        Assert.Contains(operations, o => o.Deleted?.LookupKey == baseline.Items[2].LookupKey);
        Assert.Contains(operations, o => o.Item?.LookupCode == "D");
        Assert.DoesNotContain(operations, o => o.Item?.LookupCode == "A");

        var page = target.GetDeltaPage(baseline, operations, null, 2);
        Assert.Equal(2, page.Items.Count + page.DeletedItems.Count);
        Assert.True(page.HasMore);
        Assert.Equal("v-base", page.BaseCatalogVersion);
        Assert.Equal("v-target", page.TargetCatalogVersion);
        Assert.Equal(3, page.TargetTotal);
        Assert.StartsWith(OfflineCatalogChecksum.DeltaPrefix, page.PageChecksum);
        var rest = target.GetDeltaPage(baseline, operations, page.NextCursor, 2);
        Assert.Equal(1, rest.Items.Count + rest.DeletedItems.Count);
        Assert.False(rest.HasMore);
    }

    [Fact]
    public async Task 缓存_TTL内复用_过期后重建_并保留基线供delta()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(20), TimeSpan.FromHours(2), 3, 1_000, 2_000);
        var builds = 0;
        Task<OfflineCatalogIndex?> Build(CancellationToken _)
        {
            builds += 1;
            return Task.FromResult<OfflineCatalogIndex?>(new OfflineCatalogIndex("S001", time.GetUtcNow(), new[] { Item("A") }));
        }

        var first = await cache.GetOrBuildCurrentAsync("S001", Build, CancellationToken.None);
        var again = await cache.GetOrBuildCurrentAsync("S001", Build, CancellationToken.None);
        Assert.Same(first, again);
        Assert.Equal(1, builds);

        time.Advance(TimeSpan.FromMinutes(21));
        var rebuilt = await cache.GetOrBuildCurrentAsync("S001", Build, CancellationToken.None);
        Assert.NotSame(first, rebuilt);
        Assert.Equal(2, builds);
        Assert.Same(first, cache.GetByVersion("S001", first!.CatalogVersion));
        Assert.Null(cache.GetByVersion("S999", first.CatalogVersion));

        time.Advance(TimeSpan.FromHours(3));
        Assert.Null(cache.GetByVersion("S001", first.CatalogVersion));
        Assert.Same(rebuilt, cache.GetByVersion("S001", rebuilt!.CatalogVersion));
    }

    [Fact]
    public async Task 缓存_租约固定delta操作并在闲置后过期()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(20), TimeSpan.FromHours(2), 3, 1_000, 2_000, TimeSpan.FromMinutes(30));
        var baseline = (await cache.GetOrBuildCurrentAsync("S001", _ => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex("S001", time.GetUtcNow(), new[] { Item("A") }, "v-base")), CancellationToken.None))!;
        time.Advance(TimeSpan.FromMinutes(21));
        var target = (await cache.GetOrBuildCurrentAsync("S001", _ => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex("S001", time.GetUtcNow(), new[] { Item("A"), Item("B", productCode: "P2") }, "v-target")), CancellationToken.None))!;
        var lease = cache.TryCreateDeltaLease(baseline, target, target.GetDeltaOperations(baseline))!;
        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.Null(cache.GetAndTouchLease(lease.LeaseId, "S002"));
        time.Advance(TimeSpan.FromMinutes(31));
        Assert.Null(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task 缓存_超过硬容量时拒绝新构建()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(20), TimeSpan.FromHours(2), 3, 1, 2);
        Task<OfflineCatalogIndex?> Build(string store) =>
            Task.FromResult<OfflineCatalogIndex?>(new OfflineCatalogIndex(store, time.GetUtcNow(), new[] { Item("A"), Item("B", productCode: "P2") }));
        var first = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("S001"), CancellationToken.None))!;
        var lease = cache.TryCreateFullLease(first)!;
        await Assert.ThrowsAsync<OfflineCatalogCapacityBusyException>(() =>
            cache.GetOrBuildCurrentAsync("S002", _ => Build("S002"), CancellationToken.None));
        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
    }

    [Fact]
    public async Task 缓存_活跃完整下载租约不被软容量淘汰()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(20), TimeSpan.FromHours(2), 3, 2, 4);
        Task<OfflineCatalogIndex?> Build(string store) => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex(store, time.GetUtcNow(), new[] { Item("A"), Item("B", productCode: "P2") }));

        var first = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("S001"), CancellationToken.None))!;
        var lease = cache.TryCreateFullLease(first)!;
        await cache.GetOrBuildCurrentAsync("S002", _ => Build("S002"), CancellationToken.None);

        Assert.Same(first, cache.GetByVersion("S001", first.CatalogVersion));
        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.Equal(4, cache.TotalRetainedItems);
    }

    [Fact]
    public async Task 缓存_活跃增量租约保护超保留期基线和目标()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(
            time, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), 3, 2, 4, TimeSpan.FromHours(1));
        var baseline = (await cache.GetOrBuildCurrentAsync("S001", _ => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex("S001", time.GetUtcNow(), new[] { Item("A") })), CancellationToken.None))!;
        time.Advance(TimeSpan.FromMinutes(2));
        var target = (await cache.GetOrBuildCurrentAsync("S001", _ => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex("S001", time.GetUtcNow(), new[] { Item("B") })), CancellationToken.None))!;
        var lease = cache.TryCreateDeltaLease(baseline, target, target.GetDeltaOperations(baseline))!;

        time.Advance(TimeSpan.FromMinutes(3));
        await cache.GetOrBuildCurrentAsync("S002", _ => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex("S002", time.GetUtcNow(), new[] { Item("C") })), CancellationToken.None);

        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.Same(baseline, cache.GetByVersion("S001", baseline.CatalogVersion));
        Assert.Same(target, cache.GetByVersion("S001", target.CatalogVersion));

        time.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(1)));
        Assert.Null(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.Null(cache.GetByVersion("S001", baseline.CatalogVersion));
    }

    [Fact]
    public async Task 缓存_租约占用硬容量时拒绝新目录并保留下载()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(20), TimeSpan.FromHours(2), 3, 1, 3);
        Task<OfflineCatalogIndex?> Build(string store) => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex(store, time.GetUtcNow(), new[] { Item("A"), Item("B", productCode: "P2") }));

        var first = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("S001"), CancellationToken.None))!;
        var lease = cache.TryCreateFullLease(first)!;
        await Assert.ThrowsAsync<OfflineCatalogCapacityBusyException>(() =>
            cache.GetOrBuildCurrentAsync("S002", _ => Build("S002"), CancellationToken.None));

        Assert.Same(first, cache.GetByVersion("S001", first.CatalogVersion));
        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.Equal(2, cache.TotalRetainedItems);
    }

    [Fact]
    public async Task 缓存_发布失败后恢复其他门店的缓存()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(20), TimeSpan.FromHours(2), 3, 3, 5);
        OfflineCatalogIndex Build(string store, int count) => new(
            store, time.GetUtcNow(), Enumerable.Range(0, count)
                .Select(i => Item($"{store}-{i}", productCode: $"P{i}")));

        var leased = (await cache.GetOrBuildCurrentAsync("S001", _ => Task.FromResult<OfflineCatalogIndex?>(
            Build("S001", 2)), CancellationToken.None))!;
        var lease = cache.TryCreateFullLease(leased)!;
        var other = (await cache.GetOrBuildCurrentAsync("S002", _ => Task.FromResult<OfflineCatalogIndex?>(
            Build("S002", 2)), CancellationToken.None))!;

        await Assert.ThrowsAsync<OfflineCatalogCapacityBusyException>(() =>
            cache.GetOrBuildCurrentAsync("S003", _ => Task.FromResult<OfflineCatalogIndex?>(
                Build("S003", 4)), CancellationToken.None));

        Assert.Same(other, cache.GetByVersion("S002", other.CatalogVersion));
        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.Equal(4, cache.TotalRetainedItems);
    }

    [Fact]
    public async Task 缓存_可淘汰同店未租用旧版本以容纳新版本()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(1), TimeSpan.FromHours(2), 3, 2, 3);
        Task<OfflineCatalogIndex?> Build(string version, int count) => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex("S001", time.GetUtcNow(), Enumerable.Range(0, count)
                .Select(i => Item($"{version}-{i}", productCode: $"P{i}")), version));

        var first = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("v1", 1), CancellationToken.None))!;
        var lease = cache.TryCreateFullLease(first)!;
        time.Advance(TimeSpan.FromMinutes(2));
        var middle = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("v2", 1), CancellationToken.None))!;
        time.Advance(TimeSpan.FromMinutes(2));
        var newest = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("v3", 2), CancellationToken.None))!;

        Assert.Same(first, cache.GetByVersion("S001", first.CatalogVersion));
        Assert.Null(cache.GetByVersion("S001", middle.CatalogVersion));
        Assert.Same(newest, cache.GetByVersion("S001", newest.CatalogVersion));
        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.Equal(3, cache.TotalRetainedItems);
    }

    [Fact]
    public async Task 缓存_超过版本数量限制仍保留活跃下载版本()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(1), TimeSpan.FromHours(2), 2, 3, 4);
        Task<OfflineCatalogIndex?> Build(string version) => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex("S001", time.GetUtcNow(), new[] { Item(version) }, version));

        var first = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("v1"), CancellationToken.None))!;
        var lease = cache.TryCreateFullLease(first)!;
        time.Advance(TimeSpan.FromMinutes(2));
        var middle = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("v2"), CancellationToken.None))!;
        time.Advance(TimeSpan.FromMinutes(2));
        var newest = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("v3"), CancellationToken.None))!;

        Assert.Same(first, cache.GetByVersion("S001", first.CatalogVersion));
        Assert.Null(cache.GetByVersion("S001", middle.CatalogVersion));
        Assert.Same(newest, cache.GetByVersion("S001", newest.CatalogVersion));
        Assert.NotNull(cache.GetAndTouchLease(lease.LeaseId, "S001"));
    }

    [Fact]
    public async Task 缓存_被淘汰的版本不能再创建租约()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(1), TimeSpan.FromHours(2), 3, 2, 4);
        Task<OfflineCatalogIndex?> Build(string store) => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex(store, time.GetUtcNow(), new[] { Item($"{store}-A"), Item($"{store}-B", productCode: "P2") }));

        var first = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("S001"), CancellationToken.None))!;
        await cache.GetOrBuildCurrentAsync("S002", _ => Build("S002"), CancellationToken.None);
        Assert.Null(cache.GetByVersion("S001", first.CatalogVersion));
        Assert.Null(cache.TryCreateFullLease(first));
    }

    [Fact]
    public async Task 缓存_增量基线被淘汰后不能创建租约()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(1), TimeSpan.FromHours(2), 3, 3, 4);
        Task<OfflineCatalogIndex?> Build(string store, string version, int count) => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex(store, time.GetUtcNow(), Enumerable.Range(0, count)
                .Select(i => Item($"{store}-{version}-{i}", productCode: $"P{i}")), version));

        var baseline = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("S001", "v1", 1), CancellationToken.None))!;
        time.Advance(TimeSpan.FromMinutes(2));
        var target = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("S001", "v2", 1), CancellationToken.None))!;
        var operations = target.GetDeltaOperations(baseline);
        await cache.GetOrBuildCurrentAsync("S002", _ => Build("S002", "v1", 2), CancellationToken.None);

        Assert.Null(cache.GetByVersion("S001", baseline.CatalogVersion));
        Assert.Same(target, cache.GetByVersion("S001", target.CatalogVersion));
        Assert.Null(cache.TryCreateDeltaLease(baseline, target, operations));
    }

    [Fact]
    public async Task 缓存_租约过期后硬容量中的无租约版本可让出空间()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(
            time, TimeSpan.FromHours(1), TimeSpan.FromHours(2), 3, 2, 3, TimeSpan.FromMinutes(5));
        Task<OfflineCatalogIndex?> Build(string store, int count) => Task.FromResult<OfflineCatalogIndex?>(
            new OfflineCatalogIndex(store, time.GetUtcNow(), Enumerable.Range(0, count)
                .Select(i => Item($"{store}-{i}", productCode: $"P{i}"))));

        var first = (await cache.GetOrBuildCurrentAsync("S001", _ => Build("S001", 2), CancellationToken.None))!;
        var lease = cache.TryCreateFullLease(first)!;
        await cache.GetOrBuildCurrentAsync("S002", _ => Build("S002", 1), CancellationToken.None);
        Assert.Equal(3, cache.TotalRetainedItems);

        time.Advance(TimeSpan.FromMinutes(6));
        var third = (await cache.GetOrBuildCurrentAsync("S003", _ => Build("S003", 1), CancellationToken.None))!;

        Assert.Same(third, cache.GetByVersion("S003", third.CatalogVersion));
        Assert.Null(cache.GetAndTouchLease(lease.LeaseId, "S001"));
        Assert.True(cache.TotalRetainedItems <= 3);
    }

    [Fact]
    public async Task 缓存_硬容量仅允许一个门店构建并在完成后释放名额()
    {
        var time = new OfflineCatalogFakeTimeProvider(GeneratedAt);
        var cache = new OfflineCatalogIndexCache(time, TimeSpan.FromMinutes(20), TimeSpan.FromHours(2), 3, 1, 2);
        OfflineCatalogIndex Build(string store) => new(store, time.GetUtcNow(),
            new[] { Item($"{store}-A"), Item($"{store}-B", productCode: "P2") });
        await cache.GetOrBuildCurrentAsync("S001", _ => Task.FromResult<OfflineCatalogIndex?>(Build("S001")), CancellationToken.None);

        var pending = new TaskCompletionSource<OfflineCatalogIndex?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBuilding = cache.GetOrBuildCurrentAsync("S002", _ => pending.Task, CancellationToken.None);
        var thirdBuilds = 0;
        await Assert.ThrowsAsync<OfflineCatalogCapacityBusyException>(() =>
            cache.GetOrBuildCurrentAsync("S003", _ =>
            {
                thirdBuilds++;
                return Task.FromResult<OfflineCatalogIndex?>(Build("S003"));
            }, CancellationToken.None));
        Assert.Equal(0, thirdBuilds);

        pending.SetResult(Build("S002"));
        await secondBuilding;
        var third = (await cache.GetOrBuildCurrentAsync("S003", _ => Task.FromResult<OfflineCatalogIndex?>(
            Build("S003")), CancellationToken.None))!;
        Assert.Same(third, cache.GetByVersion("S003", third.CatalogVersion));
    }

}
