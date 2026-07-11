using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDashboardBestSellersTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public SalesDashboardBestSellersTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _localConnection.Open();
        _posmConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(Store),
            typeof(StoreRetailPrice),
            typeof(StoreSalesStatistic),
            typeof(StoreSupplierSalesDetail),
            typeof(ProductStoreDailySalesStatistic),
            typeof(SalesStatisticRefreshState)
        );
        _posmDb.CodeFirst.InitTables(
            typeof(SalesOrder),
            typeof(SalesOrderDetail),
            typeof(SalesReturnRecord),
            typeof(PaymentDetail),
            typeof(POSM_设备注册信息表)
        );
    }

    [Fact]
    public async Task GetBestSellersAsync_返回仓库状态条码起订量和参与统计分店数量()
    {
        await SeedProductAsync("P-BEST-1", "ITEM-1", "BAR-1", "热销一", productIsActive: true, warehouseIsActive: false, minOrderQuantity: 6);
        await SeedProductAsync("P-BEST-2", "ITEM-2", "BAR-2", "热销二", productIsActive: false, warehouseIsActive: true, minOrderQuantity: 3);
        await SeedStoreAsync("S1", "Store 1");
        await SeedStoreAsync("S2", "Store 2");
        await SeedStoreAsync("S3", "Store 3");

        await SeedSaleAsync("O-1", "D-1", "P-BEST-1", "S1", new DateTime(2026, 6, 1), 5, 10m);
        await SeedSaleAsync("O-2", "D-2", "P-BEST-1", "S2", new DateTime(2026, 6, 2), 7, 14m);
        await SeedSaleAsync("O-3", "D-3", "P-BEST-1", "S1", new DateTime(2026, 6, 3), 2, 4m);
        await SeedSaleAsync("O-4", "D-4", "P-BEST-2", "S3", new DateTime(2026, 6, 4), 20, 40m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            new List<string> { "S1", "S2" },
            pageIndex: 1,
            pageSize: 10
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("P-BEST-1", product.ProductCode);
        Assert.Equal("BAR-1", product.Barcode);
        Assert.False(product.IsActive);
        Assert.Equal(6, product.MinOrderQuantity);
        Assert.Equal(14, product.Quantity);
        Assert.Equal(28m, product.SalesAmount);
        Assert.Equal(2, product.BranchSalesCount);
        Assert.Collection(
            product.BranchSales,
            row =>
            {
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal("Store 1", row.BranchName);
                Assert.Equal(7, row.Quantity);
            },
            row =>
            {
                Assert.Equal("S2", row.BranchCode);
                Assert.Equal("Store 2", row.BranchName);
                Assert.Equal(7, row.Quantity);
            }
        );
    }

    [Fact]
    public async Task GetBestSellersAsync_空权限分店列表直接返回空结果()
    {
        await SeedProductAsync("P-NO-STORE", "ITEM-NO-STORE", "BAR-NO-STORE", "无权限商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-NO-STORE", "D-NO-STORE", "P-NO-STORE", "S1", new DateTime(2026, 6, 1), 9, 18m);

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            new List<string>(),
            pageIndex: 1,
            pageSize: 50
        );

        Assert.Empty(result.Products);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task GetBestSellersAsync_忽略软删除仓库库存状态()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "P-SOFT-uuid",
            ProductCode = "P-SOFT",
            ItemNumber = "ITEM-SOFT",
            Barcode = "BAR-SOFT",
            ProductName = "软删除库存商品",
            LocalSupplierCode = "200",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = "P-SOFT",
            IsActive = false,
            MinOrderQuantity = 12,
            IsDeleted = true,
        }).ExecuteCommandAsync();
        await SeedSaleAsync("O-SOFT", "D-SOFT", "P-SOFT", "S1", new DateTime(2026, 6, 1), 4, 8m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Null(product.IsActive);
        Assert.Null(product.MinOrderQuantity);
    }

    [Fact]
    public async Task GetBestSellersAsync_仓库商品匹配不受本地供应商码限制()
    {
        await SeedProductAsync(
            "HB022-119",
            "HB022-119",
            "9525810220084",
            "TOY",
            productIsActive: true,
            warehouseIsActive: false,
            minOrderQuantity: 4,
            localSupplierCode: null
        );
        await SeedSaleAsync("O-HB022", "D-HB022", "HB022-119", "S1", new DateTime(2026, 6, 1), 9, 18m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("9525810220084", product.Barcode);
        Assert.False(product.IsActive);
        Assert.Equal(4, product.MinOrderQuantity);
    }

    [Fact]
    public async Task GetBestSellersAsync_HBweb条码缺失时使用统计表条码和名称()
    {
        await SeedProductAsync(
            "P-POSM-BAR",
            "ITEM-POSM-BAR",
            barcode: null,
            name: "",
            productIsActive: true,
            warehouseIsActive: true,
            minOrderQuantity: 1
        );
        await SeedSaleAsync(
            "O-POSM-BAR",
            "D-POSM-BAR",
            "P-POSM-BAR",
            "S1",
            new DateTime(2026, 6, 1),
            3,
            6m,
            barcode: "POSM-BAR-001",
            productName: "POSM 商品名"
        );
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("POSM-BAR-001", product.Barcode);
        Assert.Equal("POSM 商品名", product.ProductName);
    }

    [Fact]
    public async Task GetBestSellersAsync_订单分店为空时用设备注册分店统计StoresSold()
    {
        await SeedProductAsync("P-DEVICE-STORE", "ITEM-DEVICE-STORE", "BAR-DEVICE", "设备分店商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedStoreAsync("S1", "Store 1");
        await SeedStoreAsync("S2", "Store 2");
        await SeedDeviceAsync("POS-1", "S1");
        await SeedDeviceAsync("POS-2", "S2");
        await SeedSaleAsync("O-DEVICE-1", "D-DEVICE-1", "P-DEVICE-STORE", null, new DateTime(2026, 6, 1), 5, 10m, deviceCode: "POS-1");
        await SeedSaleAsync("O-DEVICE-2", "D-DEVICE-2", "P-DEVICE-STORE", null, new DateTime(2026, 6, 2), 7, 14m, deviceCode: "POS-2");
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal(2, product.BranchSalesCount);
        Assert.Collection(
            product.BranchSales,
            row =>
            {
                Assert.Equal("S2", row.BranchCode);
                Assert.Equal("Store 2", row.BranchName);
                Assert.Equal(7, row.Quantity);
            },
            row =>
            {
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal("Store 1", row.BranchName);
                Assert.Equal(5, row.Quantity);
            }
        );
    }

    [Fact]
    public async Task GetBestSellersAsync_数据库分页返回正确总数排名和当前页分店销量()
    {
        await SeedProductAsync("P-RANK-1", "ITEM-RANK-1", "BAR-RANK-1", "排名一", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedProductAsync("P-RANK-2", "ITEM-RANK-2", "BAR-RANK-2", "排名二", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedProductAsync("P-RANK-3", "ITEM-RANK-3", "BAR-RANK-3", "排名三", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedStoreAsync("S1", "Store 1");
        await SeedStoreAsync("S2", "Store 2");

        await SeedSaleAsync("O-RANK-1", "D-RANK-1", "P-RANK-1", "S1", new DateTime(2026, 6, 1), 30, 60m);
        await SeedSaleAsync("O-RANK-2", "D-RANK-2", "P-RANK-2", "S1", new DateTime(2026, 6, 2), 12, 24m);
        await SeedSaleAsync("O-RANK-3", "D-RANK-3", "P-RANK-2", "S2", new DateTime(2026, 6, 3), 8, 16m);
        await SeedSaleAsync("O-RANK-4", "D-RANK-4", "P-RANK-3", "S1", new DateTime(2026, 6, 4), 5, 10m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 2,
            pageSize: 1
        );

        var product = Assert.Single(result.Products);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, product.Rank);
        Assert.Equal("P-RANK-2", product.ProductCode);
        Assert.Equal(20, product.Quantity);
        Assert.Equal(2, product.BranchSalesCount);
        Assert.Equal(new[] { "S1", "S2" }, product.BranchSales.Select(x => x.BranchCode).ToArray());
    }

    [Fact]
    public async Task GetBestSellersAsync_全平台同日期同分页命中缓存()
    {
        await SeedProductAsync("P-CACHE-1", "ITEM-CACHE-1", "BAR-CACHE-1", "缓存商品一", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-CACHE-1", "D-CACHE-1", "P-CACHE-1", "S1", new DateTime(2026, 6, 1), 5, 10m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var service = CreateService();
        var dateRange = new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) };
        var first = await service.GetBestSellersAsync(dateRange, null, pageIndex: 1, pageSize: 50);

        await SeedProductAsync("P-CACHE-2", "ITEM-CACHE-2", "BAR-CACHE-2", "缓存商品二", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-CACHE-2", "D-CACHE-2", "P-CACHE-2", "S1", new DateTime(2026, 6, 2), 99, 198m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var second = await service.GetBestSellersAsync(dateRange, null, pageIndex: 1, pageSize: 50);

        var product = Assert.Single(second.Products);
        Assert.Equal(1, first.Total);
        Assert.Equal(1, second.Total);
        Assert.Equal("P-CACHE-1", product.ProductCode);
        Assert.Equal(5, product.Quantity);
    }

    [Fact]
    public async Task GetBestSellersAsync_不同分页参数使用独立缓存键()
    {
        await SeedProductAsync("P-CACHE-PAGE-1", "ITEM-CACHE-PAGE-1", "BAR-CACHE-PAGE-1", "分页缓存一", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedProductAsync("P-CACHE-PAGE-2", "ITEM-CACHE-PAGE-2", "BAR-CACHE-PAGE-2", "分页缓存二", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-CACHE-PAGE-1", "D-CACHE-PAGE-1", "P-CACHE-PAGE-1", "S1", new DateTime(2026, 6, 1), 20, 40m);
        await SeedSaleAsync("O-CACHE-PAGE-2", "D-CACHE-PAGE-2", "P-CACHE-PAGE-2", "S1", new DateTime(2026, 6, 1), 10, 20m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var service = CreateService();
        var dateRange = new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) };
        var firstPage = await service.GetBestSellersAsync(dateRange, null, pageIndex: 1, pageSize: 1);

        await SeedProductAsync("P-CACHE-PAGE-3", "ITEM-CACHE-PAGE-3", "BAR-CACHE-PAGE-3", "分页缓存三", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-CACHE-PAGE-3", "D-CACHE-PAGE-3", "P-CACHE-PAGE-3", "S1", new DateTime(2026, 6, 2), 99, 198m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var widerPage = await service.GetBestSellersAsync(dateRange, null, pageIndex: 1, pageSize: 2);

        Assert.Equal("P-CACHE-PAGE-1", Assert.Single(firstPage.Products).ProductCode);
        Assert.Equal(2, widerPage.Products.Count);
        Assert.Equal(3, widerPage.Total);
        Assert.Equal("P-CACHE-PAGE-3", widerPage.Products[0].ProductCode);
    }

    [Fact]
    public void GetBestSellersAsync_热销商品缓存时间为30分钟()
    {
        var field = typeof(SalesDashboardReactService).GetField(
            "BEST_SELLERS_CACHE_DURATION",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.NotNull(field);
        Assert.Equal(TimeSpan.FromMinutes(30), field!.GetValue(null));
    }

    [Fact]
    public async Task GetBestSellersAsync_优先读取商品统计表并返回毛利和分店明细()
    {
        await SeedProductAsync("P-STAT", "ITEM-STAT", "BAR-HB", "统计商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 2);
        await SeedStoreAsync("S1", "Store 1");
        await SeedStoreAsync("S2", "Store 2");
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-STAT",
            ProductName = "POSM 统计商品",
            Barcode = "BAR-POSM",
            TotalQuantity = 4,
            TotalAmount = 20m,
            OrderCount = 1,
            UnitCostSnapshot = 2m,
            TotalCost = 8m,
            GrossProfit = 12m,
            GrossMarginRate = 0.6m,
            CostSource = "StoreRetailPrice",
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S2",
            SupplierCode = "200",
            ProductCode = "P-STAT",
            ProductName = "POSM 统计商品",
            Barcode = "BAR-POSM",
            TotalQuantity = 6,
            TotalAmount = 30m,
            OrderCount = 1,
            UnitCostSnapshot = 2m,
            TotalCost = 12m,
            GrossProfit = 18m,
            GrossMarginRate = 0.6m,
            CostSource = "StoreRetailPrice",
        }).ExecuteCommandAsync();
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Fresh);

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 1).AddDays(1).AddTicks(-1) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, result.StatisticStatus);
        Assert.Equal("P-STAT", product.ProductCode);
        Assert.Equal("BAR-HB", product.Barcode);
        Assert.Equal(10, product.Quantity);
        Assert.Equal(50m, product.SalesAmount);
        Assert.Equal(20m, product.TotalCost);
        Assert.Equal(30m, product.GrossProfit);
        Assert.Equal(0.6m, product.GrossMarginRate);
        Assert.Equal(2, product.BranchSalesCount);
        Assert.Collection(
            product.BranchSales,
            row =>
            {
                Assert.Equal("S2", row.BranchCode);
                Assert.Equal(6, row.Quantity);
                Assert.Equal(30m, row.SalesAmount);
                Assert.Equal(18m, row.GrossProfit);
            },
            row =>
            {
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal(4, row.Quantity);
                Assert.Equal(20m, row.SalesAmount);
                Assert.Equal(12m, row.GrossProfit);
            }
        );
    }

    [Fact]
    public async Task GetBestSellersAsync_Fresh空统计结果不缓存()
    {
        var dateRange = new DateRangeDto { StartDate = new DateTime(2026, 6, 9), EndDate = new DateTime(2026, 6, 9) };
        await SeedStatisticStateAsync(new DateTime(2026, 6, 9), SalesStatisticRefreshStatus.Fresh);
        var service = CreateService();

        var first = await service.GetBestSellersAsync(dateRange, null, pageIndex: 1, pageSize: 50);

        Assert.Empty(first.Products);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, first.StatisticStatus);

        await SeedProductAsync("P-FRESH-LATE", "ITEM-FRESH-LATE", "BAR-FRESH-LATE", "后补统计商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 9),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-FRESH-LATE",
            ProductName = "后补统计商品",
            Barcode = "BAR-FRESH-LATE",
            TotalQuantity = 11,
            TotalAmount = 22m,
            OrderCount = 1,
        }).ExecuteCommandAsync();

        var second = await service.GetBestSellersAsync(dateRange, null, pageIndex: 1, pageSize: 50);

        var product = Assert.Single(second.Products);
        Assert.Equal("P-FRESH-LATE", product.ProductCode);
        Assert.Equal(11, product.Quantity);
    }

    [Fact]
    public async Task GetBestSellersAsync_POSM不可访问时仍只读统计表()
    {
        await SeedProductAsync("P-NO-POSM", "ITEM-NO-POSM", "BAR-NO-POSM", "无 POSM 统计商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 10),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-NO-POSM",
            ProductName = "无 POSM 统计商品",
            Barcode = "BAR-NO-POSM",
            TotalQuantity = 3,
            TotalAmount = 6m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedStatisticStateAsync(new DateTime(2026, 6, 10), SalesStatisticRefreshStatus.Fresh);
        await SeedStatisticStateAsync(new DateTime(2026, 6, 11), SalesStatisticRefreshStatus.Fresh);
        await SeedStatisticStateAsync(new DateTime(2026, 6, 12), SalesStatisticRefreshStatus.Failed, "对账失败");
        await SeedStatisticStateAsync(new DateTime(2026, 6, 13), SalesStatisticRefreshStatus.Pending);
        var service = CreateServiceWithBrokenPosm();

        var fresh = await service.GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 10), EndDate = new DateTime(2026, 6, 10) },
            null,
            pageIndex: 1,
            pageSize: 50
        );
        var freshEmpty = await service.GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 11), EndDate = new DateTime(2026, 6, 11) },
            null,
            pageIndex: 1,
            pageSize: 50
        );
        var failed = await service.GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 12), EndDate = new DateTime(2026, 6, 12) },
            null,
            pageIndex: 1,
            pageSize: 50
        );
        var pending = await service.GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 13), EndDate = new DateTime(2026, 6, 13) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        Assert.Equal("P-NO-POSM", Assert.Single(fresh.Products).ProductCode);
        Assert.Empty(freshEmpty.Products);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, freshEmpty.StatisticStatus);
        Assert.Empty(failed.Products);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, failed.StatisticStatus);
        Assert.Empty(pending.Products);
        Assert.Equal(SalesStatisticRefreshStatus.Pending, pending.StatisticStatus);
    }

    [Fact]
    public async Task GetBestSellersAsync_统计状态失败时直接返回空结果避免明细回退()
    {
        await SeedProductAsync("P-FALLBACK", "ITEM-FALLBACK", "BAR-FALLBACK", "回退商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-FALLBACK", "D-FALLBACK", "P-FALLBACK", "S1", new DateTime(2026, 6, 1), 8, 16m);
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Failed, "对账失败");

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        Assert.Empty(result.Products);
        Assert.Equal(0, result.Total);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, result.StatisticStatus);
        Assert.Contains("对账失败", result.StatisticMessage);
    }

    [Fact]
    public async Task GetBestSellersAsync_统计表包含结束日整天销售()
    {
        await SeedProductAsync("P-END-DATE", "ITEM-END-DATE", "BAR-END-DATE", "结束日商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-END-DATE", "D-END-DATE", "P-END-DATE", "S1", new DateTime(2026, 6, 8, 18, 30, 0), 5, 25m);
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 8));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("P-END-DATE", product.ProductCode);
        Assert.Equal(5, product.Quantity);
        Assert.Equal(25m, product.SalesAmount);
    }

    [Fact]
    public async Task GetBestSellersAsync_统计表分店过滤使用设备映射参与排名()
    {
        await SeedProductAsync("P-DEVICE-RANK", "ITEM-DEVICE-RANK", "BAR-DEVICE-RANK", "设备分店商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedStoreAsync("S1", "Store 1");
        await SeedDeviceAsync("DEVICE-S1", "S1");
        await SeedSaleAsync(
            "O-DEVICE-RANK",
            "D-DEVICE-RANK",
            "P-DEVICE-RANK",
            null,
            new DateTime(2026, 6, 1, 10, 0, 0),
            9,
            45m,
            deviceCode: "DEVICE-S1"
        );
        await SeedBestSellerStatisticsFromPosmAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 1));

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 1) },
            new List<string> { "S1" },
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("P-DEVICE-RANK", product.ProductCode);
        Assert.Equal(9, product.Quantity);
        Assert.Equal(1, product.BranchSalesCount);
        Assert.Equal("S1", Assert.Single(product.BranchSales).BranchCode);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_按分店进货价优先计算毛利并写入状态()
    {
        await SeedProductAsync("P-MARGIN", "ITEM-MARGIN", "BAR-MARGIN", "毛利商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await _localDb.Updateable<Product>()
            .SetColumns(p => p.PurchasePrice == 9m)
            .Where(p => p.ProductCode == "P-MARGIN")
            .ExecuteCommandAsync();
        await _localDb.Updateable<WarehouseProduct>()
            .SetColumns(p => p.ImportPrice == 8m)
            .Where(p => p.ProductCode == "P-MARGIN")
            .ExecuteCommandAsync();
        await _localDb.Insertable(new StoreRetailPrice
        {
            UUID = "srp-margin",
            StoreCode = "S1",
            ProductCode = "P-MARGIN",
            SupplierCode = "200",
            PurchasePrice = 2m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedSaleAsync("O-MARGIN", "D-MARGIN", "P-MARGIN", "S1", new DateTime(2026, 6, 1), 5, 25m);
        // 明确订单支付金额，验证毛利仍按生产支付口径计算。
        await _posmDb.Insertable(new PaymentDetail
        {
            PaymentGuid = "PAY-MARGIN",
            OrderGuid = "O-MARGIN",
            Amount = 25m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalQuantity = 5,
            TotalAmount = 25m,
            OrderCount = 1,
            CustomerCount = 1,
            AverageOrderValue = 25m,
        }).ExecuteCommandAsync();

        await CreateStatisticsJobService().UpdateProductStoreDailyStatistics(new DateTime(2026, 6, 1));

        var stat = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == new DateTime(2026, 6, 1) && x.ProductCode == "P-MARGIN")
            .FirstAsync();
        Assert.NotNull(stat);
        Assert.Equal(25m, stat.TotalAmount);
        Assert.Equal("StoreRetailPrice", stat.CostSource);
        Assert.Equal(2m, stat.UnitCostSnapshot);
        Assert.Equal(10m, stat.TotalCost);
        Assert.Equal(15m, stat.GrossProfit);
        Assert.Equal(0.6m, stat.GrossMarginRate);

        var state = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(x => x.StatisticType == SalesStatisticType.ProductStoreDaily && x.Date == new DateTime(2026, 6, 1))
            .FirstAsync();
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state.Status);
        Assert.Equal("POSM_LOCAL", state.SourceTimeZone);
        Assert.NotNull(state.LastAggregatedAtUtc);
    }

    [Fact]
    public async Task GetBestSellersAsync_商品统计重算中返回空结果避免读取旧统计()
    {
        await SeedProductAsync("P-QUEUED", "ITEM-QUEUED", "BAR-QUEUED", "重算商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-QUEUED", "D-QUEUED", "P-QUEUED", "S1", new DateTime(2026, 6, 1), 9, 18m);
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Queued);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-QUEUED",
            ProductName = "旧统计商品",
            Barcode = "BAR-QUEUED",
            TotalQuantity = 100,
            TotalAmount = 200m,
            OrderCount = 1,
            CostSource = "Missing",
            UpdateTime = DateTime.Now,
        }).ExecuteCommandAsync();

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 1) },
            null,
            pageIndex: 1,
            pageSize: 10
        );

        Assert.Empty(result.Products);
        Assert.Equal(0, result.Total);
        Assert.Equal(SalesStatisticRefreshStatus.Pending, result.StatisticStatus);
    }

    [Fact]
    public async Task GetProductStoreDailyStatisticStates_按日期状态筛选返回状态列表()
    {
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = new DateTime(2026, 6, 1),
            Status = SalesStatisticRefreshStatus.Fresh,
            LastSourceUploadTime = new DateTime(2026, 6, 1, 23, 0, 0),
            SourceTimeZone = "POSM_LOCAL",
            LastAggregatedAtUtc = new DateTime(2026, 6, 1, 14, 0, 0),
            LastCheckedAtUtc = new DateTime(2026, 6, 1, 14, 5, 0),
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = new DateTime(2026, 6, 2),
            Status = SalesStatisticRefreshStatus.Failed,
            SourceTimeZone = "POSM_LOCAL",
            ErrorMessage = "对账失败",
        }).ExecuteCommandAsync();

        var result = await CreateStatisticsController().GetProductStoreDailyStatisticStates(
            SalesStatisticType.ProductStoreDaily,
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 1),
            SalesStatisticRefreshStatus.Fresh
        );

        var ok = AssertOk(result);
        var data = ExtractAnonymousData<List<SalesStatisticRefreshStateListItemDto>>(ok.Value);
        var row = Assert.Single(data);
        Assert.Equal(new DateTime(2026, 6, 1), row.Date);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, row.Status);
        Assert.Equal(new DateTime(2026, 6, 1, 23, 0, 0), row.LastSourceUploadTime);
    }

    [Fact]
    public async Task GetProductStoreDailyStatisticStates_大小写不同的筛选值仍能匹配()
    {
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = new DateTime(2026, 6, 1),
            Status = SalesStatisticRefreshStatus.Fresh,
        }).ExecuteCommandAsync();

        var result = await CreateStatisticsController().GetProductStoreDailyStatisticStates(
            "productstoredaily",
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 1),
            "fresh"
        );

        var ok = AssertOk(result);
        var data = ExtractAnonymousData<List<SalesStatisticRefreshStateListItemDto>>(ok.Value);
        var row = Assert.Single(data);
        Assert.Equal(SalesStatisticType.ProductStoreDaily, row.StatisticType);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, row.Status);
    }

    [Fact]
    public async Task GetProductStoreDailyStatisticSummary_返回单日汇总和对账状态()
    {
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Fresh);
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalQuantity = 3,
            TotalAmount = 15m,
            OrderCount = 2,
            CustomerCount = 2,
            AverageOrderValue = 15m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S2",
            BranchName = "Store 2",
            TotalQuantity = 5,
            TotalAmount = 25m,
            OrderCount = 2,
            CustomerCount = 2,
            AverageOrderValue = 12.5m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-SUM-1",
            TotalQuantity = 3,
            TotalAmount = 15m,
            GrossProfit = 6m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S2",
            SupplierCode = "200",
            ProductCode = "P-SUM-2",
            TotalQuantity = 5,
            TotalAmount = 25m,
            GrossProfit = 10m,
        }).ExecuteCommandAsync();

        var result = await CreateStatisticsController()
            .GetProductStoreDailyStatisticSummary(new DateTime(2026, 6, 1));

        var ok = AssertOk(result);
        var data = ExtractAnonymousData<ProductStoreDailyStatisticSummaryDto>(ok.Value);
        Assert.Equal(2, data.RecordCount);
        Assert.Equal(8, data.TotalQuantity);
        Assert.Equal(40m, data.TotalAmount);
        Assert.Equal(16m, data.GrossProfit);
        Assert.Equal("Passed", data.ReconciliationStatus);
        Assert.Equal("Passed", data.SalesReconciliationStatus);
        Assert.Equal(40m, data.ProductTotalAmount);
        Assert.Equal(40m, data.StoreTotalAmount);
        Assert.Equal(0m, data.AmountDifference);
        Assert.Equal(8, data.ProductTotalQuantity);
        Assert.Equal(8, data.StoreTotalQuantity);
        Assert.Equal(0, data.QuantityDifference);
    }

    [Fact]
    public async Task GetProductStoreDailyStatisticSummary_返回营业额差异和空供应商诊断()
    {
        await SeedStatisticStateAsync(
            new DateTime(2026, 6, 1),
            SalesStatisticRefreshStatus.Failed,
            "商品统计与分店营业额统计不一致: 2026-06-01 S1, 商品金额 30, 分店营业额 160, 金额差 130, 未匹配供应商金额 15.5, 未匹配供应商数量 3, 未匹配商品数 2"
        );
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalQuantity = 7,
            TotalAmount = 160m,
            OrderCount = 2,
            CustomerCount = 2,
            AverageOrderValue = 80m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-DIFF-1",
            TotalQuantity = 4,
            TotalAmount = 30m,
            GrossProfit = 6m,
        }).ExecuteCommandAsync();

        var result = await CreateStatisticsController()
            .GetProductStoreDailyStatisticSummary(new DateTime(2026, 6, 1));

        var ok = AssertOk(result);
        var data = ExtractAnonymousData<ProductStoreDailyStatisticSummaryDto>(ok.Value);
        Assert.Equal("Failed", data.ReconciliationStatus);
        Assert.Equal("Failed", data.SalesReconciliationStatus);
        Assert.Equal(30m, data.ProductTotalAmount);
        Assert.Equal(160m, data.StoreTotalAmount);
        Assert.Equal(130m, data.AmountDifference);
        Assert.Equal(4, data.ProductTotalQuantity);
        Assert.Equal(7, data.StoreTotalQuantity);
        Assert.Equal(3, data.QuantityDifference);
        Assert.Equal(15.5m, data.UnmatchedSupplierAmount);
        Assert.Equal(3, data.UnmatchedSupplierQuantity);
        Assert.Equal(2, data.UnmatchedSupplierProductCount);
    }

    [Fact]
    public async Task GetProductStoreDailyStatisticSummary_分店金额差异在绝对容差内时营业额对账Passed()
    {
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Fresh);
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalQuantity = 10,
            TotalAmount = 1000m,
            OrderCount = 1,
            CustomerCount = 1,
            AverageOrderValue = 1000m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-TOLERANCE-1",
            TotalQuantity = 10,
            TotalAmount = 920m,
        }).ExecuteCommandAsync();

        var result = await CreateStatisticsController()
            .GetProductStoreDailyStatisticSummary(new DateTime(2026, 6, 1));

        var ok = AssertOk(result);
        var data = ExtractAnonymousData<ProductStoreDailyStatisticSummaryDto>(ok.Value);
        Assert.Equal(920m, data.ProductTotalAmount);
        Assert.Equal(1000m, data.StoreTotalAmount);
        Assert.Equal(80m, data.AmountDifference);
        Assert.Equal("Passed", data.SalesReconciliationStatus);
    }

    [Fact]
    public async Task GetProductStoreDailyStatisticSummary_分店差异互相抵消时营业额对账仍Failed()
    {
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Failed);
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalQuantity = 10,
            TotalAmount = 100m,
            OrderCount = 1,
            CustomerCount = 1,
            AverageOrderValue = 100m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S2",
            BranchName = "Store 2",
            TotalQuantity = 10,
            TotalAmount = 400m,
            OrderCount = 1,
            CustomerCount = 1,
            AverageOrderValue = 400m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-OFFSET-1",
            TotalQuantity = 5,
            TotalAmount = 250m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S2",
            SupplierCode = "200",
            ProductCode = "P-OFFSET-2",
            TotalQuantity = 5,
            TotalAmount = 250m,
        }).ExecuteCommandAsync();

        var result = await CreateStatisticsController()
            .GetProductStoreDailyStatisticSummary(new DateTime(2026, 6, 1));

        var ok = AssertOk(result);
        var data = ExtractAnonymousData<ProductStoreDailyStatisticSummaryDto>(ok.Value);
        Assert.Equal(500m, data.ProductTotalAmount);
        Assert.Equal(500m, data.StoreTotalAmount);
        Assert.Equal(300m, data.AmountDifference);
        Assert.Equal("Failed", data.SalesReconciliationStatus);
    }

    [Fact]
    public async Task GetProductStoreDailyStatisticSummary_非Fresh状态有旧统计行时不显示对账通过()
    {
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Stale);
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            BranchName = "Store 1",
            TotalQuantity = 3,
            TotalAmount = 15m,
            OrderCount = 1,
            CustomerCount = 1,
            AverageOrderValue = 15m,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = new DateTime(2026, 6, 1),
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = "P-STALE-1",
            TotalQuantity = 3,
            TotalAmount = 15m,
            GrossProfit = 6m,
        }).ExecuteCommandAsync();

        var result = await CreateStatisticsController()
            .GetProductStoreDailyStatisticSummary(new DateTime(2026, 6, 1));

        var ok = AssertOk(result);
        var data = ExtractAnonymousData<ProductStoreDailyStatisticSummaryDto>(ok.Value);
        Assert.Equal(SalesStatisticRefreshStatus.Stale, data.Status);
        Assert.Equal("Pending", data.ReconciliationStatus);
        Assert.Equal("Pending", data.SalesReconciliationStatus);
    }

    [Fact]
    public async Task BatchProductStoreDailyStatistics_超过范围上限返回BadRequest()
    {
        var result = await CreateStatisticsController().BatchProductStoreDailyStatistics(
            new BatchProductStoreDailyUpdateRequest
            {
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 2, 15),
            }
        );

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("一次最多重算", badRequest.Value?.ToString());
    }

    [Fact]
    public void BatchProductStoreDailyUpdateRequest_MaxConcurrency默认值为3()
    {
        var request = new BatchProductStoreDailyUpdateRequest();

        Assert.Equal(3, request.MaxConcurrency);
    }

    [Fact]
    public async Task TriggerProductStoreDailyStatistics_提交后立即返回排队日期()
    {
        var cacheWarmerMock = new Mock<ISalesDashboardCacheWarmer>();
        var result = await CreateStatisticsController(cacheWarmerMock.Object).TriggerProductStoreDailyStatistics(
            new ProductStoreDailyJobTriggerRequest
            {
                Date = new DateTime(2026, 6, 1),
            }
        );

        var ok = AssertOk(result);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, ReadAnonymousProperty<string>(ok.Value, "status"));
        Assert.Contains("2026-06-01", ReadAnonymousProperty<List<string>>(ok.Value, "submittedDates"));
        cacheWarmerMock.Verify(x => x.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task BatchProductStoreDailyStatistics_执行中日期不重复提交()
    {
        await SeedStatisticStateAsync(new DateTime(2026, 6, 1), SalesStatisticRefreshStatus.Queued);

        var cacheWarmerMock = new Mock<ISalesDashboardCacheWarmer>();
        var result = await CreateStatisticsController(cacheWarmerMock.Object).BatchProductStoreDailyStatistics(
            new BatchProductStoreDailyUpdateRequest
            {
                StartDate = new DateTime(2026, 6, 1),
                EndDate = new DateTime(2026, 6, 1),
            }
        );

        var ok = AssertOk(result);
        Assert.Empty(ReadAnonymousProperty<List<string>>(ok.Value, "submittedDates"));
        Assert.Contains("2026-06-01", ReadAnonymousProperty<List<string>>(ok.Value, "skippedDates"));
        cacheWarmerMock.Verify(x => x.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task BatchProductStoreDailyStatistics_允许指定MaxConcurrency并返回排队日期()
    {
        var cacheWarmerMock = new Mock<ISalesDashboardCacheWarmer>();
        var result = await CreateStatisticsController(cacheWarmerMock.Object).BatchProductStoreDailyStatistics(
            new BatchProductStoreDailyUpdateRequest
            {
                StartDate = new DateTime(2026, 6, 2),
                EndDate = new DateTime(2026, 6, 2),
                MaxConcurrency = 4,
            }
        );

        var ok = AssertOk(result);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, ReadAnonymousProperty<string>(ok.Value, "status"));
        Assert.Contains("2026-06-02", ReadAnonymousProperty<List<string>>(ok.Value, "submittedDates"));
        cacheWarmerMock.Verify(x => x.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task SubmitProductStoreDailyRecalculationAsync_并发提交同一天只提交一次()
    {
        var service = CreateStatisticsJobService();

        var results = await Task.WhenAll(
            service.SubmitProductStoreDailyRecalculationAsync(new[] { new DateTime(2026, 6, 1) }, "admin-a"),
            service.SubmitProductStoreDailyRecalculationAsync(new[] { new DateTime(2026, 6, 1) }, "admin-b")
        );

        Assert.Equal(1, results.Sum(result => result.SubmittedDates.Count));
        Assert.Equal(1, results.Sum(result => result.SkippedDates.Count));
        var state = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date == new DateTime(2026, 6, 1))
            .FirstAsync();
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, state.Status);
    }

    [Fact]
    public async Task UpdateStoreStatistics_分店销量使用明细数量而不是订单头ItemCount()
    {
        await SeedStoreAsync("S1", "Store 1");
        await SeedPosmOrderAsync(
            "O-STORE-QTY",
            "S1",
            new DateTime(2026, 6, 1),
            itemCount: 1,
            details: new[]
            {
                ("D-STORE-QTY-1", "P-STORE-QTY-1", 2, 20m),
                ("D-STORE-QTY-2", "P-STORE-QTY-2", 3, 30m),
            },
            payments: new[] { ("PAY-STORE-QTY-1", 50m) }
        );

        await CreateStatisticsJobService().UpdateStoreStatistics(new DateTime(2026, 6, 1));

        var stat = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(s => s.Date == new DateTime(2026, 6, 1) && s.BranchCode == "S1")
            .FirstAsync();
        Assert.NotNull(stat);
        Assert.Equal(5, stat.TotalQuantity);
        Assert.Equal(50m, stat.TotalAmount);
        Assert.Equal(1, stat.OrderCount);
        Assert.Equal(1, stat.CustomerCount);
    }

    [Fact]
    public async Task UpdateStoreStatistics_多支付明细不重复放大销量和订单数()
    {
        await SeedStoreAsync("S1", "Store 1");
        await SeedPosmOrderAsync(
            "O-MULTI-PAY",
            "S1",
            new DateTime(2026, 6, 1),
            itemCount: 1,
            details: new[] { ("D-MULTI-PAY-1", "P-MULTI-PAY-1", 4, 40m) },
            payments: new[]
            {
                ("PAY-MULTI-PAY-1", 25m),
                ("PAY-MULTI-PAY-2", 15m),
            }
        );

        await CreateStatisticsJobService().UpdateStoreStatistics(new DateTime(2026, 6, 1));

        var stat = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(s => s.Date == new DateTime(2026, 6, 1) && s.BranchCode == "S1")
            .FirstAsync();
        Assert.NotNull(stat);
        Assert.Equal(4, stat.TotalQuantity);
        Assert.Equal(40m, stat.TotalAmount);
        Assert.Equal(1, stat.OrderCount);
        Assert.Equal(1, stat.CustomerCount);
    }

    [Fact]
    public async Task UpdateStoreStatistics_空订单分店时使用设备分店并支持分店过滤()
    {
        await SeedStoreAsync("S2", "Store 2");
        await SeedDeviceAsync("POS-S2", "S2");
        await SeedPosmOrderAsync(
            "O-DEVICE-STORE-STAT",
            null,
            new DateTime(2026, 6, 1),
            itemCount: 1,
            details: new[] { ("D-DEVICE-STORE-STAT-1", "P-DEVICE-STORE-STAT-1", 6, 60m) },
            payments: new[] { ("PAY-DEVICE-STORE-STAT-1", 60m) },
            deviceCode: "POS-S2"
        );

        await CreateStatisticsJobService().UpdateStoreStatistics(
            new DateTime(2026, 6, 1),
            new List<string> { "S2" }
        );

        var stat = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(s => s.Date == new DateTime(2026, 6, 1) && s.BranchCode == "S2")
            .FirstAsync();
        Assert.NotNull(stat);
        Assert.Equal("Store 2", stat.BranchName);
        Assert.Equal(6, stat.TotalQuantity);
        Assert.Equal(60m, stat.TotalAmount);
    }

    [Fact]
    public async Task GetBestSellers_普通用户传入请求分店时仍查询全平台热销榜()
    {
        List<string>? capturedBranchCodes = null;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(x => x.GetBestSellersAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>(),
                It.IsAny<int>(),
                It.IsAny<int>()
            ))
            .Callback<DateRangeDto, List<string>?, int, int>((_, branchCodes, _, _) => capturedBranchCodes = branchCodes)
            .ReturnsAsync(new BestSellerResponseDto());

        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = new List<UserStoreDto>
                {
                    new() { StoreCode = "S1" },
                    new() { StoreCode = "S3" },
                },
            }));

        var controller = CreateController(serviceMock.Object, userServiceMock.Object);

        var response = await controller.GetBestSellers(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 8),
            new List<string> { "S1", "S2" },
            pageIndex: 1,
            pageSize: 50
        );

        Assert.IsType<OkObjectResult>(response);
        Assert.Null(capturedBranchCodes);
    }

    [Fact]
    public async Task GetBestSellers_普通用户请求无权限分店时仍查询全平台热销榜()
    {
        List<string>? capturedBranchCodes = new List<string> { "unexpected" };
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(x => x.GetBestSellersAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>(),
                It.IsAny<int>(),
                It.IsAny<int>()
            ))
            .Callback<DateRangeDto, List<string>?, int, int>((_, branchCodes, _, _) => capturedBranchCodes = branchCodes)
            .ReturnsAsync(new BestSellerResponseDto());

        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = new List<UserStoreDto>
                {
                    new() { StoreCode = "S3" },
                },
            }));

        var controller = CreateController(serviceMock.Object, userServiceMock.Object);

        var response = await controller.GetBestSellers(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 8),
            new List<string> { "S1", "S2" },
            pageIndex: 2,
            pageSize: 50
        );

        serviceMock.Verify(
            x => x.GetBestSellersAsync(It.IsAny<DateRangeDto>(), It.IsAny<List<string>?>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Once
        );
        Assert.IsType<OkObjectResult>(response);
        Assert.Null(capturedBranchCodes);
    }

    [Fact]
    public async Task GetBestSellers_普通用户没有关联分店时仍查询全平台热销榜()
    {
        List<string>? capturedBranchCodes = new List<string> { "unexpected" };
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(x => x.GetBestSellersAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>(),
                It.IsAny<int>(),
                It.IsAny<int>()
            ))
            .Callback<DateRangeDto, List<string>?, int, int>((_, branchCodes, _, _) => capturedBranchCodes = branchCodes)
            .ReturnsAsync(new BestSellerResponseDto());

        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = new List<UserStoreDto>(),
            }));

        var controller = CreateController(serviceMock.Object, userServiceMock.Object);

        var response = await controller.GetBestSellers(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 8),
            branchCodes: null,
            pageIndex: 1,
            pageSize: 50
        );

        serviceMock.Verify(
            x => x.GetBestSellersAsync(It.IsAny<DateRangeDto>(), It.IsAny<List<string>?>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Once
        );
        Assert.IsType<OkObjectResult>(response);
        Assert.Null(capturedBranchCodes);
    }

    private async Task SeedProductAsync(
        string productCode,
        string itemNumber,
        string? barcode,
        string name,
        bool productIsActive,
        bool warehouseIsActive,
        int minOrderQuantity,
        string? localSupplierCode = "200"
    )
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"{productCode}-uuid",
            ProductCode = productCode,
            ItemNumber = itemNumber,
            Barcode = barcode,
            ProductName = name,
            ProductImage = $"{productCode}.jpg",
            LocalSupplierCode = localSupplierCode,
            IsActive = productIsActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            IsActive = warehouseIsActive,
            MinOrderQuantity = minOrderQuantity,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeCode, string storeName)
    {
        await _localDb.Insertable(new Store
        {
            StoreGUID = $"{storeCode}-guid",
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDeviceAsync(string deviceCode, string branchCode)
    {
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            系统设备编号 = deviceCode,
            设备硬件识别码 = $"{deviceCode}-hardware",
            分店代码 = branchCode,
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = 1,
            设备授权码 = $"{deviceCode}-auth",
        }).ExecuteCommandAsync();
    }

    private async Task SeedStatisticStateAsync(
        DateTime date,
        string status,
        string? errorMessage = null
    )
    {
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = date.Date,
            Status = status,
            SourceTimeZone = "POSM_LOCAL",
            LastAggregatedAtUtc = DateTime.UtcNow,
            LastCheckedAtUtc = DateTime.UtcNow,
            ErrorMessage = errorMessage,
        }).ExecuteCommandAsync();
    }

    private async Task SeedBestSellerStatisticsFromPosmAsync(DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        var endExclusive = end.AddDays(1);

        await _localDb.Deleteable<ProductStoreDailySalesStatistic>()
            .Where(s => s.Date >= start && s.Date <= end && s.SupplierCode == "200")
            .ExecuteCommandAsync();
        await _localDb.Deleteable<SalesStatisticRefreshState>()
            .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date >= start && s.Date <= end)
            .ExecuteCommandAsync();

        var salesRows = await _posmDb.Queryable<SalesOrderDetail>()
            .LeftJoin<SalesOrder>((d, o) => d.OrderGuid == o.OrderGuid)
            .Where((d, o) =>
                o.Status == 1
                && d.SupplierCode == "200"
                && o.OrderTime >= start
                && o.OrderTime < endExclusive
            )
            .Select((d, o) => new
            {
                d.OrderGuid,
                d.ProductCode,
                d.Barcode,
                d.ProductName,
                d.Quantity,
                d.ActualAmount,
                o.BranchCode,
                o.DeviceCode,
                o.OrderTime,
            })
            .ToListAsync();

        var deviceCodes = salesRows
            .Where(x => string.IsNullOrWhiteSpace(x.BranchCode) && !string.IsNullOrWhiteSpace(x.DeviceCode))
            .Select(x => x.DeviceCode!)
            .Distinct()
            .ToList();
        var deviceBranchMap = deviceCodes.Any()
            ? (await _posmDb.Queryable<POSM_设备注册信息表>()
                .Where(d => deviceCodes.Contains(d.系统设备编号))
                .Select(d => new { d.系统设备编号, d.分店代码 })
                .ToListAsync())
                .Where(x => !string.IsNullOrWhiteSpace(x.系统设备编号))
                .GroupBy(x => x.系统设备编号)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(row => row.分店代码).FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)) ?? string.Empty
                )
            : new Dictionary<string, string>();

        // 测试辅助：把 POSM fixture 转成统计表行，验证 Best Sellers 运行时不再读取 POSM。
        var statisticRows = salesRows
            .Where(x => x.OrderTime.HasValue && !string.IsNullOrWhiteSpace(x.ProductCode))
            .Select(x => new
            {
                Date = x.OrderTime!.Value.Date,
                BranchCode = ResolveTestBranchCode(x.BranchCode, x.DeviceCode, deviceBranchMap),
                x.OrderGuid,
                x.ProductCode,
                x.Barcode,
                x.ProductName,
                Quantity = x.Quantity ?? 0,
                ActualAmount = x.ActualAmount ?? 0m,
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
            .GroupBy(x => new { x.Date, x.BranchCode, x.ProductCode })
            .Select(group => new ProductStoreDailySalesStatistic
            {
                Date = group.Key.Date,
                BranchCode = group.Key.BranchCode,
                SupplierCode = "200",
                ProductCode = group.Key.ProductCode,
                ProductName = group.Select(x => x.ProductName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                Barcode = group.Select(x => x.Barcode).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                TotalQuantity = group.Sum(x => x.Quantity),
                TotalAmount = group.Sum(x => x.ActualAmount),
                OrderCount = group.Select(x => x.OrderGuid).Distinct().Count(),
                UpdateTime = DateTime.Now,
            })
            .ToList();

        if (statisticRows.Any())
        {
            await _localDb.Insertable(statisticRows).ExecuteCommandAsync();
        }

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            await SeedStatisticStateAsync(date, SalesStatisticRefreshStatus.Fresh);
        }
    }

    private static string ResolveTestBranchCode(
        string? branchCode,
        string? deviceCode,
        Dictionary<string, string> deviceBranchMap
    )
    {
        if (!string.IsNullOrWhiteSpace(branchCode))
            return branchCode;

        return !string.IsNullOrWhiteSpace(deviceCode) && deviceBranchMap.TryGetValue(deviceCode, out var mappedBranch)
            ? mappedBranch
            : string.Empty;
    }

    private async Task SeedSaleAsync(
        string orderGuid,
        string detailGuid,
        string productCode,
        string? branchCode,
        DateTime orderTime,
        int quantity,
        decimal actualAmount,
        string? barcode = null,
        string? productName = null,
        string? deviceCode = null
    )
    {
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = orderGuid,
            BranchCode = branchCode,
            DeviceCode = deviceCode,
            OrderTime = orderTime,
            Status = 1,
        }).ExecuteCommandAsync();

        await _posmDb.Insertable(new SalesOrderDetail
        {
            OrderDetailGuid = detailGuid,
            OrderGuid = orderGuid,
            ProductCode = productCode,
            SupplierCode = "200",
            Barcode = barcode,
            ProductName = productName,
            Quantity = quantity,
            ActualAmount = actualAmount,
        }).ExecuteCommandAsync();
    }

    private async Task SeedPosmOrderAsync(
        string orderGuid,
        string? branchCode,
        DateTime orderTime,
        int itemCount,
        IEnumerable<(string DetailGuid, string ProductCode, int Quantity, decimal ActualAmount)> details,
        IEnumerable<(string PaymentGuid, decimal Amount)> payments,
        string? deviceCode = null
    )
    {
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = orderGuid,
            BranchCode = branchCode,
            DeviceCode = deviceCode,
            OrderTime = orderTime,
            Status = 1,
            ItemCount = itemCount,
        }).ExecuteCommandAsync();

        foreach (var detail in details)
        {
            await _posmDb.Insertable(new SalesOrderDetail
            {
                OrderDetailGuid = detail.DetailGuid,
                OrderGuid = orderGuid,
                ProductCode = detail.ProductCode,
                SupplierCode = "200",
                Quantity = detail.Quantity,
                ActualAmount = detail.ActualAmount,
            }).ExecuteCommandAsync();
        }

        foreach (var payment in payments)
        {
            await _posmDb.Insertable(new PaymentDetail
            {
                PaymentGuid = payment.PaymentGuid,
                OrderGuid = orderGuid,
                Amount = payment.Amount,
            }).ExecuteCommandAsync();
        }
    }

    private SalesDashboardReactService CreateService()
    {
        return new SalesDashboardReactService(
            CreateSqlSugarContext(_localDb),
            CreatePosmSqlSugarContext(_posmDb),
            Mock.Of<IMapper>(),
            NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions())
        );
    }

    private SalesDashboardReactService CreateServiceWithBrokenPosm()
    {
        var brokenPosmDb = new SqlSugarClient(CreateConnectionConfig("Data Source=/dev/null/hbposm-forbidden.db"));
        return new SalesDashboardReactService(
            CreateSqlSugarContext(_localDb),
            CreatePosmSqlSugarContext(brokenPosmDb),
            Mock.Of<IMapper>(),
            NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions())
        );
    }

    private SalesStatisticsJobService CreateStatisticsJobService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ScheduledTasks:MaxConcurrentUpdates"] = "2",
                ["ScheduledTasks:MaxDaysForConcurrentUpdate"] = "30",
                ["ScheduledTasks:MaxDaysPerChunk"] = "7",
            })
            .Build();

        return new SalesStatisticsJobService(
            CreatePosmSqlSugarContext(_posmDb),
            CreateSqlSugarContext(_localDb),
            NullLogger<SalesStatisticsJobService>.Instance,
            configuration,
            Mock.Of<IServiceScopeFactory>()
        );
    }

    private StatisticsJobTriggerController CreateStatisticsController(
        ISalesDashboardCacheWarmer? cacheWarmer = null
    )
    {
        var context = CreateSqlSugarContext(_localDb);
        return new StatisticsJobTriggerController(
            CreateStatisticsJobService(),
            new ScheduledTaskLogService(context, NullLogger<ScheduledTaskLogService>.Instance),
            context,
            NullLogger<StatisticsJobTriggerController>.Instance,
            cacheWarmer ?? Mock.Of<ISalesDashboardCacheWarmer>(),
            CreateAlignmentService(context),
            new SalesStatisticsAlignmentBackgroundRecalculateService(
                new ScheduledTaskLogService(context, NullLogger<ScheduledTaskLogService>.Instance),
                Mock.Of<IServiceScopeFactory>(),
                NullLogger<SalesStatisticsAlignmentBackgroundRecalculateService>.Instance
            )
        );
    }

    private SalesStatisticsAlignmentService CreateAlignmentService(SqlSugarContext context)
    {
        return new SalesStatisticsAlignmentService(
            context,
            CreatePosmSqlSugarContext(_posmDb),
            new ScheduledTaskLeaseService(
                context,
                Options.Create(new ScheduledTaskOptions { InstanceId = "test-api" }),
                NullLogger<ScheduledTaskLeaseService>.Instance
            ),
            Mock.Of<IServiceScopeFactory>(),
            NullLogger<SalesStatisticsAlignmentService>.Instance
        );
    }

    private static T ExtractAnonymousData<T>(object? value)
    {
        Assert.NotNull(value);
        var dataProperty = value!.GetType().GetProperty("data");
        Assert.NotNull(dataProperty);
        var data = dataProperty!.GetValue(value);
        return Assert.IsType<T>(data);
    }

    private static T ReadAnonymousProperty<T>(object? value, string propertyName)
    {
        Assert.NotNull(value);
        var property = value!.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        var propertyValue = property!.GetValue(value);
        return Assert.IsType<T>(propertyValue);
    }

    private static OkObjectResult AssertOk(IActionResult result)
    {
        if (result is OkObjectResult ok)
        {
            return ok;
        }

        if (result is ObjectResult objectResult)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected OkObjectResult, got {result.GetType().Name}: {objectResult.Value}"
            );
        }

        throw new Xunit.Sdk.XunitException($"Expected OkObjectResult, got {result.GetType().Name}");
    }

    private static SalesDashboardController CreateController(
        ISalesDashboardReactService service,
        IUserService userService
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
            },
            "TestAuth"
        ));

        var controller = new SalesDashboardController(
            service,
            NullLogger<SalesDashboardController>.Instance,
            userService,
            Mock.Of<ISalesDashboardCacheWarmer>()
        );
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _localConnection.Dispose();
        _posmConnection.Dispose();
        if (File.Exists(_localDbPath)) SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        if (File.Exists(_posmDbPath)) SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
    }
}
