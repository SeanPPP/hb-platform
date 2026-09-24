using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.SupplyNotices;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseProductSupplyNoticeTests : IDisposable
{
    private const string StoreCode = "1001";
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public WarehouseProductSupplyNoticeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables(typeof(Product), typeof(WarehouseProduct));
        WarehouseProductSupplyNoticeSchemaMigrator
            .EnsureAsync(_db, NullLogger.Instance)
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    // ---------- 录入规则 ----------

    [Fact]
    public void Normalize_按月录入_展开为整月()
    {
        var (notice, error) = WarehouseProductSupplyNoticeRules.Normalize(
            new WarehouseProductSupplyNoticeInputDto
            {
                SupplyPlan = WarehouseProductSupplyPlans.WillRestock,
                ExpectedPrecision = WarehouseProductSupplyExpectedPrecisions.Month,
                ExpectedFrom = new DateOnly(2026, 2, 17),
            }
        );

        Assert.Null(error);
        Assert.Equal(new DateTime(2026, 2, 1), notice!.ExpectedFrom);
        Assert.Equal(new DateTime(2026, 2, 28), notice.ExpectedTo);
    }

    [Fact]
    public void Normalize_不再供应_清空预计时间()
    {
        var (notice, error) = WarehouseProductSupplyNoticeRules.Normalize(
            new WarehouseProductSupplyNoticeInputDto
            {
                SupplyPlan = WarehouseProductSupplyPlans.Discontinued,
                ExpectedPrecision = WarehouseProductSupplyExpectedPrecisions.Day,
                ExpectedFrom = new DateOnly(2026, 10, 5),
            }
        );

        Assert.Null(error);
        Assert.Equal(WarehouseProductSupplyExpectedPrecisions.Unknown, notice!.ExpectedPrecision);
        Assert.Null(notice.ExpectedFrom);
        Assert.Null(notice.ExpectedTo);
    }

    [Theory]
    [InlineData("", "Unknown")] // 后续计划必选
    [InlineData("Maybe", "Unknown")]
    [InlineData("WillRestock", "Day")] // 选了“某日”却没给日期
    [InlineData("WillRestock", "Week")]
    public void Normalize_录入无效_返回错误(string plan, string precision)
    {
        var (notice, error) = WarehouseProductSupplyNoticeRules.Normalize(
            new WarehouseProductSupplyNoticeInputDto { SupplyPlan = plan, ExpectedPrecision = precision }
        );

        Assert.Null(notice);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Normalize_范围起止颠倒_返回错误()
    {
        var (_, error) = WarehouseProductSupplyNoticeRules.Normalize(
            new WarehouseProductSupplyNoticeInputDto
            {
                SupplyPlan = WarehouseProductSupplyPlans.WillRestock,
                ExpectedPrecision = WarehouseProductSupplyExpectedPrecisions.Range,
                ExpectedFrom = new DateOnly(2026, 10, 10),
                ExpectedTo = new DateOnly(2026, 10, 5),
            }
        );

        Assert.NotNull(error);
    }

    // ---------- 写入收口 ----------

    [Fact]
    public async Task 下架登记说明_再次下架同商品只更新不新增()
    {
        await SeedProductAsync("P-1", warehouseActive: false);

        await ApplyAsync("P-1", isActive: false, Notice(WarehouseProductSupplyPlans.Undecided));
        await ApplyAsync("P-1", isActive: false, Notice(WarehouseProductSupplyPlans.WillRestock, new DateOnly(2026, 10, 5)));

        var notices = await _db.Queryable<WarehouseProductSupplyNotice>().ToListAsync();
        var notice = Assert.Single(notices);
        Assert.Equal(WarehouseProductSupplyPlans.WillRestock, notice.SupplyPlan);
        Assert.Equal(new DateTime(2026, 10, 5), notice.ExpectedTo);
        Assert.Null(notice.ClosedAtUtc);
    }

    [Fact]
    public async Task 下架不带说明_不产生记录()
    {
        await SeedProductAsync("P-1", warehouseActive: false);

        await ApplyAsync("P-1", isActive: false, notice: null);

        Assert.Empty(await _db.Queryable<WarehouseProductSupplyNotice>().ToListAsync());
    }

    [Fact]
    public async Task 上架关闭说明_只关闭当前确实在架的商品()
    {
        await SeedProductAsync("P-ON", warehouseActive: false);
        await SeedProductAsync("P-OFF", warehouseActive: false);
        await ApplyAsync("P-ON", isActive: false, Notice(WarehouseProductSupplyPlans.WillRestock));
        await ApplyAsync("P-OFF", isActive: false, Notice(WarehouseProductSupplyPlans.WillRestock));
        await SetWarehouseActiveAsync("P-ON", true);

        // 无界面入口（货柜回写等）调用时不区分动作，一律按商品当前状态判断。
        var closed = await WarehouseProductSupplyNoticeWriter.CloseNoticesForActiveProductsAsync(
            _db,
            new[] { "P-ON", "P-OFF" },
            "tester",
            DateTime.UtcNow
        );

        Assert.Equal(1, closed);
        var notices = await _db.Queryable<WarehouseProductSupplyNotice>().ToListAsync();
        Assert.NotNull(notices.Single(item => item.ProductCode == "P-ON").ClosedAtUtc);
        Assert.Null(notices.Single(item => item.ProductCode == "P-OFF").ClosedAtUtc);
    }

    [Fact]
    public async Task 缺表时写入降级为空操作_不抛异常()
    {
        await _db.Ado.ExecuteCommandAsync("DROP TABLE \"WarehouseProductSupplyNotice\"");

        await ApplyAsync("P-1", isActive: false, Notice(WarehouseProductSupplyPlans.WillRestock));
        await ApplyAsync("P-1", isActive: true, notice: null);
    }

    // ---------- 分店端查询 ----------

    [Fact]
    public async Task Lookup_只返回主档启用且仓库暂停供货的商品_并带出说明()
    {
        await SeedProductAsync("P-PAUSED", warehouseActive: false, itemNumber: "HB-100", barcode: "9300000000017");
        await SeedProductAsync("P-ACTIVE", warehouseActive: true, itemNumber: "HB-200");
        await SeedProductAsync("P-HQ-OFF", warehouseActive: false, itemNumber: "HB-300", productActive: false);
        await ApplyAsync(
            "P-PAUSED",
            isActive: false,
            Notice(WarehouseProductSupplyPlans.WillRestock, new DateOnly(2099, 10, 5), storeNote: "运输途中", internalNote: "供应商延期")
        );
        var service = CreateStoreService();

        var byBarcode = await service.LookupAsync(StoreCode, "9300000000017");
        var byItemNumber = await service.LookupAsync(StoreCode, "hb-100");
        var active = await service.LookupAsync(StoreCode, "HB-200");
        var hqDisabled = await service.LookupAsync(StoreCode, "HB-300");

        var item = Assert.Single(byBarcode.Items);
        Assert.Equal("barcode", byBarcode.MatchType);
        Assert.Equal("itemNumber", byItemNumber.MatchType);
        Assert.Equal("P-PAUSED", item.ProductCode);
        Assert.False(item.IsOrderable);
        Assert.True(item.HasNotice);
        Assert.Equal(WarehouseProductSupplyPlans.WillRestock, item.SupplyPlan);
        Assert.Equal(new DateOnly(2099, 10, 5), item.ExpectedTo);
        Assert.Equal("运输途中", item.StoreFacingNote);
        Assert.False(item.IsOverdue);
        // 在架商品走正常订货查询；主档被 HQ 停用的商品对分店来说不存在。
        Assert.Empty(active.Items);
        Assert.Empty(hqDisabled.Items);
    }

    [Fact]
    public async Task Lookup_没有说明的历史下架商品_按后续计划待确认展示()
    {
        await SeedProductAsync("P-OLD", warehouseActive: false, itemNumber: "HB-OLD");

        var item = Assert.Single((await CreateStoreService().LookupAsync(StoreCode, "HB-OLD")).Items);

        Assert.False(item.HasNotice);
        Assert.Equal(WarehouseProductSupplyPlans.Undecided, item.SupplyPlan);
    }

    [Fact]
    public async Task Lookup_预计时间已过_标逾期且不再展示过期日期()
    {
        await SeedProductAsync("P-LATE", warehouseActive: false, itemNumber: "HB-LATE");
        await ApplyAsync("P-LATE", isActive: false, Notice(WarehouseProductSupplyPlans.WillRestock, new DateOnly(2020, 1, 1)));

        var item = Assert.Single((await CreateStoreService().LookupAsync(StoreCode, "HB-LATE")).Items);

        Assert.True(item.IsOverdue);
        Assert.Null(item.ExpectedFrom);
        Assert.Null(item.ExpectedTo);
        Assert.Equal(WarehouseProductSupplyPlans.WillRestock, item.SupplyPlan);
    }

    // ---------- 关注 ----------

    [Fact]
    public async Task 关注_重复关注幂等_在架商品不可关注()
    {
        await SeedProductAsync("P-PAUSED", warehouseActive: false);
        await SeedProductAsync("P-ACTIVE", warehouseActive: true);
        var service = CreateStoreService();

        Assert.True((await service.WatchAsync(StoreCode, "P-PAUSED", "u1")).Success);
        Assert.True((await service.WatchAsync(StoreCode, "P-PAUSED", "u2")).Success);
        Assert.False((await service.WatchAsync(StoreCode, "P-ACTIVE", "u1")).Success);
        Assert.False((await service.WatchAsync(StoreCode, "P-NOPE", "u1")).Success);

        Assert.Single(await _db.Queryable<StoreProductSupplyWatch>().ToListAsync());
        Assert.True(Assert.Single((await service.LookupAsync(StoreCode, "P-PAUSED")).Items).IsWatching);
        Assert.False(Assert.Single((await service.LookupAsync("1002", "P-PAUSED")).Items).IsWatching);
    }

    [Fact]
    public async Task 关注_商品恢复订货后计入提醒_确认只关闭已恢复的关注()
    {
        await SeedProductAsync("P-BACK", warehouseActive: false);
        await SeedProductAsync("P-WAIT", warehouseActive: false);
        var service = CreateStoreService();
        await service.WatchAsync(StoreCode, "P-BACK", "u1");
        await service.WatchAsync(StoreCode, "P-WAIT", "u1");

        // 不经过任何挂钩直接改状态，模拟货柜回写等无界面入口：提醒靠状态推导，不能漏。
        await SetWarehouseActiveAsync("P-BACK", true);

        var summary = await service.GetWatchSummaryAsync(StoreCode);
        Assert.Equal(1, summary.RestockedCount);
        Assert.Equal(1, summary.WatchingCount);
        var watches = await service.GetWatchesAsync(StoreCode);
        Assert.Equal("P-BACK", watches[0].ProductCode); // 已恢复的排最前
        Assert.True(watches[0].IsOrderable);

        // 即使把仍在等待的商品一起传进来，也只确认真正恢复的那个。
        var closed = await service.AcknowledgeRestockedAsync(StoreCode, new[] { "P-BACK", "P-WAIT" }, "u1");

        Assert.Equal(1, closed);
        var after = await service.GetWatchSummaryAsync(StoreCode);
        Assert.Equal(0, after.RestockedCount);
        Assert.Equal(1, after.WatchingCount);
    }

    [Fact]
    public async Task 取消关注后可以重新关注()
    {
        await SeedProductAsync("P-1", warehouseActive: false);
        var service = CreateStoreService();

        await service.WatchAsync(StoreCode, "P-1", "u1");
        await service.UnwatchAsync(StoreCode, "P-1", "u1");
        Assert.Equal(0, (await service.GetWatchSummaryAsync(StoreCode)).WatchingCount);
        await service.WatchAsync(StoreCode, "P-1", "u1");

        Assert.Equal(1, (await service.GetWatchSummaryAsync(StoreCode)).WatchingCount);
        Assert.Equal(2, await _db.Queryable<StoreProductSupplyWatch>().CountAsync());
    }

    // ---------- 仓库端 ----------

    [Fact]
    public async Task 仓库批量登记_只处理已下架商品_并回报跳过项与关注门店数()
    {
        await SeedProductAsync("P-PAUSED", warehouseActive: false);
        await SeedProductAsync("P-ACTIVE", warehouseActive: true);
        await CreateStoreService().WatchAsync(StoreCode, "P-PAUSED", "u1");
        await CreateStoreService().WatchAsync("1002", "P-PAUSED", "u2");
        var service = new WarehouseProductSupplyNoticeService(CreateContext());

        var result = await service.UpsertForPausedProductsAsync(
            new BatchUpsertWarehouseProductSupplyNoticeRequestDto
            {
                ProductCodes = new List<string> { "P-PAUSED", "P-ACTIVE", "P-NOPE" },
                Notice = new WarehouseProductSupplyNoticeInputDto
                {
                    SupplyPlan = WarehouseProductSupplyPlans.Seasonal,
                    ExpectedPrecision = WarehouseProductSupplyExpectedPrecisions.Month,
                    ExpectedFrom = new DateOnly(2099, 9, 1),
                    InternalNote = "圣诞季商品",
                },
            },
            "warehouse-user"
        );

        Assert.True(result.Success);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(new[] { "P-ACTIVE", "P-NOPE" }, result.SkippedProductCodes);
        var notice = Assert.Single(await service.GetOpenNoticesAsync(new[] { "P-PAUSED", "P-ACTIVE" }));
        Assert.Equal(WarehouseProductSupplyPlans.Seasonal, notice.SupplyPlan);
        Assert.Equal(new DateOnly(2099, 9, 30), notice.ExpectedTo);
        Assert.Equal("圣诞季商品", notice.InternalNote);
        Assert.Equal("warehouse-user", notice.UpdatedBy);
        Assert.Equal(2, notice.WatchingStoreCount);
    }

    [Fact]
    public async Task 仓库批量登记_录入无效时不写库()
    {
        await SeedProductAsync("P-PAUSED", warehouseActive: false);
        var service = new WarehouseProductSupplyNoticeService(CreateContext());

        var result = await service.UpsertForPausedProductsAsync(
            new BatchUpsertWarehouseProductSupplyNoticeRequestDto
            {
                ProductCodes = new List<string> { "P-PAUSED" },
                Notice = new WarehouseProductSupplyNoticeInputDto { SupplyPlan = "" },
            },
            "warehouse-user"
        );

        Assert.False(result.Success);
        Assert.Empty(await _db.Queryable<WarehouseProductSupplyNotice>().ToListAsync());
    }

    // ---------- 辅助 ----------

    private static NormalizedSupplyNotice Notice(
        string plan,
        DateOnly? day = null,
        string? storeNote = null,
        string? internalNote = null
    )
    {
        var (notice, error) = WarehouseProductSupplyNoticeRules.Normalize(
            new WarehouseProductSupplyNoticeInputDto
            {
                SupplyPlan = plan,
                ExpectedPrecision = day.HasValue
                    ? WarehouseProductSupplyExpectedPrecisions.Day
                    : WarehouseProductSupplyExpectedPrecisions.Unknown,
                ExpectedFrom = day,
                StoreFacingNote = storeNote,
                InternalNote = internalNote,
            }
        );
        Assert.Null(error);
        return notice!;
    }

    private Task ApplyAsync(string productCode, bool isActive, NormalizedSupplyNotice? notice) =>
        WarehouseProductSupplyNoticeWriter.ApplyStatusChangeAsync(
            _db,
            new[] { productCode },
            isActive,
            notice,
            "tester",
            "Test",
            DateTime.UtcNow
        );

    private async Task SeedProductAsync(
        string productCode,
        bool warehouseActive,
        string? itemNumber = null,
        string? barcode = null,
        bool productActive = true
    )
    {
        await _db.Insertable(new Product
        {
            UUID = $"UUID-{productCode}",
            ProductCode = productCode,
            ProductName = $"商品 {productCode}",
            ItemNumber = itemNumber,
            Barcode = barcode,
            IsActive = productActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            IsActive = warehouseActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private Task SetWarehouseActiveAsync(string productCode, bool isActive) =>
        _db.Updateable<WarehouseProduct>()
            .SetColumns(item => item.IsActive == isActive)
            .Where(item => item.ProductCode == productCode)
            .ExecuteCommandAsync();

    private StoreProductSupplyService CreateStoreService() => new(CreateContext());

    private SqlSugarContext CreateContext()
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, _db);
        return context;
    }
}
