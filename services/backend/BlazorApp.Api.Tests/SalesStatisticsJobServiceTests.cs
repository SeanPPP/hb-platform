using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesStatisticsJobServiceTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public SalesStatisticsJobServiceTests()
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
            typeof(StoreRetailPrice),
            typeof(HBLocalSupplier),
            typeof(ChinaSupplier),
            typeof(Store),
            typeof(StoreSalesStatistic),
            typeof(DailySalesStatistic),
            typeof(HourlySalesStatistic),
            typeof(SupplierSalesStatistic),
            typeof(StoreSupplierSalesDetail),
            typeof(AustralianSupplierStoreSalesDetail),
            typeof(ProductStoreDailySalesStatistic),
            typeof(SalesStatisticRefreshState)
        );
        _posmDb.CodeFirst.InitTables(
            typeof(SalesOrder),
            typeof(SalesOrderDetail),
            typeof(PaymentDetail),
            typeof(PosmProductSupplierMapping),
            typeof(POSM_设备注册信息表)
        );
    }

    [Fact]
    public async Task UpdateHourlyStatistics_拆分支付时写入唯一订单数()
    {
        var targetDate = new DateTime(2026, 7, 4);
        await SeedStoreAsync("S1", "分店一");
        await SeedOrderAsync("ORDER-SPLIT", "S1", targetDate.AddHours(9), 1);
        await SeedSaleDetailAsync("ORDER-SPLIT", "DETAIL-1", "P-1", 2, 40m, null);
        await SeedSaleDetailAsync("ORDER-SPLIT", "DETAIL-2", "P-2", 3, 60m, null);
        await SeedPaymentAsync("PAY-1", "ORDER-SPLIT", 40m, targetDate.AddHours(9).AddMinutes(2));
        await SeedPaymentAsync("PAY-2", "ORDER-SPLIT", 60m, targetDate.AddHours(9).AddMinutes(3));

        await CreateService().UpdateHourlyStatistics(targetDate, 9);

        var branchRow = await _localDb.Queryable<HourlySalesStatistic>()
            .Where(row => row.Date == targetDate && row.Hour == 9 && row.BranchCode == "S1")
            .FirstAsync();
        var allRow = await _localDb.Queryable<HourlySalesStatistic>()
            .Where(row => row.Date == targetDate && row.Hour == 9 && row.BranchCode == "ALL")
            .FirstAsync();

        Assert.NotNull(branchRow);
        Assert.NotNull(allRow);
        Assert.Equal(1, branchRow!.OrderCount);
        Assert.Equal(1, allRow!.OrderCount);
        Assert.Equal(5, branchRow.TotalQuantity);
        Assert.Equal(5, allRow.TotalQuantity);
        Assert.Equal(100m, branchRow.TotalAmount);
        Assert.Equal(100m, allRow.TotalAmount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_供应商为空时应写入Unknown主表()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-UNMATCHED");
        await SeedSaleAsync(
            orderGuid: "ORDER-UNMATCHED",
            detailGuid: "DETAIL-UNMATCHED",
            productCode: "P-UNMATCHED",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 3,
            actualAmount: 12.34m,
            supplierCode: string.Empty
        );

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1018" && x.ProductCode == "P-UNMATCHED")
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal("UNKNOWN", row!.SupplierCode);
        Assert.Equal(12.34m, row.TotalAmount);
        Assert.Equal(3, row.TotalQuantity);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_商品统计与分店营业额统计一致时即使供应商统计不一致也应为Fresh()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-FRESH");
        await SeedSaleAsync(
            orderGuid: "ORDER-FRESH",
            detailGuid: "DETAIL-FRESH",
            productCode: "P-FRESH",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            quantity: 5,
            actualAmount: 100m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 100m, 5);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1018", "200", 23m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        var rowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.True(state != null, $"未生成状态行，商品统计行数={rowCount}");
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
        Assert.True(string.IsNullOrWhiteSpace(state.ErrorMessage));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_商品统计与分店营业额统计不一致时应标记Failed并写明原因()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-FAILED");
        await SeedSaleAsync(
            orderGuid: "ORDER-FAILED",
            detailGuid: "DETAIL-FAILED",
            productCode: "P-FAILED",
            branchCode: "1018",
            orderTime: targetDate.AddHours(11),
            quantity: 6,
            actualAmount: 88.63m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 220m, 6);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1018", "200", 88.63m, 6);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        var rowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.True(state != null, $"未生成状态行，商品统计行数={rowCount}");
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("商品统计与分店营业额统计不一致", state.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("2026-05-01", state.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("1018", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_营业额差异在容差内时应Fresh()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-TOLERANCE");
        await SeedSaleAsync(
            orderGuid: "ORDER-TOLERANCE",
            detailGuid: "DETAIL-TOLERANCE",
            productCode: "P-TOLERANCE",
            branchCode: "1004",
            orderTime: targetDate.AddHours(11),
            quantity: 1,
            actualAmount: 2153.38m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 2154.04m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
        Assert.True(string.IsNullOrWhiteSpace(state.ErrorMessage));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_营业额差异超过百分之一且超过100时仍应Failed()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-TOLERANCE-FAILED");
        await SeedSaleAsync(
            orderGuid: "ORDER-TOLERANCE-FAILED",
            detailGuid: "DETAIL-TOLERANCE-FAILED",
            productCode: "P-TOLERANCE-FAILED",
            branchCode: "1004",
            orderTime: targetDate.AddHours(12),
            quantity: 1,
            actualAmount: 2020m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 2154.04m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("金额差", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_分店营业额数量不一致但金额一致时不应Failed()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-QUANTITY");
        await SeedSaleAsync(
            orderGuid: "ORDER-QUANTITY",
            detailGuid: "DETAIL-QUANTITY",
            productCode: "P-QUANTITY",
            branchCode: "1018",
            orderTime: targetDate.AddHours(12),
            quantity: 4,
            actualAmount: 60m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 60m, 999);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1018", "200", 60m, 4);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        var rowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.True(state != null, $"未生成状态行，商品统计行数={rowCount}");
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_商品统计分店缺少营业额基准时应Failed()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-MISSING-STORE");
        await SeedSaleAsync(
            orderGuid: "ORDER-MISSING-STORE",
            detailGuid: "DETAIL-MISSING-STORE",
            productCode: "P-MISSING-STORE",
            branchCode: "1018",
            orderTime: targetDate.AddHours(13),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "200"
        );

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("分店营业额统计缺失", state.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("1018", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSupplierStatistics_映射为空时应回退明细供应商并合并Unknown()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedSaleAsync(
            orderGuid: "ORDER-SUPPLIER-FALLBACK",
            detailGuid: "DETAIL-SUPPLIER-FALLBACK",
            productCode: "P-SUPPLIER-FALLBACK",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 1,
            actualAmount: 2.50m,
            supplierCode: "112"
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-SUPPLIER-EMPTY-MAPPING",
            detailGuid: "DETAIL-SUPPLIER-EMPTY-MAPPING",
            productCode: "P-SUPPLIER-EMPTY-MAPPING",
            branchCode: "1004",
            orderTime: targetDate.AddHours(11),
            quantity: 2,
            actualAmount: 29.98m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-SUPPLIER-EMPTY-MAPPING", string.Empty, null);
        await SeedSaleAsync(
            orderGuid: "ORDER-SUPPLIER-UNKNOWN-MISSING",
            detailGuid: "DETAIL-SUPPLIER-UNKNOWN-MISSING",
            productCode: "P-SUPPLIER-UNKNOWN-MISSING",
            branchCode: "1004",
            orderTime: targetDate.AddHours(12),
            quantity: 1,
            actualAmount: 5m,
            supplierCode: string.Empty
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-SUPPLIER-UNKNOWN-EMPTY",
            detailGuid: "DETAIL-SUPPLIER-UNKNOWN-EMPTY",
            productCode: "P-SUPPLIER-UNKNOWN-EMPTY",
            branchCode: "1004",
            orderTime: targetDate.AddHours(13),
            quantity: 2,
            actualAmount: 8m,
            supplierCode: string.Empty
        );
        await SeedPosmProductSupplierMappingAsync("P-SUPPLIER-UNKNOWN-EMPTY", " ", null);

        await CreateService().UpdateSupplierStatistics(targetDate, targetDate);

        var rows = await _localDb.Queryable<SupplierSalesStatistic>()
            .Where(x => x.Date == targetDate && x.IsDomestic == false)
            .OrderBy(x => x.SupplierCode)
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.SupplierCode == "112" && row.TotalAmount == 2.50m && row.TotalQuantity == 1);
        Assert.Contains(rows, row => row.SupplierCode == "200" && row.TotalAmount == 29.98m && row.TotalQuantity == 2);
        Assert.Contains(rows, row =>
            row.SupplierCode == "UNKNOWN"
            && row.SupplierName == "未匹配供应商"
            && row.TotalAmount == 13m
            && row.TotalQuantity == 3
        );
    }

    [Fact]
    public async Task UpdateDetailStatistics_金额应按订单支付金额分摊()
    {
        var targetDate = new DateTime(2026, 6, 18);
        await SeedProductAsync("P-ALLOC-1");
        await SeedProductAsync("P-ALLOC-2");
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 72.14m, 3);
        await SeedOrderAsync("ORDER-ALLOC", "1004", targetDate.AddHours(10), 3);
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-ALLOC",
            detailGuid: "DETAIL-ALLOC-1",
            productCode: "P-ALLOC-1",
            quantity: 1,
            actualAmount: 30m,
            supplierCode: "112"
        );
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-ALLOC",
            detailGuid: "DETAIL-ALLOC-2",
            productCode: "P-ALLOC-2",
            quantity: 2,
            actualAmount: 40m,
            supplierCode: "113"
        );
        await SeedPaymentAsync("PAY-ALLOC", "ORDER-ALLOC", 72.14m, targetDate.AddHours(10).AddMinutes(1));

        var service = CreateService();
        await service.UpdateSupplierStatistics(targetDate, targetDate);
        await service.UpdateStoreSupplierStatistics(targetDate);
        await service.UpdateProductStoreDailyStatistics(targetDate);

        var supplierAmount = await _localDb.Queryable<SupplierSalesStatistic>()
            .Where(row => row.Date == targetDate && row.IsDomestic == false)
            .SumAsync(row => row.TotalAmount);
        var storeSupplierAmount = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(row => row.Date == targetDate)
            .SumAsync(row => row.TotalAmount);
        var productStoreAmount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .SumAsync(row => row.TotalAmount);

        Assert.InRange(Math.Abs(supplierAmount - 72.14m), 0m, 0.0001m);
        Assert.InRange(Math.Abs(storeSupplierAmount - 72.14m), 0m, 0.0001m);
        Assert.InRange(Math.Abs(productStoreAmount - 72.14m), 0m, 0.0001m);
    }

    [Fact]
    public async Task UpdateDetailStatistics_无支付记录时金额应按支付口径计零()
    {
        var targetDate = new DateTime(2026, 6, 18);
        await SeedProductAsync("P-NO-PAYMENT");
        await SeedOrderAsync("ORDER-NO-PAYMENT", "1004", targetDate.AddHours(10), 1);
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-NO-PAYMENT",
            detailGuid: "DETAIL-NO-PAYMENT",
            productCode: "P-NO-PAYMENT",
            quantity: 1,
            actualAmount: 30m,
            supplierCode: "112"
        );

        var service = CreateService();
        await service.UpdateSupplierStatistics(targetDate, targetDate);
        await service.UpdateStoreSupplierStatistics(targetDate);
        await service.UpdateProductStoreDailyStatistics(targetDate);

        var supplierAmount = await _localDb.Queryable<SupplierSalesStatistic>()
            .Where(row => row.Date == targetDate)
            .SumAsync(row => row.TotalAmount);
        var storeSupplierAmount = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(row => row.Date == targetDate)
            .SumAsync(row => row.TotalAmount);
        var productStoreAmount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .SumAsync(row => row.TotalAmount);

        Assert.Equal(0m, supplierAmount);
        Assert.Equal(0m, storeSupplierAmount);
        Assert.Equal(0m, productStoreAmount);
    }

    [Fact]
    public async Task UpdateSupplierStatistics_局部国内供应商刷新不应覆盖本地200总计()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedSupplierSalesStatisticAsync(targetDate, "200", 100m, 10);
        await SeedSaleAsync(
            orderGuid: "ORDER-SUPPLIER-CN01",
            detailGuid: "DETAIL-SUPPLIER-CN01",
            productCode: "P-SUPPLIER-CN01",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-SUPPLIER-CN01", "200", "CN-01");
        await SeedSaleAsync(
            orderGuid: "ORDER-SUPPLIER-CN02",
            detailGuid: "DETAIL-SUPPLIER-CN02",
            productCode: "P-SUPPLIER-CN02",
            branchCode: "1004",
            orderTime: targetDate.AddHours(11),
            quantity: 3,
            actualAmount: 70m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-SUPPLIER-CN02", "200", "CN-02");

        await CreateService().UpdateSupplierStatistics(
            targetDate,
            targetDate,
            new List<string> { "CN-01" }
        );

        var local200 = await _localDb.Queryable<SupplierSalesStatistic>()
            .Where(row => row.Date == targetDate && row.SupplierCode == "200")
            .FirstAsync();
        var cn01 = await _localDb.Queryable<SupplierSalesStatistic>()
            .Where(row => row.Date == targetDate && row.SupplierCode == "CN-01")
            .FirstAsync();

        Assert.NotNull(local200);
        Assert.False(local200!.IsDomestic);
        Assert.Equal(100m, local200.TotalAmount);
        Assert.Equal(10, local200.TotalQuantity);
        Assert.NotNull(cn01);
        Assert.True(cn01!.IsDomestic);
        Assert.Equal(30m, cn01.TotalAmount);
        Assert.Equal(2, cn01.TotalQuantity);
    }

    [Fact]
    public async Task UpdateSupplierStatistics_按本地200局部刷新应清理已无销售国内旧子项()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedSupplierSalesStatisticAsync(targetDate, "200", 100m, 10);
        await SeedSupplierSalesStatisticAsync(targetDate, "CN-02", 70m, 3, isDomestic: true);
        await SeedSaleAsync(
            orderGuid: "ORDER-SUPPLIER-LOCAL-200",
            detailGuid: "DETAIL-SUPPLIER-LOCAL-200",
            productCode: "P-SUPPLIER-LOCAL-200",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-SUPPLIER-LOCAL-200", "200", "CN-01");

        await CreateService().UpdateSupplierStatistics(
            targetDate,
            targetDate,
            new List<string> { "200" }
        );

        var rows = await _localDb.Queryable<SupplierSalesStatistic>()
            .Where(row => row.Date == targetDate)
            .OrderBy(row => row.SupplierCode)
            .ToListAsync();

        Assert.Contains(rows, row =>
            row.SupplierCode == "200"
            && row.IsDomestic == false
            && row.TotalAmount == 30m
            && row.TotalQuantity == 2
        );
        Assert.Contains(rows, row =>
            row.SupplierCode == "CN-01"
            && row.IsDomestic == true
            && row.TotalAmount == 30m
            && row.TotalQuantity == 2
        );
        Assert.DoesNotContain(rows, row => row.SupplierCode == "CN-02");
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_映射缺失或为空时应回退明细供应商并避免空供应商主键冲突()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedSaleAsync(
            orderGuid: "ORDER-MISSING-MAPPING",
            detailGuid: "DETAIL-MISSING-MAPPING",
            productCode: "P-MISSING-MAPPING",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 1,
            actualAmount: 2.50m,
            supplierCode: "112"
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-EMPTY-MAPPING",
            detailGuid: "DETAIL-EMPTY-MAPPING",
            productCode: "P-EMPTY-MAPPING",
            branchCode: "1004",
            orderTime: targetDate.AddHours(11),
            quantity: 2,
            actualAmount: 29.98m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-EMPTY-MAPPING", string.Empty, null);
        await SeedSaleAsync(
            orderGuid: "ORDER-UNKNOWN-MISSING",
            detailGuid: "DETAIL-UNKNOWN-MISSING",
            productCode: "P-UNKNOWN-MISSING",
            branchCode: "1004",
            orderTime: targetDate.AddHours(12),
            quantity: 1,
            actualAmount: 5m,
            supplierCode: string.Empty
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-UNKNOWN-EMPTY",
            detailGuid: "DETAIL-UNKNOWN-EMPTY",
            productCode: "P-UNKNOWN-EMPTY",
            branchCode: "1004",
            orderTime: targetDate.AddHours(13),
            quantity: 2,
            actualAmount: 8m,
            supplierCode: string.Empty
        );
        await SeedPosmProductSupplierMappingAsync("P-UNKNOWN-EMPTY", string.Empty, null);

        await CreateService().UpdateStoreSupplierStatistics(targetDate);

        var rows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .OrderBy(x => x.SupplierCode)
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, row => string.IsNullOrWhiteSpace(row.SupplierCode));
        Assert.Contains(rows, row => row.SupplierCode == "112" && row.TotalAmount == 2.50m);
        Assert.Contains(rows, row => row.SupplierCode == "200" && row.TotalAmount == 29.98m);
        Assert.Contains(rows, row => row.SupplierCode == "UNKNOWN" && row.TotalAmount == 13m);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_分店为空时应按设备注册信息回填分店()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedDeviceRegistrationAsync("DEVICE-1004", "1004");
        await SeedSaleAsync(
            orderGuid: "ORDER-DEVICE-BRANCH",
            detailGuid: "DETAIL-DEVICE-BRANCH",
            productCode: "P-DEVICE-BRANCH",
            branchCode: string.Empty,
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "112",
            deviceCode: "DEVICE-1004"
        );

        await CreateService().UpdateStoreSupplierStatistics(targetDate);

        var row = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004" && x.SupplierCode == "112")
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(30m, row!.TotalAmount);
        Assert.Equal(2, row.TotalQuantity);
        Assert.Equal(1, row.OrderCount);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_局部供应商重算不应删除其他供应商旧统计()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", "999", 88m, 3);
        await SeedSaleAsync(
            orderGuid: "ORDER-PARTIAL-112",
            detailGuid: "DETAIL-PARTIAL-112",
            productCode: "P-PARTIAL-112",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 1,
            actualAmount: 2.50m,
            supplierCode: "112"
        );

        await CreateService().UpdateStoreSupplierStatistics(targetDate, supplierCodes: new List<string> { "112" });

        var rows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .OrderBy(x => x.SupplierCode)
            .ToListAsync();

        Assert.Contains(rows, row => row.SupplierCode == "112" && row.TotalAmount == 2.50m);
        Assert.Contains(rows, row => row.SupplierCode == "999" && row.TotalAmount == 88m);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_局部重算无新数据时应只清理旧数据()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", "112", 88m, 3);

        await CreateService().UpdateStoreSupplierStatistics(
            targetDate,
            supplierCodes: new List<string> { "112" }
        );

        var rows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .ToListAsync();

        Assert.Empty(rows);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_国内供应商应支持按最终供应商编码过滤()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedSaleAsync(
            orderGuid: "ORDER-CHINA-SUPPLIER",
            detailGuid: "DETAIL-CHINA-SUPPLIER",
            productCode: "P-CHINA-SUPPLIER",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-CHINA-SUPPLIER", "200", "CN-01");

        await CreateService().UpdateStoreSupplierStatistics(targetDate, supplierCodes: new List<string> { "CN-01" });

        var row = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004" && x.SupplierCode == "CN-01")
            .FirstAsync();

        Assert.NotNull(row);
        Assert.True(row!.IsDomestic);
        Assert.Equal(30m, row.TotalAmount);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_同一订单多条明细合并到同一供应商时订单数应去重()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedSaleAsync(
            orderGuid: "ORDER-DISTINCT-COUNT",
            detailGuid: "DETAIL-DISTINCT-MAPPED",
            productCode: "P-DISTINCT-MAPPED",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 1,
            actualAmount: 10m,
            supplierCode: "112"
        );
        await SeedPosmProductSupplierMappingAsync("P-DISTINCT-MAPPED", "112", null);
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-DISTINCT-COUNT",
            detailGuid: "DETAIL-DISTINCT-FALLBACK",
            productCode: "P-DISTINCT-FALLBACK",
            quantity: 1,
            actualAmount: 20m,
            supplierCode: "112"
        );
        await SeedPaymentAsync("PAY-DISTINCT-FALLBACK", "ORDER-DISTINCT-COUNT", 20m, targetDate.AddHours(10).AddMinutes(2));

        await CreateService().UpdateStoreSupplierStatistics(targetDate);

        var row = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004" && x.SupplierCode == "112")
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(30m, row!.TotalAmount);
        Assert.Equal(2, row.TotalQuantity);
        Assert.Equal(1, row.OrderCount);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_按本地供应商200重算时应覆盖最终中国供应商旧统计()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", "CN-01", 9m, 1);
        await SeedSaleAsync(
            orderGuid: "ORDER-LOCAL-200-FILTER",
            detailGuid: "DETAIL-LOCAL-200-FILTER",
            productCode: "P-LOCAL-200-FILTER",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-LOCAL-200-FILTER", "200", "CN-01");

        await CreateService().UpdateStoreSupplierStatistics(targetDate, supplierCodes: new List<string> { "200" });

        var rows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004" && x.SupplierCode == "CN-01")
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(30m, rows[0].TotalAmount);
        Assert.Equal(2, rows[0].TotalQuantity);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_按本地供应商200重算时应清理已无销售的旧中国供应商统计()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", "CN-01", 9m, 1, true);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", "CN-02", 12m, 2, true);
        await SeedSaleAsync(
            orderGuid: "ORDER-LOCAL-200-STALE",
            detailGuid: "DETAIL-LOCAL-200-STALE",
            productCode: "P-LOCAL-200-STALE",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 30m,
            supplierCode: "200"
        );
        await SeedPosmProductSupplierMappingAsync("P-LOCAL-200-STALE", "200", "CN-01");

        await CreateService().UpdateStoreSupplierStatistics(targetDate, supplierCodes: new List<string> { "200" });

        var rows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .OrderBy(x => x.SupplierCode)
            .ToListAsync();

        Assert.Contains(rows, row => row.SupplierCode == "CN-01" && row.TotalAmount == 30m);
        Assert.DoesNotContain(rows, row => row.SupplierCode == "CN-02");
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_Unknown供应商应支持局部重算()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", "UNKNOWN", 1m, 1);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", string.Empty, 99m, 9);
        await SeedSaleAsync(
            orderGuid: "ORDER-UNKNOWN-FILTER",
            detailGuid: "DETAIL-UNKNOWN-FILTER",
            productCode: "P-UNKNOWN-FILTER",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 8m,
            supplierCode: string.Empty
        );
        await SeedPosmProductSupplierMappingAsync("P-UNKNOWN-FILTER", string.Empty, null);

        await CreateService().UpdateStoreSupplierStatistics(targetDate, supplierCodes: new List<string> { "UNKNOWN" });

        var rows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004" && x.SupplierCode == "UNKNOWN")
            .ToListAsync();
        var allRows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(8m, rows[0].TotalAmount);
        Assert.Equal(2, rows[0].TotalQuantity);
        Assert.DoesNotContain(allRows, row => string.IsNullOrWhiteSpace(row.SupplierCode));
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_Unknown供应商应支持空格供应商局部重算()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedStoreSupplierSalesDetailAsync(targetDate, "1004", "UNKNOWN", 1m, 1);
        await SeedSaleAsync(
            orderGuid: "ORDER-UNKNOWN-WHITESPACE",
            detailGuid: "DETAIL-UNKNOWN-WHITESPACE",
            productCode: "P-UNKNOWN-WHITESPACE",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 8m,
            supplierCode: " "
        );
        await SeedPosmProductSupplierMappingAsync("P-UNKNOWN-WHITESPACE", " ", null);

        await CreateService().UpdateStoreSupplierStatistics(targetDate, supplierCodes: new List<string> { "UNKNOWN" });

        var rows = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004" && x.SupplierCode == "UNKNOWN")
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(8m, rows[0].TotalAmount);
        Assert.Equal(2, rows[0].TotalQuantity);
    }

    [Fact]
    public async Task UpdateStoreSupplierStatistics_空订单号不应回退为明细行数()
    {
        var targetDate = new DateTime(2026, 6, 17);
        await SeedSaleAsync(
            orderGuid: string.Empty,
            detailGuid: "DETAIL-STORE-BLANK-ORDER",
            productCode: "P-STORE-BLANK-ORDER",
            branchCode: "1004",
            orderTime: targetDate.AddHours(10),
            quantity: 1,
            actualAmount: 6m,
            supplierCode: "112"
        );

        await CreateService().UpdateStoreSupplierStatistics(targetDate);

        var row = await _localDb.Queryable<StoreSupplierSalesDetail>()
            .Where(row =>
                row.Date == targetDate
                && row.BranchCode == "1004"
                && row.SupplierCode == "112"
            )
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(0m, row!.TotalAmount);
        Assert.Equal(1, row.TotalQuantity);
        Assert.Equal(0, row.OrderCount);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatistics_空映射供应商应合并到Unknown避免空主键冲突()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-UNKNOWN-MISSING",
            detailGuid: "DETAIL-AUS-UNKNOWN-MISSING",
            productCode: "P-AUS-UNKNOWN-MISSING",
            branchCode: "1007",
            orderTime: targetDate.AddHours(9),
            quantity: 1,
            actualAmount: 5m,
            supplierCode: string.Empty
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-UNKNOWN-EMPTY",
            detailGuid: "DETAIL-AUS-UNKNOWN-EMPTY",
            productCode: "P-AUS-UNKNOWN-EMPTY",
            branchCode: "1007",
            orderTime: targetDate.AddHours(10),
            quantity: 2,
            actualAmount: 8m,
            supplierCode: string.Empty
        );
        await SeedPosmProductSupplierMappingAsync("P-AUS-UNKNOWN-EMPTY", string.Empty, null);
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-UNKNOWN-WHITESPACE",
            detailGuid: "DETAIL-AUS-UNKNOWN-WHITESPACE",
            productCode: "P-AUS-UNKNOWN-WHITESPACE",
            branchCode: "1007",
            orderTime: targetDate.AddHours(11),
            quantity: 3,
            actualAmount: 12m,
            supplierCode: string.Empty
        );
        await SeedPosmProductSupplierMappingAsync("P-AUS-UNKNOWN-WHITESPACE", " ", null);

        await CreateService().UpdateAustralianSupplierStoreStatistics(targetDate);

        var rows = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1007")
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("UNKNOWN", row.SupplierCode);
        Assert.Equal("未匹配供应商", row.SupplierName);
        Assert.Equal(25m, row.TotalAmount);
        Assert.Equal(6, row.TotalQuantity);
        Assert.Equal(3, row.OrderCount);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatistics_映射为空时应回退明细供应商并按主键聚合订单数去重()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedOrderAsync("ORDER-AUS-FALLBACK", "1007", targetDate.AddHours(12), 99);
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-AUS-FALLBACK",
            detailGuid: "DETAIL-AUS-FALLBACK-1",
            productCode: "P-AUS-FALLBACK-1",
            quantity: 2,
            actualAmount: 9m,
            supplierCode: "112"
        );
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-AUS-FALLBACK",
            detailGuid: "DETAIL-AUS-FALLBACK-2",
            productCode: "P-AUS-FALLBACK-2",
            quantity: 3,
            actualAmount: 11m,
            supplierCode: "112"
        );
        await SeedPaymentAsync("PAY-AUS-FALLBACK", "ORDER-AUS-FALLBACK", 20m, targetDate.AddHours(12).AddMinutes(1));
        await SeedPosmProductSupplierMappingAsync("P-AUS-FALLBACK-2", string.Empty, null);

        await CreateService().UpdateAustralianSupplierStoreStatistics(targetDate);

        var row = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row =>
                row.Date == targetDate
                && row.BranchCode == "1007"
                && row.SupplierCode == "112"
            )
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(20m, row!.TotalAmount);
        Assert.Equal(5, row.TotalQuantity);
        Assert.Equal(1, row.OrderCount);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatistics_金额应按订单支付金额分摊()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedOrderAsync("ORDER-AUS-ALLOC", "1007", targetDate.AddHours(12), 3);
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-AUS-ALLOC",
            detailGuid: "DETAIL-AUS-ALLOC-1",
            productCode: "P-AUS-ALLOC-1",
            quantity: 1,
            actualAmount: 30m,
            supplierCode: "112"
        );
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-AUS-ALLOC",
            detailGuid: "DETAIL-AUS-ALLOC-2",
            productCode: "P-AUS-ALLOC-2",
            quantity: 2,
            actualAmount: 40m,
            supplierCode: "113"
        );
        await SeedPaymentAsync("PAY-AUS-ALLOC", "ORDER-AUS-ALLOC", 72.14m, targetDate.AddHours(12).AddMinutes(1));

        await CreateService().UpdateAustralianSupplierStoreStatistics(targetDate);

        var totalAmount = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1007")
            .SumAsync(row => row.TotalAmount);

        Assert.InRange(Math.Abs(totalAmount - 72.14m), 0m, 0.0001m);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatistics_局部供应商过滤应按Trim后编码匹配()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-TRIM-FILTER",
            detailGuid: "DETAIL-AUS-TRIM-FILTER",
            productCode: "P-AUS-TRIM-FILTER",
            branchCode: "1007",
            orderTime: targetDate.AddHours(12),
            quantity: 2,
            actualAmount: 18m,
            supplierCode: string.Empty
        );
        await SeedPosmProductSupplierMappingAsync("P-AUS-TRIM-FILTER", " 112 ", null);

        await CreateService().UpdateAustralianSupplierStoreStatistics(
            targetDate,
            supplierCodes: new List<string> { "112" }
        );

        var row = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row =>
                row.Date == targetDate
                && row.BranchCode == "1007"
                && row.SupplierCode == "112"
            )
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(18m, row!.TotalAmount);
        Assert.Equal(2, row.TotalQuantity);
        Assert.Equal(1, row.OrderCount);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatistics_局部供应商刷新不应删除其他供应商旧统计()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedAustralianSupplierStoreSalesDetailAsync(targetDate, "1007", "999", 99m, 9);
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-PARTIAL-112",
            detailGuid: "DETAIL-AUS-PARTIAL-112",
            productCode: "P-AUS-PARTIAL-112",
            branchCode: "1007",
            orderTime: targetDate.AddHours(12),
            quantity: 2,
            actualAmount: 18m,
            supplierCode: "112"
        );

        await CreateService().UpdateAustralianSupplierStoreStatistics(
            targetDate,
            supplierCodes: new List<string> { "112" }
        );

        var rows = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1007")
            .OrderBy(row => row.SupplierCode)
            .ToListAsync();

        Assert.Contains(rows, row => row.SupplierCode == "112" && row.TotalAmount == 18m);
        Assert.Contains(rows, row => row.SupplierCode == "999" && row.TotalAmount == 99m);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatistics_局部刷新应清理旧空白供应商主键()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedAustralianSupplierStoreSalesDetailAsync(targetDate, "1007", string.Empty, 99m, 9);
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-PARTIAL-BLANK",
            detailGuid: "DETAIL-AUS-PARTIAL-BLANK",
            productCode: "P-AUS-PARTIAL-BLANK",
            branchCode: "1007",
            orderTime: targetDate.AddHours(12),
            quantity: 2,
            actualAmount: 18m,
            supplierCode: "112"
        );

        await CreateService().UpdateAustralianSupplierStoreStatistics(
            targetDate,
            supplierCodes: new List<string> { "112" }
        );

        var rows = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1007")
            .OrderBy(row => row.SupplierCode)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("112", row.SupplierCode);
        Assert.Equal(18m, row.TotalAmount);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatistics_空订单号不应回退为明细行数()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedSaleAsync(
            orderGuid: string.Empty,
            detailGuid: "DETAIL-AUS-BLANK-ORDER",
            productCode: "P-AUS-BLANK-ORDER",
            branchCode: "1007",
            orderTime: targetDate.AddHours(12),
            quantity: 1,
            actualAmount: 6m,
            supplierCode: "112"
        );

        await CreateService().UpdateAustralianSupplierStoreStatistics(targetDate);

        var row = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row =>
                row.Date == targetDate
                && row.BranchCode == "1007"
                && row.SupplierCode == "112"
            )
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(0m, row!.TotalAmount);
        Assert.Equal(1, row.TotalQuantity);
        Assert.Equal(0, row.OrderCount);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatisticsWithContext_应清理旧空供应商主键()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedAustralianSupplierStoreSalesDetailAsync(targetDate, "1007", string.Empty, 99m, 9);
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-CONTEXT-UNKNOWN",
            detailGuid: "DETAIL-AUS-CONTEXT-UNKNOWN",
            productCode: "P-AUS-CONTEXT-UNKNOWN",
            branchCode: "1007",
            orderTime: targetDate.AddHours(12),
            quantity: 2,
            actualAmount: 8m,
            supplierCode: string.Empty
        );

        await InvokeAustralianSupplierStoreStatisticsWithContextAsync(targetDate, null, null);

        var rows = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1007")
            .OrderBy(row => row.SupplierCode)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("UNKNOWN", row.SupplierCode);
        Assert.Equal("未匹配供应商", row.SupplierName);
        Assert.Equal(8m, row.TotalAmount);
        Assert.Equal(2, row.TotalQuantity);
        Assert.Equal(1, row.OrderCount);
    }

    [Fact]
    public async Task UpdateAustralianSupplierStoreStatisticsWithContext_局部刷新应清理旧空白供应商主键()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedAustralianSupplierStoreSalesDetailAsync(targetDate, "1007", string.Empty, 99m, 9);
        await SeedSaleAsync(
            orderGuid: "ORDER-AUS-CONTEXT-PARTIAL-BLANK",
            detailGuid: "DETAIL-AUS-CONTEXT-PARTIAL-BLANK",
            productCode: "P-AUS-CONTEXT-PARTIAL-BLANK",
            branchCode: "1007",
            orderTime: targetDate.AddHours(12),
            quantity: 2,
            actualAmount: 18m,
            supplierCode: "112"
        );

        await InvokeAustralianSupplierStoreStatisticsWithContextAsync(
            targetDate,
            null,
            new List<string> { "112" }
        );

        var rows = await _localDb.Queryable<AustralianSupplierStoreSalesDetail>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1007")
            .OrderBy(row => row.SupplierCode)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("112", row.SupplierCode);
        Assert.Equal(18m, row.TotalAmount);
    }

    [Fact]
    public async Task UpdateDailyStatistics_拆分支付时金额按支付数量按明细订单数去重()
    {
        var targetDate = new DateTime(2026, 7, 6);
        await SeedOrderAsync("ORDER-DAILY-SPLIT", "1007", targetDate.AddHours(13), 99);
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-DAILY-SPLIT",
            detailGuid: "DETAIL-DAILY-SPLIT-1",
            productCode: "P-DAILY-SPLIT-1",
            quantity: 2,
            actualAmount: 9m,
            supplierCode: "112"
        );
        await SeedSaleDetailAsync(
            orderGuid: "ORDER-DAILY-SPLIT",
            detailGuid: "DETAIL-DAILY-SPLIT-2",
            productCode: "P-DAILY-SPLIT-2",
            quantity: 3,
            actualAmount: 11m,
            supplierCode: "112"
        );
        await SeedPaymentAsync("PAY-DAILY-SPLIT-1", "ORDER-DAILY-SPLIT", 7m, targetDate.AddHours(13).AddMinutes(1));
        await SeedPaymentAsync("PAY-DAILY-SPLIT-2", "ORDER-DAILY-SPLIT", 13m, targetDate.AddHours(13).AddMinutes(2));

        await CreateService().UpdateDailyStatistics(targetDate.ToString("yyyy-MM-dd"));

        var row = await _localDb.Queryable<DailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(20m, row!.TotalAmount);
        Assert.Equal(5, row.TotalQuantity);
        Assert.Equal(1, row.OrderCount);
        Assert.Equal(20m, row.AverageOrderValue);
    }

    [Fact]
    public async Task RecoverTimedOutProductStoreDailyRecalculationJobsAsync_只恢复超时执行中任务()
    {
        var nowUtc = new DateTime(2026, 6, 8, 6, 0, 0, DateTimeKind.Utc);
        var timeout = TimeSpan.FromMinutes(30);
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 1),
            SalesStatisticRefreshStatus.Queued,
            requestedAtUtc: nowUtc.AddMinutes(-31),
            jobId: Guid.NewGuid(),
            errorMessage: "旧排队任务"
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 2),
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: nowUtc.AddHours(-1),
            startedAtUtc: nowUtc.AddMinutes(-31),
            lastCheckedAtUtc: nowUtc.AddMinutes(-1),
            jobId: Guid.NewGuid(),
            errorMessage: "旧运行任务"
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 3),
            SalesStatisticRefreshStatus.Queued,
            requestedAtUtc: nowUtc.AddMinutes(-5),
            jobId: Guid.NewGuid()
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 4),
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: nowUtc.AddMinutes(-20),
            startedAtUtc: nowUtc.AddMinutes(-5),
            jobId: Guid.NewGuid()
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 5),
            SalesStatisticRefreshStatus.Fresh,
            lastCheckedAtUtc: nowUtc.AddHours(-2)
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 6),
            SalesStatisticRefreshStatus.Failed,
            lastCheckedAtUtc: nowUtc.AddHours(-2),
            errorMessage: "对账失败"
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 7),
            SalesStatisticRefreshStatus.Stale,
            lastCheckedAtUtc: nowUtc.AddHours(-2)
        );
        await SeedRefreshStateAsync(
            new DateTime(2026, 6, 8),
            SalesStatisticRefreshStatus.Pending,
            lastCheckedAtUtc: nowUtc.AddHours(-2)
        );

        var recoveredCount = await CreateService()
            .RecoverTimedOutProductStoreDailyRecalculationJobsAsync(timeout, nowUtc);

        Assert.Equal(2, recoveredCount);
        var recoveredQueued = await LoadRefreshStateAsync(new DateTime(2026, 6, 1));
        Assert.Equal(SalesStatisticRefreshStatus.Pending, recoveredQueued!.Status);
        Assert.Null(recoveredQueued.JobId);
        Assert.Null(recoveredQueued.StartedAtUtc);
        Assert.Null(recoveredQueued.CompletedAtUtc);
        Assert.Null(recoveredQueued.ErrorMessage);
        Assert.Equal(nowUtc, recoveredQueued.LastCheckedAtUtc);

        var recoveredRunning = await LoadRefreshStateAsync(new DateTime(2026, 6, 2));
        Assert.Equal(SalesStatisticRefreshStatus.Pending, recoveredRunning!.Status);
        Assert.Null(recoveredRunning.JobId);
        Assert.Null(recoveredRunning.StartedAtUtc);
        Assert.Null(recoveredRunning.CompletedAtUtc);
        Assert.Null(recoveredRunning.ErrorMessage);
        Assert.Equal(nowUtc, recoveredRunning.LastCheckedAtUtc);

        Assert.Equal(SalesStatisticRefreshStatus.Queued, (await LoadRefreshStateAsync(new DateTime(2026, 6, 3)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Running, (await LoadRefreshStateAsync(new DateTime(2026, 6, 4)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, (await LoadRefreshStateAsync(new DateTime(2026, 6, 5)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, (await LoadRefreshStateAsync(new DateTime(2026, 6, 6)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Stale, (await LoadRefreshStateAsync(new DateTime(2026, 6, 7)))!.Status);
        Assert.Equal(SalesStatisticRefreshStatus.Pending, (await LoadRefreshStateAsync(new DateTime(2026, 6, 8)))!.Status);
    }

    [Fact]
    public async Task RecoverTimedOutProductStoreDailyRecalculationJobsAsync_恢复后可再次提交重算()
    {
        var targetDate = new DateTime(2026, 6, 1);
        var nowUtc = new DateTime(2026, 6, 8, 6, 0, 0, DateTimeKind.Utc);
        var service = CreateService();
        await SeedRefreshStateAsync(
            targetDate,
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: nowUtc.AddHours(-1),
            startedAtUtc: nowUtc.AddMinutes(-31),
            jobId: Guid.NewGuid()
        );

        var recoveredCount = await service.RecoverTimedOutProductStoreDailyRecalculationJobsAsync(
            TimeSpan.FromMinutes(30),
            nowUtc
        );
        var result = await service.SubmitProductStoreDailyRecalculationAsync(new[] { targetDate }, "admin");

        Assert.Equal(1, recoveredCount);
        Assert.Single(result.SubmittedDates);
        Assert.Empty(result.SkippedDates);
        Assert.Equal(targetDate, result.SubmittedDates.Single());
    }

    [Fact]
    public async Task SubmitProductStoreDailyRecalculationAsync_重复日期与执行中日期仍按唯一日期跳过()
    {
        var queuedDate = new DateTime(2026, 6, 1);
        var freshDate = new DateTime(2026, 6, 2);
        var service = CreateService();
        await SeedRefreshStateAsync(
            queuedDate,
            SalesStatisticRefreshStatus.Running,
            requestedAtUtc: new DateTime(2026, 6, 8, 6, 0, 0, DateTimeKind.Utc),
            startedAtUtc: new DateTime(2026, 6, 8, 6, 1, 0, DateTimeKind.Utc),
            jobId: Guid.NewGuid()
        );

        var result = await service.SubmitProductStoreDailyRecalculationAsync(
            new[] { queuedDate, queuedDate, freshDate, freshDate },
            "admin",
            4
        );

        Assert.Equal(new[] { freshDate }, result.SubmittedDates);
        Assert.Equal(new[] { queuedDate }, result.SkippedDates);
    }

    [Fact]
    public void SubmitProductStoreDailyRecalculationAsync_保留默认并发参数并提供夹取帮助方法()
    {
        var submitMethod = typeof(SalesStatisticsJobService).GetMethod(
            nameof(SalesStatisticsJobService.SubmitProductStoreDailyRecalculationAsync)
        );
        Assert.NotNull(submitMethod);

        var parameters = submitMethod!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal("maxConcurrency", parameters[2].Name);
        Assert.True(parameters[2].IsOptional);
        Assert.Equal(3, Assert.IsType<int>(parameters[2].DefaultValue));

        var clampMethod = typeof(SalesStatisticsJobService).GetMethod(
            "NormalizeProductStatisticMaxConcurrency",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(clampMethod);
        Assert.Equal(3, Assert.IsType<int>(clampMethod!.Invoke(null, new object[] { 0 })));
        Assert.Equal(4, Assert.IsType<int>(clampMethod.Invoke(null, new object[] { 4 })));
        Assert.Equal(10, Assert.IsType<int>(clampMethod.Invoke(null, new object[] { 11 })));
    }

    [Fact]
    public async Task ExecuteTransactionSafelyAsync_业务异常后回滚再失败时_应保留原始业务异常()
    {
        var logger = new TestLogger<SalesStatisticsJobService>();
        var helper = typeof(SalesStatisticsJobService).GetMethod(
            "ExecuteTransactionSafelyAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.NotNull(helper);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeHelperAsync(
                helper!,
                () => Task.CompletedTask,
                () => throw new InvalidOperationException("业务失败"),
                () => Task.CompletedTask,
                () => throw new InvalidOperationException("回滚失败"),
                logger,
                "分时统计"
            )
        );

        Assert.Equal("业务失败", error.Message);
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Error
                && entry.Message.Contains("回滚事务失败", StringComparison.Ordinal)
                && entry.Message.Contains("分时统计", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task ExecuteTransactionSafelyAsync_提交异常后回滚再失败时_应保留原始提交异常()
    {
        var logger = new TestLogger<SalesStatisticsJobService>();
        var helper = typeof(SalesStatisticsJobService).GetMethod(
            "ExecuteTransactionSafelyAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.NotNull(helper);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeHelperAsync(
                helper!,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => throw new InvalidOperationException("提交失败"),
                () => throw new InvalidOperationException("回滚失败"),
                logger,
                "分店统计"
            )
        );

        Assert.Equal("提交失败", error.Message);
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Error
                && entry.Message.Contains("回滚事务失败", StringComparison.Ordinal)
                && entry.Message.Contains("分店统计", StringComparison.Ordinal)
        );
    }

    private SalesStatisticsJobService CreateService()
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

    private async Task SeedSaleAsync(
        string orderGuid,
        string detailGuid,
        string productCode,
        string? branchCode,
        DateTime orderTime,
        int quantity,
        decimal actualAmount,
        string? supplierCode,
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
            LastUploadTime = orderTime.AddMinutes(5),
        }).ExecuteCommandAsync();

        await _posmDb.Insertable(new SalesOrderDetail
        {
            OrderDetailGuid = detailGuid,
            OrderGuid = orderGuid,
            ProductCode = productCode,
            SupplierCode = supplierCode ?? string.Empty,
            ProductName = productCode,
            Barcode = $"{productCode}-BAR",
            Quantity = quantity,
            ActualAmount = actualAmount,
            LastUploadTime = orderTime.AddMinutes(6),
        }).ExecuteCommandAsync();

        await SeedPaymentAsync($"PAY-{detailGuid}", orderGuid, actualAmount, orderTime.AddMinutes(1));
    }

    private async Task SeedDeviceRegistrationAsync(string deviceCode, string branchCode)
    {
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = $"{deviceCode}-hardware",
            系统设备编号 = deviceCode,
            分店代码 = branchCode,
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = 1,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSaleDetailAsync(
        string orderGuid,
        string detailGuid,
        string productCode,
        int quantity,
        decimal actualAmount,
        string? supplierCode
    )
    {
        await _posmDb.Insertable(new SalesOrderDetail
        {
            OrderDetailGuid = detailGuid,
            OrderGuid = orderGuid,
            ProductCode = productCode,
            SupplierCode = supplierCode ?? string.Empty,
            ProductName = productCode,
            Barcode = $"{productCode}-BAR",
            Quantity = quantity,
            ActualAmount = actualAmount,
            LastUploadTime = DateTime.Now,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeCode, string storeName)
    {
        await _localDb.Insertable(new Store
        {
            StoreGUID = Guid.NewGuid().ToString("N"),
            StoreCode = storeCode,
            StoreName = storeName,
        }).ExecuteCommandAsync();
    }

    private async Task SeedOrderAsync(
        string orderGuid,
        string branchCode,
        DateTime orderTime,
        int itemCount
    )
    {
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = orderGuid,
            BranchCode = branchCode,
            OrderTime = orderTime,
            Status = 1,
            ItemCount = itemCount,
            LastUploadTime = orderTime.AddMinutes(5),
        }).ExecuteCommandAsync();
    }

    private async Task SeedPaymentAsync(
        string paymentGuid,
        string orderGuid,
        decimal amount,
        DateTime createdTime
    )
    {
        await _posmDb.Insertable(new PaymentDetail
        {
            PaymentGuid = paymentGuid,
            OrderGuid = orderGuid,
            Amount = amount,
            CreatedTime = createdTime,
            LastUploadTime = createdTime.AddMinutes(1),
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductAsync(string productCode)
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"{productCode}-uuid",
            ProductCode = productCode,
            ItemNumber = productCode,
            Barcode = $"{productCode}-BAR",
            ProductName = productCode,
            LocalSupplierCode = "200",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedPosmProductSupplierMappingAsync(
        string productCode,
        string? localSupplierCode,
        string? chinaSupplierCode
    )
    {
        await _posmDb.Insertable(new PosmProductSupplierMapping
        {
            ProductCode = productCode,
            LocalSupplierCode = localSupplierCode ?? string.Empty,
            ChinaSupplierCode = chinaSupplierCode,
            LastUpdateTime = DateTime.Now,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreSalesStatisticAsync(
        DateTime date,
        string branchCode,
        decimal totalAmount,
        int totalQuantity
    )
    {
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = date.Date,
            BranchCode = branchCode,
            BranchName = $"Store-{branchCode}",
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = 1,
            CustomerCount = 1,
            AverageOrderValue = totalAmount,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreSupplierSalesDetailAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        decimal totalAmount,
        int totalQuantity,
        bool? isDomestic = null
    )
    {
        await _localDb.Insertable(new StoreSupplierSalesDetail
        {
            Date = date.Date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            SupplierName = supplierCode,
            IsDomestic = isDomestic,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = 1,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSupplierSalesStatisticAsync(
        DateTime date,
        string supplierCode,
        decimal totalAmount,
        int totalQuantity,
        bool isDomestic = false
    )
    {
        await _localDb.Insertable(new SupplierSalesStatistic
        {
            Date = date.Date,
            SupplierCode = supplierCode,
            SupplierName = supplierCode,
            IsDomestic = isDomestic,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            StoreCount = 1,
            OrderCount = 1,
            UpdateTime = DateTime.Now,
        }).ExecuteCommandAsync();
    }

    private async Task SeedAustralianSupplierStoreSalesDetailAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        decimal totalAmount,
        int totalQuantity
    )
    {
        await _localDb.Insertable(new AustralianSupplierStoreSalesDetail
        {
            Date = date.Date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            SupplierName = supplierCode,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = 1,
        }).ExecuteCommandAsync();
    }

    private async Task<SalesStatisticRefreshState?> LoadRefreshStateAsync(DateTime targetDate)
    {
        return await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(s =>
                s.StatisticType == SalesStatisticType.ProductStoreDaily
                && s.Date >= targetDate.Date
                && s.Date < targetDate.Date.AddDays(1)
            )
            .FirstAsync();
    }

    private async Task SeedRefreshStateAsync(
        DateTime date,
        string status,
        DateTime? requestedAtUtc = null,
        DateTime? startedAtUtc = null,
        DateTime? lastCheckedAtUtc = null,
        Guid? jobId = null,
        string? errorMessage = null
    )
    {
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = date.Date,
            Status = status,
            SourceTimeZone = "POSM_LOCAL",
            JobId = jobId,
            RequestedBy = jobId == null ? null : "admin",
            RequestedAtUtc = requestedAtUtc,
            StartedAtUtc = startedAtUtc,
            LastCheckedAtUtc = lastCheckedAtUtc ?? requestedAtUtc ?? startedAtUtc,
            ErrorMessage = errorMessage,
        }).ExecuteCommandAsync();
    }

    private async Task InvokeAustralianSupplierStoreStatisticsWithContextAsync(
        DateTime date,
        List<string>? branchCodes,
        List<string>? supplierCodes
    )
    {
        var method = typeof(SalesStatisticsJobService).GetMethod(
            "UpdateAustralianSupplierStoreStatisticsWithContext",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var task = method!.Invoke(
            CreateService(),
            new object?[]
            {
                CreateSqlSugarContext(_localDb),
                CreatePosmSqlSugarContext(_posmDb),
                NullLogger<SalesStatisticsJobService>.Instance,
                date,
                branchCodes,
                supplierCodes,
            }
        ) as Task;

        Assert.NotNull(task);
        await task!;
    }

    private static async Task InvokeHelperAsync(
        MethodInfo helper,
        Func<Task> beginAsync,
        Func<Task> workAsync,
        Func<Task> commitAsync,
        Func<Task> rollbackAsync,
        ILogger<SalesStatisticsJobService> logger,
        string operationName
    )
    {
        var task = helper.Invoke(
            null,
            new object[] { beginAsync, workAsync, commitAsync, rollbackAsync, logger, operationName }
        ) as Task;

        Assert.NotNull(task);
        await task!;
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
        if (File.Exists(_localDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        if (File.Exists(_posmDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
