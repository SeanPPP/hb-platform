using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PosmSalesOrderReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public PosmSalesOrderReactServiceTests()
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
        _db.CodeFirst.InitTables(typeof(SalesOrder), typeof(SalesOrderDetail), typeof(Store), typeof(Product));
    }

    [Fact]
    public async Task GetSalesOrderListAsync_商品编码关键词命中且父订单去重()
    {
        await SeedOrderAsync("ORDER-001", new DateTime(2026, 7, 1, 9, 0, 0), "DEVICE-A");
        await SeedDetailAsync("ORDER-001", "jm-001", "BAR-001", "商品一");
        await SeedDetailAsync("ORDER-001", "jm-001", "BAR-002", "商品二");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { Keyword = "  jm-001  " }
        );

        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("ORDER-001", result.Items[0].OrderGuid);
    }

    [Fact]
    public async Task GetMatchedProductsAsync_按订单聚合命中商品且与列表关键词口径一致()
    {
        await SeedOrderAsync("ORDER-001", new DateTime(2026, 7, 1, 9, 0, 0), "DEVICE-A");
        await SeedOrderAsync("ORDER-002", new DateTime(2026, 7, 1, 10, 0, 0), "DEVICE-A");
        await SeedOrderAsync("ORDER-003", new DateTime(2026, 7, 1, 11, 0, 0), "DEVICE-A");
        // 同一订单同一货号两行（不同折扣），命中后必须合并为一条并累计数量。
        await SeedDetailAsync("ORDER-001", "jm-001", "BAR-001", "保温杯");
        await SeedDetailAsync("ORDER-001", "jm-001", "BAR-001", "保温杯");
        await SeedDetailAsync("ORDER-001", "jm-002", "BAR-002", "纸巾");
        await SeedDetailAsync("ORDER-002", "jm-003", "BAR-003", "保温杯 大号");
        await SeedDetailAsync("ORDER-003", "jm-004", "BAR-004", "收纳箱");

        var service = CreateService();
        var matched = await service.GetMatchedProductsAsync(
            new[] { "ORDER-001", "ORDER-002", "ORDER-003", "ORDER-001" },
            " 保温杯 "
        );

        Assert.Equal(2, matched.Count);
        var first = Assert.Single(matched["ORDER-001"]);
        Assert.Equal("jm-001", first.ProductCode);
        Assert.Equal("保温杯", first.ProductName);
        Assert.Equal("BAR-001", first.Barcode);
        Assert.Equal(2, first.Quantity);
        Assert.Equal("jm-003", Assert.Single(matched["ORDER-002"]).ProductCode);
        Assert.False(matched.ContainsKey("ORDER-003"));

        // 关键词命中订单集合必须与列表过滤结果一致，否则列表会出现没有命中商品的订单。
        var list = await service.GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { Keyword = "保温杯" }
        );
        Assert.Equal(
            matched.Keys.OrderBy(guid => guid),
            list.Items.Select(item => item.OrderGuid!).OrderBy(guid => guid)
        );
        // 件数按明细数量求和：ORDER-001 三行各 1 件 = 3，SKU 去重后为 2。
        var orderOne = list.Items.Single(item => item.OrderGuid == "ORDER-001");
        Assert.Equal(3, orderOne.QuantityTotal);
        Assert.Equal(2, orderOne.SkuCount);
    }

    [Fact]
    public async Task GetMatchedProductsAsync_空关键词或空订单不查库()
    {
        var service = CreateService();
        Assert.Empty(await service.GetMatchedProductsAsync(Array.Empty<string>(), "jm"));
        Assert.Empty(await service.GetMatchedProductsAsync(new[] { "ORDER-001" }, "   "));
    }

    [Fact]
    public async Task 关键词按主档货号命中订单并回带货号()
    {
        await _db.Insertable(
                new Product
                {
                    ProductCode = "81C5AF52",
                    ItemNumber = "HB034-80",
                    Barcode = "9528503423080",
                    ProductName = "Vinal Roll",
                }
            )
            .ExecuteCommandAsync();
        await SeedOrderAsync("ORDER-ITEM", new DateTime(2026, 9, 17, 9, 0, 0), "DEVICE-A");
        await SeedDetailAsync("ORDER-ITEM", "81C5AF52", "", "Vinal Roll");
        await SeedOrderAsync("ORDER-OTHER", new DateTime(2026, 9, 17, 9, 5, 0), "DEVICE-A");
        await SeedDetailAsync("ORDER-OTHER", "OTHER-CODE", "", "别的商品");

        var service = CreateService();
        // 货号不在 POSM 明细里，必须经主档解析成商品编码才能命中。
        var list = await service.GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { Keyword = "HB034-80" }
        );
        Assert.Equal("ORDER-ITEM", Assert.Single(list.Items).OrderGuid);

        var matched = await service.GetMatchedProductsAsync(new[] { "ORDER-ITEM", "ORDER-OTHER" }, "HB034-80");
        var hit = Assert.Single(matched["ORDER-ITEM"]);
        Assert.Equal("81C5AF52", hit.ProductCode);
        Assert.Equal("HB034-80", hit.ItemNumber);
        // 主档没有对应商品的编码不回带货号。
        var otherMatched = await service.GetMatchedProductsAsync(new[] { "ORDER-OTHER" }, "别的");
        Assert.Null(Assert.Single(otherMatched["ORDER-OTHER"]).ItemNumber);
    }

    public static TheoryData<string, string, string> SortCases =>
        new()
        {
            { "orderGuid", "asc", "ORDER-A" },
            { "orderGuid", "desc", "ORDER-C" },
            { "branchCode", "asc", "ORDER-B" },
            { "branchCode", "desc", "ORDER-A" },
            { "deviceCode", "asc", "ORDER-C" },
            { "deviceCode", "desc", "ORDER-B" },
            { "orderTime", "asc", "ORDER-B" },
            { "orderTime", "desc", "ORDER-C" },
            { "skuCount", "asc", "ORDER-B" },
            { "skuCount", "desc", "ORDER-C" },
            { "itemCount", "asc", "ORDER-B" },
            { "itemCount", "desc", "ORDER-C" },
            { "totalAmount", "asc", "ORDER-B" },
            { "totalAmount", "desc", "ORDER-C" },
            { "discountAmount", "asc", "ORDER-B" },
            { "discountAmount", "desc", "ORDER-C" },
            { "actualPay", "asc", "ORDER-B" },
            { "actualPay", "desc", "ORDER-C" },
        };

    [Theory]
    [MemberData(nameof(SortCases))]
    public async Task GetSalesOrderListAsync_白名单字段支持升降序(
        string sortField,
        string sortDirection,
        string expectedFirst
    )
    {
        await SeedSortingOrdersAsync();

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams
            {
                SortField = sortField,
                SortDirection = sortDirection,
                PageSize = 10,
            }
        );

        Assert.Equal(expectedFirst, result.Items[0].OrderGuid);
    }

    [Theory]
    [InlineData("ORDER-ALPHA")]
    [InlineData("DEVICE-ALPHA")]
    [InlineData("BAR-ALPHA")]
    [InlineData("苹果汁")]
    public async Task GetSalesOrderListAsync_通用关键词匹配订单设备条码和商品名(string keyword)
    {
        await SeedOrderAsync("ORDER-ALPHA", new DateTime(2026, 7, 2, 10, 30, 0), "DEVICE-ALPHA");
        await SeedDetailAsync("ORDER-ALPHA", "P-ALPHA", "BAR-ALPHA", "苹果汁");
        await SeedOrderAsync("ORDER-BETA", new DateTime(2026, 7, 2, 11, 30, 0), "DEVICE-BETA");
        await SeedDetailAsync("ORDER-BETA", "P-BETA", "BAR-BETA", "橙汁");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { Keyword = $" {keyword} " }
        );

        Assert.Equal("ORDER-ALPHA", Assert.Single(result.Items).OrderGuid);
    }

    [Fact]
    public async Task GetSalesOrderListAsync_显式条件与通用关键词按And组合()
    {
        await SeedOrderAsync(
            "MATCH-ORDER",
            new DateTime(2026, 7, 2, 10, 30, 0),
            "DEVICE-01",
            "BRANCH-01",
            itemCount: 4,
            totalAmount: 30m,
            discountAmount: 5m
        );
        await SeedDetailAsync("MATCH-ORDER", "P-ONE", "BAR-MATCH", "匹配商品");
        await SeedDetailAsync("MATCH-ORDER", "P-TWO", "BAR-OTHER", "其他商品");
        await SeedOrderAsync("WRONG-BRANCH", new DateTime(2026, 7, 2, 10, 30, 0), "DEVICE-01", "BRANCH-02", 4, 30m, 5m);
        await SeedDetailAsync("WRONG-BRANCH", "P-ONE", "BAR-MATCH", "匹配商品");
        await SeedOrderAsync("OTHER-ORDER", new DateTime(2026, 7, 2, 10, 30, 0), "DEVICE-01", "BRANCH-01", 4, 30m, 5m);
        await SeedDetailAsync("OTHER-ORDER", "P-ONE", "BAR-MATCH", "匹配商品");
        await SeedDetailAsync("OTHER-ORDER", "P-TWO", "BAR-OTHER", "其他商品");
        await SeedOrderAsync("MATCH-WRONG-DEVICE", new DateTime(2026, 7, 2, 10, 30, 0), "OTHER", "BRANCH-01", 4, 30m, 5m);
        await SeedDetailAsync("MATCH-WRONG-DEVICE", "P-ONE", "BAR-MATCH", "匹配商品");
        await SeedDetailAsync("MATCH-WRONG-DEVICE", "P-TWO", "BAR-OTHER", "其他商品");
        await SeedOrderAsync("MATCH-WRONG-DATE", new DateTime(2026, 7, 3, 10, 30, 0), "DEVICE-01", "BRANCH-01", 4, 30m, 5m);
        await SeedDetailAsync("MATCH-WRONG-DATE", "P-ONE", "BAR-MATCH", "匹配商品");
        await SeedDetailAsync("MATCH-WRONG-DATE", "P-TWO", "BAR-OTHER", "其他商品");
        await SeedOrderAsync("MATCH-WRONG-TIME", new DateTime(2026, 7, 2, 10, 31, 0), "DEVICE-01", "BRANCH-01", 4, 30m, 5m);
        await SeedDetailAsync("MATCH-WRONG-TIME", "P-ONE", "BAR-MATCH", "匹配商品");
        await SeedDetailAsync("MATCH-WRONG-TIME", "P-TWO", "BAR-OTHER", "其他商品");
        await SeedOrderAsync("MATCH-WRONG-KEYWORD", new DateTime(2026, 7, 2, 10, 30, 0), "DEVICE-01", "BRANCH-01", 4, 30m, 5m);
        await SeedDetailAsync("MATCH-WRONG-KEYWORD", "P-OTHER", "BAR-OTHER", "其他商品");
        await SeedDetailAsync("MATCH-WRONG-KEYWORD", "P-ANOTHER", "BAR-ANOTHER", "另一商品");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams
            {
                Keyword = "P-ONE",
                OrderGuidKeyword = "MATCH",
                DeviceCodeKeyword = "DEVICE-0",
                BranchCode = "BRANCH-01",
                StartDate = new DateTime(2026, 7, 2),
                EndDate = new DateTime(2026, 7, 2),
                TimeStart = new TimeSpan(10, 30, 0),
                TimeEnd = new TimeSpan(10, 30, 0),
                SkuCountMin = 2,
                SkuCountMax = 2,
                ItemCountMin = 4,
                ItemCountMax = 4,
                TotalAmountMin = 30m,
                TotalAmountMax = 30m,
                DiscountAmountMin = 5m,
                DiscountAmountMax = 5m,
                ActualPayMin = 25m,
                ActualPayMax = 25m,
            }
        );

        Assert.Equal("MATCH-ORDER", Assert.Single(result.Items).OrderGuid);
    }

    [Fact]
    public async Task GetSalesOrderListAsync_数值区间包含上下边界()
    {
        await SeedOrderAsync("BOUNDARY", new DateTime(2026, 7, 2, 12, 0, 0), "D", itemCount: 3, totalAmount: 12m, discountAmount: 2m, storedActualAmount: 999m);
        await SeedDetailAsync("BOUNDARY", "P-1", "B-1", "一");
        await SeedDetailAsync("BOUNDARY", "P-2", "B-2", "二");
        await SeedOrderAsync("OUT-SKU", new DateTime(2026, 7, 2, 12, 0, 0), "D", itemCount: 3, totalAmount: 12m, discountAmount: 2m);
        await SeedDetailAsync("OUT-SKU", "P-1", "B-1", "一");
        await SeedOrderAsync("OUT-ITEM", new DateTime(2026, 7, 2, 12, 0, 0), "D", itemCount: 4, totalAmount: 12m, discountAmount: 2m);
        await SeedDetailAsync("OUT-ITEM", "P-1", "B-1", "一");
        await SeedDetailAsync("OUT-ITEM", "P-2", "B-2", "二");
        await SeedOrderAsync("OUT-TOTAL", new DateTime(2026, 7, 2, 12, 0, 0), "D", itemCount: 3, totalAmount: 14m, discountAmount: 3m);
        await SeedDetailAsync("OUT-TOTAL", "P-1", "B-1", "一");
        await SeedDetailAsync("OUT-TOTAL", "P-2", "B-2", "二");
        await SeedOrderAsync("OUT-DISCOUNT", new DateTime(2026, 7, 2, 12, 0, 0), "D", itemCount: 3, totalAmount: 11m, discountAmount: 0m);
        await SeedDetailAsync("OUT-DISCOUNT", "P-1", "B-1", "一");
        await SeedDetailAsync("OUT-DISCOUNT", "P-2", "B-2", "二");
        await SeedOrderAsync("OUT-ACTUAL", new DateTime(2026, 7, 2, 12, 0, 0), "D", itemCount: 3, totalAmount: 13m, discountAmount: 1m);
        await SeedDetailAsync("OUT-ACTUAL", "P-1", "B-1", "一");
        await SeedDetailAsync("OUT-ACTUAL", "P-2", "B-2", "二");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams
            {
                SkuCountMin = 2,
                SkuCountMax = 2,
                ItemCountMin = 3,
                ItemCountMax = 3,
                TotalAmountMin = 11m,
                TotalAmountMax = 13m,
                DiscountAmountMin = 1m,
                DiscountAmountMax = 3m,
                ActualPayMin = 9m,
                ActualPayMax = 11m,
            }
        );

        Assert.Equal("BOUNDARY", Assert.Single(result.Items).OrderGuid);
    }

    [Fact]
    public async Task GetSalesOrderListAsync_每个数值区间都能单独命中边界()
    {
        await SeedOrderAsync("BOUNDARY", new DateTime(2026, 7, 2, 12, 0, 0), "D", itemCount: 3, totalAmount: 12m, discountAmount: 2m, storedActualAmount: 999m);
        await SeedDetailAsync("BOUNDARY", "P-1", "B-1", "一");
        await SeedDetailAsync("BOUNDARY", "P-2", "B-2", "二");
        var queries = new Dictionary<string, PosmSalesOrderQueryParams>
        {
            ["sku"] = new() { SkuCountMin = 2, SkuCountMax = 2 },
            ["item"] = new() { ItemCountMin = 3, ItemCountMax = 3 },
            ["total"] = new() { TotalAmountMin = 12m, TotalAmountMax = 12m },
            ["discount"] = new() { DiscountAmountMin = 2m, DiscountAmountMax = 2m },
            ["actualPay"] = new() { ActualPayMin = 10m, ActualPayMax = 10m },
        };

        var misses = new List<string>();
        foreach (var (name, query) in queries)
        {
            var result = await CreateService().GetSalesOrderListAsync(query);
            if (result.Total != 1)
                misses.Add($"{name}={result.Total}");
        }

        Assert.True(misses.Count == 0, string.Join(", ", misses));
    }

    [Fact]
    public async Task GetSalesOrderListAsync_列表保留存储实付字段但ActualPay仍按总额减折扣()
    {
        await SeedOrderAsync(
            "CALCULATED-PAY",
            new DateTime(2026, 7, 2, 12, 0, 0),
            "D",
            totalAmount: 12m,
            discountAmount: 2m,
            storedActualAmount: 999m
        );
        await SeedDetailAsync("CALCULATED-PAY", "P-1", "B-1", "一");
        await SeedOrderAsync(
            "LOWER-PAY",
            new DateTime(2026, 7, 2, 12, 0, 0),
            "D",
            totalAmount: 9m,
            discountAmount: 1m,
            storedActualAmount: 1000m
        );
        await SeedDetailAsync("LOWER-PAY", "P-2", "B-2", "二");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams
            {
                ActualPayMin = 10m,
                ActualPayMax = 10m,
                SortField = "actualPay",
                SortDirection = "desc",
            }
        );

        var item = Assert.Single(result.Items);
        Assert.Equal("CALCULATED-PAY", item.OrderGuid);
        Assert.Equal(999m, item.ActualAmount);
    }

    [Fact]
    public async Task GetSalesOrderListAsync_Sku聚合可过滤并排序()
    {
        await SeedOrderAsync("ONE-SKU", new DateTime(2026, 7, 2, 12, 0, 0), "D1");
        await SeedDetailAsync("ONE-SKU", "P-1", "B-1", "一");
        await SeedOrderAsync("TWO-SKU", new DateTime(2026, 7, 2, 12, 0, 0), "D2");
        await SeedDetailAsync("TWO-SKU", "P-1", "B-2", "一");
        await SeedDetailAsync("TWO-SKU", "P-2", "B-3", "二");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams
            {
                SkuCountMin = 2,
                SortField = "skuCount",
                SortDirection = "desc",
            }
        );

        var item = Assert.Single(result.Items);
        Assert.Equal("TWO-SKU", item.OrderGuid);
        Assert.Equal(2, item.SkuCount);
    }

    [Theory]
    [InlineData("DROP TABLE", "asc")]
    [InlineData("orderGuid", "sideways")]
    public async Task GetSalesOrderListAsync_非法排序字段或方向回退下单时间升序(
        string sortField,
        string sortDirection
    )
    {
        await SeedOrderAsync("LATE", new DateTime(2026, 7, 2, 13, 0, 0), "D1");
        await SeedDetailAsync("LATE", "P-1", "B-1", "一");
        await SeedOrderAsync("EARLY", new DateTime(2026, 7, 2, 9, 0, 0), "D2");
        await SeedDetailAsync("EARLY", "P-2", "B-2", "二");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams
            {
                SortField = sortField,
                SortDirection = sortDirection,
            }
        );

        Assert.Equal(new[] { "EARLY", "LATE" }, result.Items.Select(x => x.OrderGuid));
    }

    [Fact]
    public async Task GetSalesOrderListAsync_相同主排序值按订单号稳定分页()
    {
        var sameTime = new DateTime(2026, 7, 2, 9, 0, 0);
        foreach (var orderGuid in new[] { "ORDER-C", "ORDER-A", "ORDER-B" })
        {
            await SeedOrderAsync(orderGuid, sameTime, "SAME");
            await SeedDetailAsync(orderGuid, $"P-{orderGuid}", $"B-{orderGuid}", orderGuid);
        }

        var service = CreateService();
        var first = await service.GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { SortField = "orderTime", SortDirection = "asc", PageNumber = 1, PageSize = 1 }
        );
        var second = await service.GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { SortField = "orderTime", SortDirection = "asc", PageNumber = 2, PageSize = 1 }
        );

        Assert.Equal("ORDER-A", first.Items[0].OrderGuid);
        Assert.Equal("ORDER-B", second.Items[0].OrderGuid);
    }

    [Fact]
    public async Task GetSalesOrderListAsync_空白关键词不限制结果且负页码不产生负Skip()
    {
        await SeedOrderAsync("ORDER-1", new DateTime(2026, 7, 2, 9, 0, 0), "D1");
        await SeedDetailAsync("ORDER-1", "P-1", "B-1", "一");
        await SeedOrderAsync("ORDER-2", new DateTime(2026, 7, 2, 10, 0, 0), "D2");
        await SeedDetailAsync("ORDER-2", "P-2", "B-2", "二");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { Keyword = "   ", PageNumber = -2, PageSize = 10 }
        );

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetSalesOrderListAsync_最大页码不会让分页偏移溢出为负数()
    {
        await SeedOrderAsync("ORDER-1", new DateTime(2026, 7, 2, 9, 0, 0), "D1");
        await SeedDetailAsync("ORDER-1", "P-1", "B-1", "一");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { PageNumber = int.MaxValue, PageSize = 2 }
        );

        Assert.Equal(1, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetSalesOrderListAsync_最大PageSize限制响应和查询条数为一千()
    {
        await SeedManyOrdersAsync(1001);

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams { PageNumber = 1, PageSize = int.MaxValue }
        );

        Assert.Equal(1001, result.Total);
        Assert.Equal(1000, result.PageSize);
        Assert.Equal(1000, result.Items.Count);
    }

    [Fact]
    public async Task 日期与时段筛选按订单墙钟时间直接比较不做时区平移()
    {
        // OrderTime 是 POS 写入的门店本地墙钟时间，2026-09-17 已对照库内最新订单与悉尼时间核实。
        await SeedOrderAsync("WALL-MATCH", new DateTime(2026, 7, 17, 0, 30, 0), "D1");
        await SeedDetailAsync("WALL-MATCH", "P-1", "B-1", "七月十七日零点半");
        await SeedOrderAsync("PREVIOUS-DAY", new DateTime(2026, 7, 16, 23, 30, 0), "D2");
        await SeedDetailAsync("PREVIOUS-DAY", "P-2", "B-2", "七月十六日深夜");
        await SeedOrderAsync("NEXT-DAY", new DateTime(2026, 7, 18, 0, 30, 0), "D3");
        await SeedDetailAsync("NEXT-DAY", "P-3", "B-3", "七月十八日零点半");
        await SeedOrderAsync("WRONG-TIME", new DateTime(2026, 7, 17, 10, 30, 0), "D4");
        await SeedDetailAsync("WRONG-TIME", "P-4", "B-4", "七月十七日上午十点半");

        var result = await CreateService().GetSalesOrderListAsync(
            new PosmSalesOrderQueryParams
            {
                StartDate = new DateTime(2026, 7, 17),
                EndDate = new DateTime(2026, 7, 17),
                TimeStart = new TimeSpan(0, 30, 0),
                TimeEnd = new TimeSpan(0, 30, 0),
            }
        );

        Assert.Equal("WALL-MATCH", Assert.Single(result.Items).OrderGuid);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private PosmSalesOrderReactService CreateService()
    {
        var mapper = new MapperConfiguration(_ => { }, NullLoggerFactory.Instance).CreateMapper();
        return new PosmSalesOrderReactService(
            CreateContext<POSMSqlSugarContext>(),
            CreateContext<SqlSugarContext>(),
            mapper,
            NullLogger<PosmSalesOrderReactService>.Instance
        );
    }

    private TContext CreateContext<TContext>()
    {
        var context = (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
        typeof(TContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, _db);
        return context;
    }

    private Task SeedOrderAsync(
        string orderGuid,
        DateTime orderTime,
        string deviceCode,
        string branchCode = "BRANCH-A",
        int itemCount = 2,
        decimal totalAmount = 20m,
        decimal discountAmount = 2m,
        decimal? storedActualAmount = null
    ) =>
        _db.Insertable(
                new SalesOrder
                {
                    OrderGuid = orderGuid,
                    OrderTime = orderTime,
                    BranchCode = branchCode,
                    DeviceCode = deviceCode,
                    ItemCount = itemCount,
                    TotalAmount = totalAmount,
                    DiscountAmount = discountAmount,
                    ActualAmount = storedActualAmount ?? totalAmount - discountAmount,
                    Status = (int)OrderType.Paid,
                }
            )
            .ExecuteCommandAsync();

    private Task SeedDetailAsync(
        string orderGuid,
        string productCode,
        string barcode,
        string productName
    ) =>
        _db.Insertable(
                new SalesOrderDetail
                {
                    OrderDetailGuid = Guid.NewGuid().ToString("N"),
                    OrderGuid = orderGuid,
                    ProductCode = productCode,
                    Barcode = barcode,
                    ProductName = productName,
                    Quantity = 1,
                }
            )
            .ExecuteCommandAsync();

    private async Task SeedSortingOrdersAsync()
    {
        await SeedOrderAsync("ORDER-A", new DateTime(2026, 7, 2, 11, 0, 0), "DEVICE-B", "BRANCH-C", 2, 20m, 2m, 999m);
        await SeedDetailAsync("ORDER-A", "A-1", "A-1", "A-1");
        await SeedDetailAsync("ORDER-A", "A-2", "A-2", "A-2");

        await SeedOrderAsync("ORDER-B", new DateTime(2026, 7, 2, 9, 0, 0), "DEVICE-C", "BRANCH-A", 1, 10m, 1m, 500m);
        await SeedDetailAsync("ORDER-B", "B-1", "B-1", "B-1");

        await SeedOrderAsync("ORDER-C", new DateTime(2026, 7, 2, 13, 0, 0), "DEVICE-A", "BRANCH-B", 3, 30m, 3m, 0m);
        await SeedDetailAsync("ORDER-C", "C-1", "C-1", "C-1");
        await SeedDetailAsync("ORDER-C", "C-2", "C-2", "C-2");
        await SeedDetailAsync("ORDER-C", "C-3", "C-3", "C-3");
    }

    private async Task SeedManyOrdersAsync(int count)
    {
        var orders = Enumerable.Range(1, count)
            .Select(index =>
                new SalesOrder
                {
                    OrderGuid = $"ORDER-{index:D4}",
                    OrderTime = new DateTime(2026, 7, 2, 9, 0, 0).AddSeconds(index),
                    BranchCode = "BRANCH-A",
                    DeviceCode = "D",
                    ItemCount = 1,
                    TotalAmount = 1m,
                    DiscountAmount = 0m,
                    ActualAmount = 1m,
                    Status = (int)OrderType.Paid,
                }
            )
            .ToList();
        var details = orders.Select(order =>
                new SalesOrderDetail
                {
                    OrderDetailGuid = Guid.NewGuid().ToString("N"),
                    OrderGuid = order.OrderGuid!,
                    ProductCode = $"P-{order.OrderGuid}",
                    Barcode = $"B-{order.OrderGuid}",
                    ProductName = order.OrderGuid,
                    Quantity = 1,
                }
            )
            .ToList();

        await _db.Insertable(orders).ExecuteCommandAsync();
        await _db.Insertable(details).ExecuteCommandAsync();
    }
}
