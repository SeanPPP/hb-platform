using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.ProductInsights;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>移动端仓库商品进销查询的口径回归：进货只认实际到货，分店行合并订货/发货/销售。</summary>
public sealed class WarehouseProductInsightServiceTests : IDisposable
{
    private static readonly DateTime RangeStart = new(2026, 6, 1);
    private static readonly DateTime RangeEnd = new(2026, 8, 31);

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public WarehouseProductInsightServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(CreateConnectionConfig(_connection.ConnectionString));
        _db.CodeFirst.InitTables(
            typeof(WarehouseProduct),
            typeof(Product),
            typeof(DomesticProduct),
            typeof(ChinaSupplier),
            typeof(Container),
            typeof(ContainerDetail),
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(ProductStoreDailySalesStatistic),
            typeof(ProductLocation),
            typeof(Location),
            typeof(Store)
        );
    }

    [Fact]
    public void 区间上限按含首尾计算且为四百天()
    {
        var endDate = new DateTime(2026, 9, 17);

        Assert.Equal(400, WarehouseProductInsightRules.MaxRangeDays);
        Assert.Equal(1, WarehouseProductInsightRules.CountDays(endDate, endDate));
        Assert.Equal(400, WarehouseProductInsightRules.CountDays(WarehouseProductInsightRules.ClampStartDate(endDate), endDate));
        Assert.True(WarehouseProductInsightRules.IsWithinMaxRange(endDate.AddDays(-399), endDate));
        Assert.False(WarehouseProductInsightRules.IsWithinMaxRange(endDate.AddDays(-400), endDate));
        Assert.Equal(90, WarehouseProductInsightRules.CountDays(WarehouseProductInsightRules.DefaultStartDate(endDate), endDate));
    }

    [Fact]
    public async Task 超过四百天的区间直接拒绝而不是截断查询()
    {
        await SeedProductAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().GetProductInsightAsync(
                new WarehouseProductInsightQuery
                {
                    ProductCode = "P1",
                    StartDate = RangeEnd.AddDays(-400),
                    EndDate = RangeEnd,
                },
                null
            )
        );

        Assert.Contains("400", exception.Message);
    }

    [Fact]
    public async Task 进货只统计实际到货货柜在途货柜单列不计入合计()
    {
        await SeedProductAsync();
        await SeedContainerAsync("C1", "HG-ARRIVED", actualArrivalDate: new DateTime(2026, 7, 10), estimatedArrivalDate: new DateTime(2026, 7, 1), quantity: 600m);
        await SeedContainerAsync("C2", "HG-INTRANSIT", actualArrivalDate: null, estimatedArrivalDate: new DateTime(2026, 8, 20), quantity: 120m);

        var result = await QueryAsync();

        Assert.Equal(600m, result.Totals.InboundQuantity);
        Assert.Equal(1, result.Totals.ContainerCount);
        Assert.Equal(120m, result.Totals.InTransitQuantity);
        Assert.Equal(1, result.Totals.InTransitContainerCount);
        var inTransit = Assert.Single(result.Containers, row => row.IsEstimatedArrival);
        Assert.Equal("HG-INTRANSIT", inTransit.ContainerNumber);
        Assert.Equal(new DateTime(2026, 8, 20), inTransit.ArrivalDate);
    }

    [Fact]
    public async Task 货柜进货固定按最近一年统计不跟随查询区间()
    {
        await SeedProductAsync();
        // 到货日早于查询区间，但仍在结束日往前一年内：仓库靠库存供货，这批货必须算进进货。
        await SeedContainerAsync("C1", "HG-LASTYEAR", actualArrivalDate: new DateTime(2025, 12, 1), estimatedArrivalDate: null, quantity: 800m);
        // 超过一年的货柜不计入。
        await SeedContainerAsync("C2", "HG-TOOOLD", actualArrivalDate: new DateTime(2025, 8, 1), estimatedArrivalDate: null, quantity: 500m);

        var result = await QueryAsync();

        Assert.Equal(800m, result.Totals.InboundQuantity);
        Assert.Equal(1, result.Totals.ContainerCount);
        Assert.Equal("2025-09-01", result.InboundRange.StartDate);
        Assert.Equal("2026-08-31", result.InboundRange.EndDate);
        Assert.Equal(365, result.InboundRange.DayCount);
        // 订货发货销售仍按查询区间，两套区间必须分别回显。
        Assert.Equal("2026-06-01", result.Range.StartDate);
        Assert.Equal(92, result.Range.DayCount);
    }

    [Fact]
    public async Task 发货超过进货属于正常库存供货不被截断()
    {
        await SeedProductAsync();
        await SeedStoreAsync("s1", "B1", "分店一");
        await SeedContainerAsync("C1", "HG-001", actualArrivalDate: new DateTime(2026, 7, 1), estimatedArrivalDate: null, quantity: 100m);
        await SeedOrderAsync("O1", "B1", "WO-001", orderDate: new DateTime(2026, 6, 10), outboundDate: new DateTime(2026, 6, 11), quantity: 300m, allocQuantity: 300m);

        var result = await QueryAsync();

        Assert.Equal(100m, result.Totals.InboundQuantity);
        Assert.Equal(300m, result.Totals.ShippedQuantity);
    }

    [Fact]
    public async Task 分店行同时给出订货发货销售与待发和售罄率()
    {
        await SeedProductAsync();
        await SeedStoreAsync("s1", "B1", "班克斯敦");
        // 同一张单据先订货后出库：订货按订单日、发货按出库日分别归集。
        await SeedOrderAsync("O1", "B1", "WO-001", orderDate: new DateTime(2026, 6, 10), outboundDate: new DateTime(2026, 6, 12), quantity: 100m, allocQuantity: 60m);
        await SeedStatisticAsync(new DateTime(2026, 6, 20), "B1", 30, 240m);
        await SeedStatisticAsync(new DateTime(2026, 6, 21), "B1", 15, 120m);

        var result = await QueryAsync();

        var branch = Assert.Single(result.Branches);
        Assert.Equal("B1", branch.StoreCode);
        Assert.Equal("班克斯敦", branch.StoreName);
        Assert.Equal(100m, branch.OrderedQuantity);
        Assert.Equal(60m, branch.ShippedQuantity);
        Assert.Equal(40m, branch.PendingQuantity);
        Assert.Equal(45, branch.SalesQuantity);
        Assert.Equal(360m, branch.SalesAmount);
        Assert.Equal(0.75m, branch.SellThroughRate);
        Assert.Equal(40m, result.Totals.PendingQuantity);
        Assert.Equal(1, result.Totals.PendingStoreCount);
        Assert.Equal(2, result.DailySales.Count);
        Assert.Equal(new DateTime(2026, 6, 21), result.DailySales[0].Date);
    }

    [Fact]
    public async Task 未发货分店的售罄率返回空而不是零()
    {
        await SeedProductAsync();
        await SeedStoreAsync("s1", "B1", "只订未发");
        await SeedOrderAsync("O1", "B1", "WO-001", orderDate: new DateTime(2026, 6, 10), outboundDate: null, quantity: 80m, allocQuantity: 0m);

        var result = await QueryAsync();

        var branch = Assert.Single(result.Branches);
        Assert.Equal(80m, branch.OrderedQuantity);
        Assert.Equal(0m, branch.ShippedQuantity);
        Assert.Null(branch.SellThroughRate);
    }

    [Fact]
    public async Task 授权分店之外的订货发货销售不进入结果()
    {
        await SeedProductAsync();
        await SeedStoreAsync("s1", "B1", "授权分店");
        await SeedStoreAsync("s2", "B2", "未授权分店");
        await SeedOrderAsync("O1", "B1", "WO-001", orderDate: new DateTime(2026, 6, 10), outboundDate: new DateTime(2026, 6, 11), quantity: 50m, allocQuantity: 50m);
        await SeedOrderAsync("O2", "B2", "WO-002", orderDate: new DateTime(2026, 6, 10), outboundDate: new DateTime(2026, 6, 11), quantity: 70m, allocQuantity: 70m);
        await SeedStatisticAsync(new DateTime(2026, 6, 20), "B1", 10, 80m);
        await SeedStatisticAsync(new DateTime(2026, 6, 20), "B2", 20, 160m);

        var result = await QueryAsync(["B1"]);

        var branch = Assert.Single(result.Branches);
        Assert.Equal("B1", branch.StoreCode);
        Assert.Equal(50m, result.Totals.OrderedQuantity);
        Assert.Equal(10, result.Totals.SalesQuantity);
        Assert.Equal("authorized-stores", result.Scope);
    }

    [Fact]
    public async Task 区间外的订货与销售不计入合计()
    {
        await SeedProductAsync();
        await SeedStoreAsync("s1", "B1", "分店一");
        await SeedOrderAsync("O1", "B1", "WO-EARLY", orderDate: RangeStart.AddDays(-1), outboundDate: RangeStart.AddDays(-1), quantity: 99m, allocQuantity: 99m);
        await SeedOrderAsync("O2", "B1", "WO-IN", orderDate: RangeEnd, outboundDate: RangeEnd, quantity: 11m, allocQuantity: 11m);
        await SeedStatisticAsync(RangeEnd.AddDays(1), "B1", 77, 616m);
        await SeedStatisticAsync(RangeEnd, "B1", 7, 56m);

        var result = await QueryAsync();

        Assert.Equal(11m, result.Totals.OrderedQuantity);
        Assert.Equal(11m, result.Totals.ShippedQuantity);
        Assert.Equal(7, result.Totals.SalesQuantity);
    }

    [Fact]
    public async Task 没有任何授权分店时不返回分店数据但仍返回商品与货柜()
    {
        await SeedProductAsync();
        await SeedStoreAsync("s1", "B1", "分店一");
        await SeedContainerAsync("C1", "HG-001", actualArrivalDate: new DateTime(2026, 7, 10), estimatedArrivalDate: null, quantity: 300m);
        await SeedOrderAsync("O1", "B1", "WO-001", orderDate: new DateTime(2026, 6, 10), outboundDate: new DateTime(2026, 6, 11), quantity: 50m, allocQuantity: 50m);

        var result = await QueryAsync([]);

        Assert.Empty(result.Branches);
        Assert.Equal(0m, result.Totals.OrderedQuantity);
        Assert.Equal(300m, result.Totals.InboundQuantity);
        Assert.Equal("P1", result.Product.ProductCode);
    }

    [Fact]
    public async Task 商品主档带出库存与仓位()
    {
        await SeedProductAsync(stockQuantity: 318);
        await _db.Insertable(new Location { LocationGuid = "L1", LocationCode = "A-03-12" }).ExecuteCommandAsync();
        await _db.Insertable(new ProductLocation { Guid = "PL1", ProductCode = "P1", LocationGuid = "L1" }).ExecuteCommandAsync();

        var result = await QueryAsync();

        Assert.Equal("保温杯", result.Product.ProductName);
        Assert.Equal("A1207", result.Product.ItemNumber);
        Assert.Equal(318, result.Product.StockQuantity);
        Assert.Equal("A-03-12", result.Product.LocationCode);
    }

    private async Task<WarehouseProductInsightDto> QueryAsync(List<string>? branchCodes = null)
    {
        var response = await CreateService().GetProductInsightAsync(
            new WarehouseProductInsightQuery
            {
                ProductCode = "P1",
                StartDate = RangeStart,
                EndDate = RangeEnd,
            },
            branchCodes
        );
        Assert.True(response.Success);
        return response.Data!;
    }

    private async Task SeedProductAsync(int? stockQuantity = null)
    {
        await _db.Insertable(new Product
        {
            UUID = "u1",
            ProductCode = "P1",
            ProductName = "保温杯",
            ItemNumber = "A1207",
            Barcode = "9312004417",
        }).ExecuteCommandAsync();
        await _db.Insertable(new WarehouseProduct { ProductCode = "P1", StockQuantity = stockQuantity }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeGuid, string storeCode, string storeName) =>
        await _db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = true,
        }).ExecuteCommandAsync();

    private async Task SeedContainerAsync(
        string containerCode,
        string containerNumber,
        DateTime? actualArrivalDate,
        DateTime? estimatedArrivalDate,
        decimal quantity
    )
    {
        await _db.Insertable(new Container
        {
            ContainerCode = containerCode,
            ContainerNumber = containerNumber,
            ActualArrivalDate = actualArrivalDate,
            EstimatedArrivalDate = estimatedArrivalDate,
        }).ExecuteCommandAsync();
        await _db.Insertable(new ContainerDetail
        {
            DetailCode = $"{containerCode}-D1",
            ContainerCode = containerCode,
            ProductCode = "P1",
            LoadingQuantity = quantity,
            LoadingPieces = 10m,
        }).ExecuteCommandAsync();
    }

    private async Task SeedOrderAsync(
        string orderGuid,
        string storeCode,
        string orderNo,
        DateTime orderDate,
        DateTime? outboundDate,
        decimal quantity,
        decimal allocQuantity
    )
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            StoreCode = storeCode,
            OrderNo = orderNo,
            OrderDate = orderDate,
            OutboundDate = outboundDate,
            FlowStatus = outboundDate.HasValue ? 2 : 1,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-D1",
            OrderGUID = orderGuid,
            ProductCode = "P1",
            Quantity = quantity,
            AllocQuantity = allocQuantity,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStatisticAsync(DateTime date, string branchCode, int quantity, decimal amount) =>
        await _db.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date,
            BranchCode = branchCode,
            SupplierCode = "200",
            ProductCode = "P1",
            TotalQuantity = quantity,
            TotalAmount = amount,
            UpdateTime = date.AddHours(6),
        }).ExecuteCommandAsync();

    private WarehouseProductFlowAnalysisService CreateService() =>
        new(
            CreateSqlSugarContext(_db),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<WarehouseProductFlowAnalysisService>.Instance
        );

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (File.Exists(_dbPath))
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
