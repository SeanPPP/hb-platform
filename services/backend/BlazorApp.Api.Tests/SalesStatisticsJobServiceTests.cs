using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HBSalesRecord;
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
    private readonly string _hbSalesDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;
    private readonly SqlSugarScope _hbSalesDb;

    public SalesStatisticsJobServiceTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _hbSalesConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _localConnection.Open();
        _posmConnection.Open();
        _hbSalesConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));
        _hbSalesDb = new SqlSugarScope(CreateConnectionConfig(_hbSalesConnection.ConnectionString));

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
            typeof(SalesStatisticRefreshState),
            typeof(ScheduledTaskLease),
            typeof(ProductSetCode),
            typeof(StoreMultiCodeProduct)
        );
        _posmDb.CodeFirst.InitTables(
            typeof(SalesOrder),
            typeof(SalesOrderDetail),
            typeof(SalesReturnRecord),
            typeof(PaymentDetail),
            typeof(PosmProductSupplierMapping),
            typeof(POSM_设备注册信息表)
        );
        _hbSalesDb.CodeFirst.InitTables(typeof(SalesOrderMain), typeof(SalesOrderDetailRecord));
    }

    [Fact]
    public async Task LoadStoreCostsInBatchesAsync_超过批量上限应拆分查询且完整返回()
    {
        var productCodes = Enumerable.Range(
                1,
                SalesStatisticsJobService.StoreCostProductQueryBatchSize + 1
            )
            .Select(index => $"P-{index:0000}")
            .ToList();
        var prices = productCodes.Select((productCode, index) => new StoreRetailPrice
        {
            UUID = $"PRICE-{index:0000}",
            StoreCode = "1004",
            ProductCode = productCode,
            SupplierCode = "200",
            PurchasePrice = index + 1,
            IsActive = true,
            IsDeleted = false,
        }).ToList();
        foreach (var batch in prices.Chunk(100))
            await _localDb.Insertable(batch.ToList()).ExecuteCommandAsync();

        var queryCount = 0;
        _localDb.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.Contains("StoreRetailPrice", StringComparison.OrdinalIgnoreCase))
                queryCount++;
        };
        List<SalesStatisticsJobService.StoreCostRow> rows;
        try
        {
            rows = await SalesStatisticsJobService.LoadStoreCostsInBatchesAsync(
                CreateSqlSugarContext(_localDb),
                productCodes,
                new[] { "1004" }
            );
        }
        finally
        {
            _localDb.Aop.OnLogExecuting = null;
        }

        Assert.Equal(2, queryCount);
        Assert.Equal(productCodes.Count, rows.Count);
        Assert.Contains(rows, row => row.ProductCode == "P-0001" && row.PurchasePrice == 1m);
        Assert.Contains(rows, row => row.ProductCode == "P-0501" && row.PurchasePrice == 501m);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_完成后应恢复主库命令超时()
    {
        const int originalTimeoutSeconds = 37;
        _localDb.Ado.CommandTimeOut = originalTimeoutSeconds;

        await CreateService().UpdateProductStoreDailyStatistics(new DateTime(2026, 1, 2));

        Assert.Equal(originalTimeoutSeconds, _localDb.Ado.CommandTimeOut);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_零销售日应Fresh并写入完成状态()
    {
        var targetDate = new DateTime(2026, 1, 3);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        var productRowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();
        var storeRowCount = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
        Assert.Null(state.ErrorMessage);
        Assert.NotNull(state.LastAggregatedAtUtc);
        Assert.NotNull(state.LastCheckedAtUtc);
        Assert.NotNull(state.CompletedAtUtc);
        Assert.Equal(0, productRowCount);
        Assert.Equal(0, storeRowCount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025零销售日应成对写入Fresh状态()
    {
        var targetDate = new DateTime(2025, 4, 4);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(x => x.Date == targetDate)
            .ToListAsync();
        var productRowCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();
        var storeRowCount = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(x => x.Date == targetDate)
            .CountAsync();

        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.ProductStoreDaily
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.CompletedAtUtc.HasValue
        );
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.StoreSales
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.CompletedAtUtc.HasValue
        );
        Assert.Equal(0, productRowCount);
        Assert.Equal(0, storeRowCount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_有来源行但无有效主键时应Failed()
    {
        var targetDate = new DateTime(2026, 1, 4);
        await SeedOrderAsync("ORDER-INVALID-KEY", string.Empty, targetDate.AddHours(10), 1);
        await SeedSaleDetailAsync(
            "ORDER-INVALID-KEY",
            "DETAIL-INVALID-KEY",
            string.Empty,
            1,
            10m,
            "200"
        );

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("没有可写入的有效分店商品", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales普通销售应写入商品分店统计()
    {
        var targetDate = new DateTime(2025, 4, 1);
        var modifiedAt = targetDate.AddHours(11);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 30m, 2);
        await SeedHBSalesAsync(1, targetDate, "HB-P-NORMAL", "1004", "200", 2m, 30m, "1", modifiedAt);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004" && x.ProductCode == "HB-P-NORMAL")
            .FirstAsync();
        var state = await LoadRefreshStateAsync(targetDate);

        Assert.NotNull(row);
        Assert.Equal("200", row!.SupplierCode);
        Assert.Equal("HB-P-NORMAL 名称", row.ProductName);
        Assert.Equal("HB-P-NORMAL-BAR", row.Barcode);
        Assert.Equal(2, row.TotalQuantity);
        Assert.Equal(30m, row.TotalAmount);
        Assert.Equal(modifiedAt, row.LastSourceUploadTime);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales类型2应排除()
    {
        var targetDate = new DateTime(2025, 4, 2);
        await SeedHBSalesAsync(2, targetDate, "HB-P-EXCLUDED", "1004", "200", 2m, 30m, "2");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var count = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.ProductCode == "HB-P-EXCLUDED")
            .CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales仅排除类型2且包含空类型和退货类型()
    {
        var targetDate = new DateTime(2025, 4, 2);
        await SeedHBSalesAsync(201, targetDate, "HB-P-TYPE-1", "1004", "200", 1m, 10m, "1");
        await SeedHBSalesAsync(202, targetDate, "HB-P-TYPE-NULL", "1004", "200", 1m, 20m, null);
        await SeedHBSalesAsync(203, targetDate, "HB-P-TYPE-2", "1004", "200", 1m, 30m, "2");
        await SeedHBSalesAsync(204, targetDate, "HB-P-TYPE-3", "1004", "200", 1m, 40m, "3");
        await SeedHBSalesAsync(205, targetDate, "HB-P-TYPE-4", "1004", "200", 1m, 50m, "4");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var rows = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .OrderBy(row => row.ProductCode)
            .ToListAsync();

        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(rows, row => row.ProductCode == "HB-P-TYPE-2");
        Assert.Contains(rows, row => row.ProductCode == "HB-P-TYPE-NULL" && row.TotalAmount == 20m);
        Assert.Contains(rows, row => row.ProductCode == "HB-P-TYPE-3" && row.TotalAmount == -40m);
        Assert.Contains(rows, row => row.ProductCode == "HB-P-TYPE-4" && row.TotalAmount == -50m);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales单据类型应Trim后两路使用同一口径()
    {
        var targetDate = new DateTime(2025, 4, 22);
        await SeedHBSalesAsync(422, targetDate, "HB-P-SPACED-2", "1004", "200", 1m, 20m, " 2 ");
        await SeedHBSalesAsync(423, targetDate, "HB-P-SPACED-3", "1004", "200", 1m, 30m, " 3");
        await SeedHBSalesAsync(424, targetDate, "HB-P-SPACED-4", "1004", "200", 1m, 40m, "4 ");
        await SeedHBSalesAsync(425, targetDate, "HB-P-NULL-TYPE", "1004", "200", 1m, 50m, null);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var products = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();

        Assert.DoesNotContain(products, row => row.ProductCode == "HB-P-SPACED-2");
        Assert.Contains(products, row => row.ProductCode == "HB-P-SPACED-3" && row.TotalAmount == -30m);
        Assert.Contains(products, row => row.ProductCode == "HB-P-SPACED-4" && row.TotalAmount == -40m);
        Assert.Contains(products, row => row.ProductCode == "HB-P-NULL-TYPE" && row.TotalAmount == 50m);
        Assert.Equal(-20m, store!.TotalAmount);
        Assert.Equal(-1, store.TotalQuantity);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025应原子替换分店和商品统计并同步两类状态()
    {
        var targetDate = new DateTime(2025, 4, 2);
        var sourceWatermark = targetDate.AddHours(15);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 999m, 99);
        await SeedHBSalesAsync(206, targetDate, "HB-P-ATOMIC", "1004", "200", 2m, 30m, "1", sourceWatermark);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "HB-P-ATOMIC")
            .FirstAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Equal(30m, store!.TotalAmount);
        Assert.Equal(30m, product!.TotalAmount);
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.StoreSales
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.LastSourceUploadTime == sourceWatermark
        );
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.ProductStoreDaily
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.LastSourceUploadTime == sourceWatermark
        );
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025原子路径应只加载一次HBSales明细且保留双来源口径()
    {
        var targetDate = new DateTime(2025, 4, 24);
        await SeedSaleAsync(
            "POSM-ATOMIC-ONE-LOAD",
            "POSM-ATOMIC-ONE-LOAD-DETAIL",
            "POSM-P-ATOMIC-ONE-LOAD",
            "1004",
            targetDate.AddHours(9),
            1,
            7m,
            "200"
        );
        await SeedHBSalesAsync(426, targetDate, "HB-P-ATOMIC-SALE", " 1004 ", "200", 2m, 20m, "1");
        await SeedHBSalesAsync(427, targetDate, "HB-P-ATOMIC-RETURN", "1004", "200", 1m, 5m, " 3 ");
        await SeedHBSalesAsync(428, targetDate, "HB-P-ATOMIC-EXCLUDED", "1004", "200", 1m, 99m, " 2 ");

        var detailLoadCount = 0;
        var aggregateQueryCount = 0;
        var watermarkQueryCount = 0;
        _hbSalesDb.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.Contains("B产品编号", StringComparison.Ordinal))
                detailLoadCount++;
            if (sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase))
                aggregateQueryCount++;
            if (sql.Contains("MAX(", StringComparison.OrdinalIgnoreCase))
                watermarkQueryCount++;
        };
        try
        {
            await CreateService().UpdateProductStoreDailyStatistics(targetDate);
        }
        finally
        {
            _hbSalesDb.Aop.OnLogExecuting = null;
        }

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var products = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Equal(1, detailLoadCount);
        Assert.Equal(0, aggregateQueryCount);
        // pre 水位复用明细行四列 MAX，HBSales 仅保留 post 的一次数据库水位查询。
        Assert.Equal(1, watermarkQueryCount);
        Assert.Equal(22m, store!.TotalAmount);
        Assert.Equal(2m, store.TotalQuantity);
        Assert.Equal(3, store.OrderCount);
        Assert.Equal(22m, products.Sum(row => row.TotalAmount));
        Assert.Contains(products, row => row.ProductCode == "HB-P-ATOMIC-RETURN" && row.TotalAmount == -5m);
        Assert.DoesNotContain(products, row => row.ProductCode == "HB-P-ATOMIC-EXCLUDED");
    }

    [Fact]
    public async Task HBSales批量快照签名应对顺序稳定且能发现数量金额删除和时间变化()
    {
        var targetDate = new DateTime(2025, 4, 26);
        await SeedHBSalesAsync(801, targetDate, "SNAP-A", "1004", "200", 1m, 10m, "1", targetDate.AddHours(8));
        await SeedHBSalesAsync(802, targetDate, "SNAP-B", "1004", "200", 2m, 20m, "3", targetDate.AddHours(9));
        var service = CreateService();

        var first = (await service.Load2025HBSalesBatchSnapshotAsync([targetDate])).GetSignature(targetDate);
        await _hbSalesDb.Deleteable<SalesOrderDetailRecord>().ExecuteCommandAsync();
        await _hbSalesDb.Deleteable<SalesOrderMain>().ExecuteCommandAsync();
        // 反向插入验证 checksum 不依赖数据库返回顺序。
        await SeedHBSalesAsync(802, targetDate, "SNAP-B", "1004", "200", 2m, 20m, "3", targetDate.AddHours(9));
        await SeedHBSalesAsync(801, targetDate, "SNAP-A", "1004", "200", 1m, 10m, "1", targetDate.AddHours(8));
        var reordered = (await service.Load2025HBSalesBatchSnapshotAsync([targetDate])).GetSignature(targetDate);
        Assert.Equal(first, reordered);

        var amountRow = await _hbSalesDb.Queryable<SalesOrderDetailRecord>().Where(row => row.ID == 801).FirstAsync();
        amountRow!.B合计金额 = 11m;
        await _hbSalesDb.Updateable(amountRow).ExecuteCommandAsync();
        var amountChanged = (await service.Load2025HBSalesBatchSnapshotAsync([targetDate])).GetSignature(targetDate);
        Assert.NotEqual(first.Checksum, amountChanged.Checksum);

        await _hbSalesDb.Deleteable<SalesOrderDetailRecord>().Where(row => row.ID == 802).ExecuteCommandAsync();
        var deleted = (await service.Load2025HBSalesBatchSnapshotAsync([targetDate])).GetSignature(targetDate);
        Assert.Equal(first.RowCount - 1, deleted.RowCount);

        var mainRow = await _hbSalesDb.Queryable<SalesOrderMain>().Where(row => row.ID == 801).FirstAsync();
        mainRow!.FGC_CreateDate = targetDate.AddHours(12);
        await _hbSalesDb.Updateable(mainRow).ExecuteCommandAsync();
        var timeChanged = (await service.Load2025HBSalesBatchSnapshotAsync([targetDate])).GetSignature(targetDate);
        Assert.NotEqual(deleted.Checksum, timeChanged.Checksum);
        Assert.Equal(targetDate.AddHours(12), timeChanged.MainCreatedAt);
    }

    [Fact]
    public async Task HBSales批量预载应与普通单日路径保持类型二三四口径等价()
    {
        var baselineDate = new DateTime(2025, 4, 27);
        var batchDate = baselineDate.AddDays(1);
        await SeedHBSalesAsync(811, baselineDate, "NORMAL", "1004", "200", 2m, 20m, "1");
        await SeedHBSalesAsync(812, baselineDate, "EXCLUDED", "1004", "200", 1m, 99m, "2");
        await SeedHBSalesAsync(813, baselineDate, "RETURN", "1004", "200", 1m, 5m, "3");
        await SeedHBSalesAsync(814, baselineDate, "REFUND", "1004", "200", 1m, 3m, "4");
        await SeedHBSalesAsync(821, batchDate, "NORMAL", "1004", "200", 2m, 20m, "1");
        await SeedHBSalesAsync(822, batchDate, "EXCLUDED", "1004", "200", 1m, 99m, "2");
        await SeedHBSalesAsync(823, batchDate, "RETURN", "1004", "200", 1m, 5m, "3");
        await SeedHBSalesAsync(824, batchDate, "REFUND", "1004", "200", 1m, 3m, "4");
        var service = CreateService();

        await service.UpdateProductStoreDailyStatistics(baselineDate);
        var snapshot = await service.Load2025HBSalesBatchSnapshotAsync([batchDate]);
        await service.Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(batchDate, snapshot);

        var baselineRows = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == baselineDate).OrderBy(row => row.ProductCode).ToListAsync();
        var batchRows = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == batchDate).OrderBy(row => row.ProductCode).ToListAsync();
        Assert.Equal(
            baselineRows.Select(row => (row.ProductCode, row.TotalQuantity, row.TotalAmount)),
            batchRows.Select(row => (row.ProductCode, row.TotalQuantity, row.TotalAmount))
        );
        var states = await _localDb.Queryable<SalesStatisticRefreshState>().Where(row => row.Date == batchDate).ToListAsync();
        Assert.All(states, state => Assert.Equal("ProvisionalFresh", state.Status));
    }

    [Fact]
    public async Task HBSales批量快照稳定后才升级Fresh且变化时双状态Failed()
    {
        var targetDate = new DateTime(2025, 4, 29);
        await SeedHBSalesAsync(831, targetDate, "STABLE", "1004", "200", 1m, 10m, "1");
        var service = CreateService();
        var snapshot = await service.Load2025HBSalesBatchSnapshotAsync([targetDate]);

        await service.Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(targetDate, snapshot);
        var provisionalStates = await _localDb.Queryable<SalesStatisticRefreshState>().Where(row => row.Date == targetDate).ToListAsync();
        Assert.All(provisionalStates, state => Assert.Equal("ProvisionalFresh", state.Status));

        await service.Finalize2025BatchSnapshotDateAsync(targetDate);
        var freshStates = await _localDb.Queryable<SalesStatisticRefreshState>().Where(row => row.Date == targetDate).ToListAsync();
        Assert.All(freshStates, state => Assert.Equal(SalesStatisticRefreshStatus.Fresh, state.Status));

        var detail = await _hbSalesDb.Queryable<SalesOrderDetailRecord>().Where(row => row.ID == 831).FirstAsync();
        detail!.B数量 = 2m;
        await _hbSalesDb.Updateable(detail).ExecuteCommandAsync();
        var postSnapshot = await service.Load2025HBSalesBatchSnapshotAsync([targetDate]);
        Assert.NotEqual(snapshot.GetSignature(targetDate), postSnapshot.GetSignature(targetDate));
        await service.Fail2025BatchSnapshotDatesAsync([targetDate], "测试：批末 HBSales 签名变化");
        var failedStates = await _localDb.Queryable<SalesStatisticRefreshState>().Where(row => row.Date == targetDate).ToListAsync();
        Assert.All(failedStates, state => Assert.Equal(SalesStatisticRefreshStatus.Failed, state.Status));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales主表结账日期窗口应包含边界和三天异常并排除窗口外()
    {
        var targetDate = new DateTime(2025, 4, 24);
        var expectedWatermark = targetDate.AddHours(12);
        await SeedHBSalesAsync(
            701,
            targetDate,
            "HB-P-MAIN-MINUS-7",
            "1004",
            "200",
            1m,
            10m,
            "1",
            targetDate.AddHours(10),
            mainCheckoutDate: targetDate.AddDays(-7)
        );
        await SeedHBSalesAsync(
            702,
            targetDate,
            "HB-P-MAIN-PLUS-7",
            "1004",
            "200",
            1m,
            20m,
            "1",
            targetDate.AddHours(11),
            mainCheckoutDate: targetDate.AddDays(7)
        );
        await SeedHBSalesAsync(
            703,
            targetDate,
            "HB-P-MAIN-PLUS-3",
            "1004",
            "200",
            1m,
            30m,
            "1",
            expectedWatermark,
            mainCheckoutDate: targetDate.AddDays(3)
        );
        await SeedHBSalesAsync(
            704,
            targetDate,
            "HB-P-MAIN-OUTSIDE",
            "1004",
            "200",
            1m,
            40m,
            "1",
            targetDate.AddDays(8).AddHours(1),
            mainCheckoutDate: targetDate.AddDays(8)
        );

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var products = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Equal(60m, store!.TotalAmount);
        Assert.Contains(products, row => row.ProductCode == "HB-P-MAIN-MINUS-7");
        Assert.Contains(products, row => row.ProductCode == "HB-P-MAIN-PLUS-7");
        Assert.Contains(products, row => row.ProductCode == "HB-P-MAIN-PLUS-3");
        Assert.DoesNotContain(products, row => row.ProductCode == "HB-P-MAIN-OUTSIDE");
        Assert.All(states, state => Assert.Equal(expectedWatermark, state.LastSourceUploadTime));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales查询后应恢复命令超时()
    {
        const int originalTimeoutSeconds = 37;
        var targetDate = new DateTime(2025, 4, 2);
        _hbSalesDb.Ado.CommandTimeOut = originalTimeoutSeconds;
        await SeedHBSalesAsync(207, targetDate, "HB-P-TIMEOUT", "1004", "200", 1m, 10m, "1");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        Assert.Equal(originalTimeoutSeconds, _hbSalesDb.Ado.CommandTimeOut);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales类型3和4应反向冲减()
    {
        var targetDate = new DateTime(2025, 4, 3);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", -15m, -3);
        await SeedHBSalesAsync(3, targetDate, "HB-P-RETURN", "1004", "200", 1m, 10m, "3");
        await SeedHBSalesAsync(4, targetDate, "HB-P-RETURN", "1004", "200", 2m, 5m, "4");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.ProductCode == "HB-P-RETURN")
            .FirstAsync();
        Assert.NotNull(row);
        Assert.Equal(-3, row!.TotalQuantity);
        Assert.Equal(-15m, row.TotalAmount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025HBSales小数数量应先汇总再转整数()
    {
        var targetDate = new DateTime(2025, 4, 3);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 12m, 1);
        await SeedHBSalesAsync(31, targetDate, "HB-P-FRACTION", "1004", "200", 0.6m, 6m, "1");
        await SeedHBSalesAsync(32, targetDate, "HB-P-FRACTION", "1004", "200", 0.6m, 6m, "1");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x =>
                x.Date == targetDate
                && x.BranchCode == "1004"
                && x.ProductCode == "HB-P-FRACTION"
            )
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(1, row!.TotalQuantity);
        Assert.Equal(12m, row.TotalAmount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025缺商品编码应使用三类强键唯一映射()
    {
        var targetDate = new DateTime(2025, 4, 13);
        await SeedProductLookupAsync("P-ITEM", "ITEM-ONLY", "P-ITEM-BAR");
        await SeedProductSetCodeAsync("P-SET", "SET-ONLY");
        await SeedStoreMultiCodeProductAsync("1018", "P-MULTI", "MULTI-ONLY");
        await SeedHBSalesAsync(
            401,
            targetDate,
            null,
            "1004",
            "200",
            1m,
            10m,
            "1",
            barcode: null,
            useDefaultBarcode: false,
            itemNumber: " ITEM-ONLY "
        );
        await SeedHBSalesAsync(402, targetDate, null, "1004", "200", 1m, 20m, "1", barcode: "SET-ONLY");
        await SeedHBSalesAsync(403, targetDate, null, "1018", "200", 1m, 30m, "1", barcode: "MULTI-ONLY");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var rows = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .OrderBy(row => row.ProductCode)
            .ToListAsync();

        Assert.Equal(new[] { "P-ITEM", "P-MULTI", "P-SET" }, rows.Select(row => row.ProductCode));
        Assert.Equal(60m, rows.Sum(row => row.TotalAmount));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025跨来源同一强键候选应接受()
    {
        var targetDate = new DateTime(2025, 4, 14);
        await SeedProductLookupAsync("P-CONSISTENT", "CONSISTENT-CODE", "CONSISTENT-CODE");
        await SeedProductSetCodeAsync("P-CONSISTENT", "CONSISTENT-CODE");
        await SeedStoreMultiCodeProductAsync("1018", "P-CONSISTENT", "CONSISTENT-CODE");
        await SeedHBSalesAsync(404, targetDate, null, "1018", "200", 1m, 10m, "1", barcode: "CONSISTENT-CODE");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.Date == targetDate && item.ProductCode == "P-CONSISTENT")
            .FirstAsync();
        Assert.NotNull(row);
        Assert.Equal(10m, row!.TotalAmount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025三级映射应使用OrdinalIgnoreCase且SQL不Trim列()
    {
        var targetDate = new DateTime(2025, 4, 23);
        await SeedStoreMultiCodeProductAsync("Store-A", "P-BRANCH-CASE", "Branch-Bar");
        await SeedProductLookupAsync("P-ITEM-CASE", "Item-Key", "Unused-Bar");
        await SeedProductSetCodeAsync("P-SET-CASE", "Set-Bar");
        await SeedStoreMultiCodeProductAsync("Other-Store", "P-CROSS-CASE", "Cross-Bar");

        await SeedHBSalesAsync(431, targetDate, null, "store-a", "200", 1m, 1m, "1", barcode: "Branch-Bar");
        await SeedHBSalesAsync(432, targetDate, null, "store-a", "200", 1m, 1m, "1", barcode: "branch-bar");
        await SeedHBSalesAsync(433, targetDate, null, "1004", "200", 1m, 1m, "1", barcode: null, useDefaultBarcode: false, itemNumber: "Item-Key");
        await SeedHBSalesAsync(434, targetDate, null, "1004", "200", 1m, 1m, "1", barcode: null, useDefaultBarcode: false, itemNumber: "item-key");
        await SeedHBSalesAsync(435, targetDate, null, "1004", "200", 1m, 1m, "1", barcode: "Set-Bar");
        await SeedHBSalesAsync(436, targetDate, null, "1004", "200", 1m, 1m, "1", barcode: "set-bar");
        await SeedHBSalesAsync(437, targetDate, null, "no-exact-store", "200", 1m, 1m, "1", barcode: "Cross-Bar");
        await SeedHBSalesAsync(438, targetDate, null, "no-exact-store", "200", 1m, 1m, "1", barcode: "cross-bar");

        var candidateSql = new List<string>();
        _localDb.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.Contains("ProductSetCode", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("StoreMultiCodeProduct", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("FROM \"Product\"", StringComparison.OrdinalIgnoreCase))
            {
                candidateSql.Add(sql);
            }
        };
        try
        {
            await CreateService().UpdateProductStoreDailyStatistics(targetDate);
        }
        finally
        {
            _localDb.Aop.OnLogExecuting = null;
        }

        var rows = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        Assert.Contains(rows, row => row.ProductCode == "P-BRANCH-CASE" && row.TotalAmount == 2m);
        Assert.Contains(rows, row => row.ProductCode == "P-ITEM-CASE" && row.TotalAmount == 2m);
        Assert.Contains(rows, row => row.ProductCode == "P-SET-CASE" && row.TotalAmount == 2m);
        Assert.Contains(rows, row => row.ProductCode == "P-CROSS-CASE" && row.TotalAmount == 2m);
        Assert.NotEmpty(candidateSql);
        Assert.All(candidateSql, sql => Assert.DoesNotContain("TRIM(", sql, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025分店多码应优先覆盖全局冲突_0558()
    {
        var targetDate = new DateTime(2025, 4, 18);
        await SeedStoreMultiCodeProductAsync("1018", "P-0558-STORE", "0558");
        await SeedProductLookupAsync("P-0558-GLOBAL-A", "0558", "A-BAR");
        await SeedProductSetCodeAsync("P-0558-GLOBAL-B", "0558");
        await SeedHBSalesAsync(407, targetDate, null, "1018", "200", 1m, 10m, "1", barcode: "0558");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.Date == targetDate && item.ProductCode == "P-0558-STORE")
            .FirstAsync();
        Assert.NotNull(row);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025全局唯一候选应回退解析2010至2019()
    {
        var targetDate = new DateTime(2025, 4, 19);
        for (var code = 2010; code <= 2019; code++)
        {
            var lookupCode = code.ToString();
            await SeedProductLookupAsync($"P-GLOBAL-{lookupCode}", $"ITEM-{lookupCode}", lookupCode);
            await SeedHBSalesAsync(code, targetDate, null, "1004", "200", 1m, 10m, "1", barcode: lookupCode);
        }

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var rows = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.Date == targetDate)
            .OrderBy(item => item.ProductCode)
            .ToListAsync();
        Assert.Equal(10, rows.Count);
        Assert.Equal(100m, rows.Sum(item => item.TotalAmount));
        Assert.All(rows, row => Assert.StartsWith("P-GLOBAL-", row.ProductCode));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025失效映射不应参与解析()
    {
        var targetDate = new DateTime(2025, 4, 20);
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1018",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedProductLookupAsync("P-INACTIVE", "INACTIVE-CODE", "INACTIVE-CODE", false, true);
        await SeedProductSetCodeAsync("P-INACTIVE", "INACTIVE-CODE", false, true);
        await SeedStoreMultiCodeProductAsync("1018", "P-INACTIVE", "INACTIVE-CODE", false, true);
        await SeedHBSalesAsync(420, targetDate, null, "1018", "200", 1m, 10m, "1", barcode: "INACTIVE-CODE");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UpdateProductStoreDailyStatistics(targetDate)
        );

        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.Date == targetDate && item.ProductCode == "OLD-PRODUCT")
            .FirstAsync();
        Assert.Contains("唯一商品候选", error.Message);
        Assert.Equal(99m, product!.TotalAmount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025跨分店多码冲突时应阻断()
    {
        var targetDate = new DateTime(2025, 4, 21);
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1018",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedStoreMultiCodeProductAsync("1004", "P-MULTI-A", "MULTI-CONFLICT");
        await SeedStoreMultiCodeProductAsync("1005", "P-MULTI-B", "MULTI-CONFLICT");
        await SeedHBSalesAsync(421, targetDate, null, "1018", "200", 1m, 10m, "1", barcode: "MULTI-CONFLICT");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UpdateProductStoreDailyStatistics(targetDate)
        );

        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.Date == targetDate && item.ProductCode == "OLD-PRODUCT")
            .FirstAsync();
        Assert.Contains("唯一商品候选", error.Message);
        Assert.Equal(99m, product!.TotalAmount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateProductStoreDailyStatistics_2025缺商品编码无候选或冲突时应保留旧双表(bool hasConflict)
    {
        var targetDate = hasConflict ? new DateTime(2025, 4, 15) : new DateTime(2025, 4, 16);
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1018",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        if (hasConflict)
        {
            await SeedProductLookupAsync("P-CONFLICT-A", "CONFLICT-CODE", "A-BAR");
            await SeedProductSetCodeAsync("P-CONFLICT-B", "CONFLICT-CODE");
        }
        await SeedHBSalesAsync(
            hasConflict ? 405 : 406,
            targetDate,
            null,
            "1018",
            "200",
            1m,
            10m,
            "1",
            barcode: hasConflict ? "CONFLICT-CODE" : "NO-CANDIDATE"
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UpdateProductStoreDailyStatistics(targetDate)
        );

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1018")
            .FirstAsync();
        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "OLD-PRODUCT")
            .FirstAsync();

        Assert.Contains("唯一商品候选", error.Message);
        Assert.Equal(99m, store!.TotalAmount);
        Assert.Equal(99m, product!.TotalAmount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2026不应查询缺商品编码候选表()
    {
        var targetDate = new DateTime(2026, 4, 17);
        _localDb.DbMaintenance.DropTable<ProductSetCode>();
        _localDb.DbMaintenance.DropTable<StoreMultiCodeProduct>();
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 10m, 1);
        await SeedSaleAsync("POSM-2026-LOOKUP", "POSM-2026-LOOKUP-DETAIL", "P-2026-LOOKUP", "1004", targetDate.AddHours(9), 1, 10m, "200");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(item => item.Date == targetDate && item.ProductCode == "P-2026-LOOKUP")
            .FirstAsync();
        Assert.NotNull(row);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025全分店非午夜请求应原子替换双表并同步状态()
    {
        var requestDate = new DateTime(2025, 4, 7, 16, 30, 0);
        var targetDate = requestDate.Date;
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1004",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedHBSalesAsync(301, targetDate, "HB-P-STORE-ATOMIC", "1004", "200", 2m, 30m, "1");

        await CreateService().UpdateStoreStatistics(requestDate);

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "HB-P-STORE-ATOMIC")
            .FirstAsync();
        var oldProductCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "OLD-PRODUCT")
            .CountAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Equal(30m, store!.TotalAmount);
        Assert.Equal(30m, product!.TotalAmount);
        Assert.Equal(0, oldProductCount);
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.ProductStoreDaily
            && row.Status == SalesStatisticRefreshStatus.Fresh);
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.StoreSales
            && row.Status == SalesStatisticRefreshStatus.Fresh);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025指定分店应拒绝避免破坏双表一致性()
    {
        var requestDate = new DateTime(2025, 4, 7, 16, 30, 0);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UpdateStoreStatistics(requestDate, new List<string> { "1004" })
        );

        Assert.Contains("不能仅刷新指定分店", error.Message);
        Assert.Contains("双表一致性", error.Message);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025新水位为空时应成对覆盖旧水位()
    {
        var requestDate = new DateTime(2025, 4, 8, 14, 0, 0);
        var targetDate = requestDate.Date;
        await SeedRefreshStateAsync(
            targetDate,
            SalesStatisticRefreshStatus.Fresh,
            lastSourceUploadTime: targetDate.AddHours(8)
        );
        await SeedRefreshStateAsync(
            targetDate,
            SalesStatisticRefreshStatus.Fresh,
            lastSourceUploadTime: targetDate.AddHours(9),
            statisticType: SalesStatisticType.StoreSales
        );

        await CreateService().UpdateStoreStatistics(requestDate);

        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        var productState = states.Single(row => row.StatisticType == SalesStatisticType.ProductStoreDaily);
        var storeState = states.Single(row => row.StatisticType == SalesStatisticType.StoreSales);

        Assert.Equal(productState.Status, storeState.Status);
        Assert.Null(productState.LastSourceUploadTime);
        Assert.Null(storeState.LastSourceUploadTime);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025混合有效与缺商品非零来源应失败且保留旧双表()
    {
        var targetDate = new DateTime(2025, 4, 9);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1004",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedHBSalesAsync(302, targetDate, "HB-P-VALID", "1004", "200", 1m, 10m, "1");
        await SeedHBSalesAsync(303, targetDate, null, "1004", "200", 1m, 5m, "1");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UpdateStoreStatistics(targetDate.AddHours(10))
        );

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "OLD-PRODUCT")
            .FirstAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Contains("商品编码", error.Message);
        Assert.Equal(99m, store!.TotalAmount);
        Assert.Equal(99m, product!.TotalAmount);
        Assert.All(states, row => Assert.Equal(SalesStatisticRefreshStatus.Failed, row.Status));
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.ProductStoreDaily);
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.StoreSales);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025零值无效来源应忽略()
    {
        var targetDate = new DateTime(2025, 4, 10);
        await SeedHBSalesAsync(304, targetDate, "HB-P-VALID", "1004", "200", 1m, 10m, "1");
        await SeedHBSalesAsync(305, targetDate, null, null, "200", 0m, 0m, "1");

        await CreateService().UpdateStoreStatistics(targetDate.AddHours(10));

        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "HB-P-VALID")
            .FirstAsync();
        var invalidCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == null)
            .CountAsync();

        Assert.NotNull(product);
        Assert.Equal(10m, product!.TotalAmount);
        Assert.Equal(0, invalidCount);
    }

    [Fact]
    public async Task RunLeasedProductStoreDailyRefreshAsync_2025应原子刷新双表并完成租约()
    {
        var requestDate = new DateTime(2025, 4, 11, 11, 45, 0);
        var targetDate = requestDate.Date;
        await SeedHBSalesAsync(306, targetDate, "HB-P-LEASED", "1004", "200", 1m, 10m, "1");
        using var serviceProvider = CreateRollingRefreshServiceProvider();
        var service = CreateService(serviceProvider.GetRequiredService<IServiceScopeFactory>());

        var succeeded = await InvokeRunLeasedProductStoreDailyRefreshAsync(service, requestDate);

        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        var lease = await _localDb.Queryable<ScheduledTaskLease>()
            .Where(row => row.ScopeKey == targetDate.ToString("yyyy-MM-dd"))
            .FirstAsync();

        Assert.True(succeeded);
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.ProductStoreDaily
            && row.Status == SalesStatisticRefreshStatus.Fresh);
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.StoreSales
            && row.Status == SalesStatisticRefreshStatus.Fresh);
        Assert.Equal(ScheduledTaskLeaseStatus.Success, lease!.Status);
    }

    [Fact]
    public async Task RunLeasedProductStoreDailyRefreshAsync_2025原子失败时应成对标记并完成失败租约()
    {
        var targetDate = new DateTime(2025, 4, 12);
        await SeedHBSalesAsync(307, targetDate, null, "1004", "200", 1m, 10m, "1");
        using var serviceProvider = CreateRollingRefreshServiceProvider();
        var service = CreateService(serviceProvider.GetRequiredService<IServiceScopeFactory>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeRunLeasedProductStoreDailyRefreshAsync(service, targetDate.AddHours(9))
        );

        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        var lease = await _localDb.Queryable<ScheduledTaskLease>()
            .Where(row => row.ScopeKey == targetDate.ToString("yyyy-MM-dd"))
            .FirstAsync();

        Assert.Contains("商品编码", error.Message);
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.ProductStoreDaily
            && row.Status == SalesStatisticRefreshStatus.Failed);
        Assert.Contains(states, row => row.StatisticType == SalesStatisticType.StoreSales
            && row.Status == SalesStatisticRefreshStatus.Failed);
        Assert.Equal(ScheduledTaskLeaseStatus.Failed, lease!.Status);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2026仍只更新分店统计()
    {
        var requestDate = new DateTime(2026, 4, 12, 19, 15, 0);
        await SeedSaleAsync(
            "POSM-STORE-2026",
            "POSM-STORE-2026-DETAIL",
            "P-STORE-2026",
            "1004",
            requestDate,
            1,
            10m,
            "200"
        );

        await CreateService().UpdateStoreStatistics(requestDate);

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row =>
                row.Date >= requestDate.Date
                && row.Date < requestDate.Date.AddDays(1)
                && row.BranchCode == "1004"
            )
            .FirstAsync();
        var productCount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date >= requestDate.Date && row.Date < requestDate.Date.AddDays(1))
            .CountAsync();

        Assert.Equal(10m, store!.TotalAmount);
        Assert.Equal(0, productCount);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025应按分店累加HBSales和POSM()
    {
        var targetDate = new DateTime(2025, 4, 3);
        await SeedStoreAsync("1004", "测试分店");
        await SeedSaleAsync(
            "POSM-STORE-2025",
            "POSM-STORE-2025-DETAIL",
            "P-STORE-2025",
            "1004",
            targetDate.AddHours(9),
            1,
            10m,
            "200"
        );
        await SeedHBSalesAsync(33, targetDate, "HB-P-STORE", "1004", "200", 2m, 20m, "1");
        await SeedHBSalesAsync(34, targetDate, "HB-P-EXCLUDED", "1004", "200", 9m, 90m, "2");

        await CreateService().UpdateStoreStatistics(targetDate);

        var row = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal("测试分店", row!.BranchName);
        Assert.Equal(30m, row.TotalAmount);
        Assert.Equal(3, row.TotalQuantity);
        Assert.Equal(2, row.OrderCount);
        Assert.Equal(15m, row.AverageOrderValue);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025HBSales类型3和4应在数据库聚合时反向冲减()
    {
        var targetDate = new DateTime(2025, 4, 3);
        await SeedHBSalesAsync(36, targetDate, "HB-P-RETURN", "1004", "200", 1m, 10m, "3");
        await SeedHBSalesAsync(37, targetDate, "HB-P-RETURN", "1004", "200", 2m, 5m, "4");

        await CreateService().UpdateStoreStatistics(targetDate);

        var row = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .FirstAsync();

        Assert.NotNull(row);
        Assert.Equal(-15m, row!.TotalAmount);
        Assert.Equal(-3, row.TotalQuantity);
        Assert.Equal(2, row.OrderCount);
    }

    [Fact]
    public async Task UpdateStoreStatistics_2025同一订单分店码尾空格应只计算一个订单()
    {
        var targetDate = new DateTime(2025, 4, 3);
        await SeedHBSalesAsync(39, targetDate, "HB-P-SPACE-1", "1004", "200", 1m, 10m, "1");
        await _hbSalesDb.Insertable(new SalesOrderDetailRecord
        {
            ID = 40,
            B销售单号 = "HB-ORDER-39",
            B分店代码 = "1004 ",
            B结账日期 = targetDate,
            B产品编号 = "HB-P-SPACE-2",
            B供应商ID = "200",
            B数量 = 1m,
            B合计金额 = 5m,
        }).ExecuteCommandAsync();

        await CreateService().UpdateStoreStatistics(targetDate);

        var row = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1004")
            .FirstAsync();
        Assert.NotNull(row);
        Assert.Equal(15m, row!.TotalAmount);
        Assert.Equal(2, row.TotalQuantity);
        Assert.Equal(1, row.OrderCount);
    }

    [Fact]
    public async Task UpdateStoreStatisticsWithContext_来源已空时应删除旧分店统计()
    {
        var targetDate = new DateTime(2025, 4, 3);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 99m, 9);
        var method = typeof(SalesStatisticsJobService).GetMethod(
            "UpdateStoreStatisticsWithContext",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        var task = (Task)method!.Invoke(
            CreateService(),
            new object?[]
            {
                CreateSqlSugarContext(_localDb),
                CreatePosmSqlSugarContext(_posmDb),
                new HBSalesRecordSqlSugarContext(_hbSalesDb),
                NullLogger<SalesStatisticsJobService>.Instance,
                targetDate,
                null,
            }
        )!;

        await task;

        Assert.Equal(0, await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate)
            .CountAsync());
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025业务失败时应保留两张旧表并成对标记Failed()
    {
        var targetDate = new DateTime(2025, 4, 3);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1004",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedHBSalesAsync(38, targetDate, "HB-P-NO-BRANCH", null, "200", 1m, 10m, "1");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UpdateProductStoreDailyStatistics(targetDate)
        );

        var store = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var product = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "OLD-PRODUCT")
            .FirstAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Contains("分店编码", error.Message);
        Assert.Equal(99m, store!.TotalAmount);
        Assert.Equal(99m, product!.TotalAmount);
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.ProductStoreDaily
            && state.Status == SalesStatisticRefreshStatus.Failed
        );
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.StoreSales
            && state.Status == SalesStatisticRefreshStatus.Failed
        );
    }

    [Fact]
    public async Task QueryDailySourceWatermarkAsync_2025应在数据库聚合HBSales水位()
    {
        var targetDate = new DateTime(2025, 4, 3);
        var modifiedAt = targetDate.AddHours(15);
        await SeedHBSalesAsync(
            35,
            targetDate,
            "HB-P-WATERMARK",
            "1004",
            "200",
            1m,
            10m,
            "1",
            modifiedAt
        );
        var method = typeof(SalesStatisticsJobService).GetMethod(
            "QueryDailySourceWatermarkAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        var task = (Task<DateTime?>)method!.Invoke(
            null,
            new object?[]
            {
                CreatePosmSqlSugarContext(_posmDb),
                new HBSalesRecordSqlSugarContext(_hbSalesDb),
                targetDate,
                null,
            }
        )!;

        Assert.Equal(modifiedAt, await task);
    }

    [Fact]
    public async Task Update2025StoreAndProductStatisticsAtomically_预加载HBSales水位应独立取四个原始时间最大值()
    {
        var targetDate = new DateTime(2025, 4, 3);
        var expectedWatermark = targetDate.AddHours(12);
        await SeedHBSalesAsync(
            36,
            targetDate,
            "HB-P-RAW-WATERMARK",
            "1004",
            "200",
            1m,
            10m,
            "1",
            targetDate.AddHours(9)
        );

        var main = await _hbSalesDb.Queryable<SalesOrderMain>()
            .Where(row => row.ID == 36)
            .FirstAsync();
        var detail = await _hbSalesDb.Queryable<SalesOrderDetailRecord>()
            .Where(row => row.ID == 36)
            .FirstAsync();
        main!.FGC_LastModifyDate = targetDate.AddHours(9);
        main.FGC_CreateDate = targetDate.AddHours(10);
        detail!.FGC_LastModifyDate = targetDate.AddHours(11);
        detail.FGC_CreateDate = expectedWatermark;
        await _hbSalesDb.Updateable(main).ExecuteCommandAsync();
        await _hbSalesDb.Updateable(detail).ExecuteCommandAsync();

        // 调用方水位取四列最大值；若误用两个 coalesce 后字段会得到 11:00 并在这里错误拒绝。
        await InvokeUpdate2025StoreAndProductStatisticsAtomicallyAsync(
            CreateService(),
            targetDate,
            expectedWatermark
        );

        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.ProductStoreDaily
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.LastSourceUploadTime == expectedWatermark
        );
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.StoreSales
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.LastSourceUploadTime == expectedWatermark
        );
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025POSM明细或支付晚于订单头时两类状态应使用同一水位()
    {
        var targetDate = new DateTime(2025, 4, 3);
        var effectiveSourceWatermark = targetDate.AddHours(18);
        await SeedOrderAsync("POSM-LATE-WATERMARK", "1004", targetDate.AddHours(9), 1);
        await SeedSaleDetailAsync("POSM-LATE-WATERMARK", "POSM-LATE-WATERMARK-DETAIL", "P-LATE-WATERMARK", 1, 10m, "200");
        await SeedPaymentAsync("POSM-LATE-WATERMARK-PAY", "POSM-LATE-WATERMARK", 10m, targetDate.AddHours(17));
        var detail = await _posmDb.Queryable<SalesOrderDetail>()
            .Where(row => row.OrderDetailGuid == "POSM-LATE-WATERMARK-DETAIL")
            .FirstAsync();
        detail!.LastUploadTime = effectiveSourceWatermark;
        await _posmDb.Updateable(detail).ExecuteCommandAsync();

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.ProductStoreDaily
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.LastSourceUploadTime == effectiveSourceWatermark
        );
        Assert.Contains(states, state =>
            state.StatisticType == SalesStatisticType.StoreSales
            && state.Status == SalesStatisticRefreshStatus.Fresh
            && state.LastSourceUploadTime == effectiveSourceWatermark
        );
    }

    [Fact]
    public async Task Update2025StoreAndProductStatisticsAtomically_来源水位变化应成对Failed并保留旧双表()
    {
        var targetDate = new DateTime(2025, 4, 24);
        var callerWatermark = targetDate.AddHours(8);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1004",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedHBSalesAsync(
            440,
            targetDate,
            "NEW-PRODUCT",
            "1004",
            "200",
            1m,
            10m,
            "1",
            callerWatermark.AddMinutes(1)
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeUpdate2025StoreAndProductStatisticsAtomicallyAsync(
                CreateService(),
                targetDate,
                callerWatermark
            )
        );

        var oldStore = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var oldProduct = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "OLD-PRODUCT")
            .FirstAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Contains("水位", error.Message);
        Assert.Equal(99m, oldStore!.TotalAmount);
        Assert.Equal(99m, oldProduct!.TotalAmount);
        Assert.Contains(states, state => state.StatisticType == SalesStatisticType.ProductStoreDaily
            && state.Status == SalesStatisticRefreshStatus.Failed);
        Assert.Contains(states, state => state.StatisticType == SalesStatisticType.StoreSales
            && state.Status == SalesStatisticRefreshStatus.Failed);
    }

    [Fact]
    public async Task Update2025StoreAndProductStatisticsAtomically_构建期间水位变化应拒绝提交并保留旧双表()
    {
        var targetDate = new DateTime(2025, 4, 25);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 99m, 9);
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = targetDate,
            BranchCode = "1004",
            SupplierCode = "200",
            ProductCode = "OLD-PRODUCT",
            TotalQuantity = 9,
            TotalAmount = 99m,
            OrderCount = 1,
        }).ExecuteCommandAsync();
        await SeedHBSalesAsync(
            441,
            targetDate,
            "BUILD-PRODUCT",
            "1004",
            "200",
            1m,
            10m,
            "1",
            targetDate.AddHours(8)
        );

        var driftInserted = 0;
        _localDb.Aop.OnLogExecuting = (sql, _) =>
        {
            if (!sql.Contains("Product", StringComparison.OrdinalIgnoreCase)
                || Interlocked.Exchange(ref driftInserted, 1) != 0)
            {
                return;
            }

            using var driftDb = new SqlSugarClient(
                CreateConnectionConfig($"Data Source={_hbSalesDbPath}")
            );
            driftDb.Insertable(new SalesOrderMain
            {
                ID = 442,
                B销售单号 = "HB-ORDER-442",
                B分店代码 = "1004",
                B单据类型 = "1",
                B结账日期 = targetDate,
                FGC_LastModifyDate = targetDate.AddHours(9),
            }).ExecuteCommand();
            driftDb.Insertable(new SalesOrderDetailRecord
            {
                ID = 442,
                B销售单号 = "HB-ORDER-442",
                B分店代码 = "1004",
                B结账日期 = targetDate,
                B产品编号 = "LATE-PRODUCT",
                B供应商ID = "200",
                B数量 = 1m,
                B合计金额 = 20m,
                FGC_LastModifyDate = targetDate.AddHours(9),
            }).ExecuteCommand();
        };

        InvalidOperationException error;
        try
        {
            error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService().UpdateProductStoreDailyStatistics(targetDate)
            );
        }
        finally
        {
            _localDb.Aop.OnLogExecuting = null;
        }

        var oldStore = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == targetDate && row.BranchCode == "1004")
            .FirstAsync();
        var oldProduct = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date == targetDate && row.ProductCode == "OLD-PRODUCT")
            .FirstAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.Date == targetDate)
            .ToListAsync();

        Assert.Contains("构建期间来源水位发生变化", error.Message);
        Assert.Equal(99m, oldStore!.TotalAmount);
        Assert.Equal(99m, oldProduct!.TotalAmount);
        Assert.Contains(states, state => state.StatisticType == SalesStatisticType.ProductStoreDaily
            && state.Status == SalesStatisticRefreshStatus.Failed);
        Assert.Contains(states, state => state.StatisticType == SalesStatisticType.StoreSales
            && state.Status == SalesStatisticRefreshStatus.Failed);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2025应累加HBSales和POSM且HBSales不走支付分摊()
    {
        var targetDate = new DateTime(2025, 4, 4);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 100m, 3);
        await SeedOrderAsync("POSM-2025-COMBINED", "1004", targetDate.AddHours(9), 1);
        await SeedSaleDetailAsync("POSM-2025-COMBINED", "POSM-2025-COMBINED-DETAIL", "P-COMBINED", 1, 100m, "200");
        await SeedPaymentAsync("POSM-2025-COMBINED-PAY", "POSM-2025-COMBINED", 80m, targetDate.AddHours(9).AddMinutes(1));
        await SeedHBSalesAsync(5, targetDate, "P-COMBINED", "1004", "200", 2m, 20m, "1", targetDate.AddHours(12));

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.ProductCode == "P-COMBINED")
            .FirstAsync();
        var states = await _localDb.Queryable<SalesStatisticRefreshState>()
            .Where(state => state.Date == targetDate)
            .ToListAsync();
        var productState = states.First(state =>
            state.StatisticType == SalesStatisticType.ProductStoreDaily
        );
        var storeState = states.First(state =>
            state.StatisticType == SalesStatisticType.StoreSales
        );
        Assert.NotNull(row);
        Assert.Equal(3, row!.TotalQuantity);
        Assert.Equal(100m, row.TotalAmount);
        // 2025 原子刷新以四类来源的最大水位落两类状态，不能只取 HBSales 水位。
        Assert.Equal(storeState.LastSourceUploadTime, productState.LastSourceUploadTime);
        Assert.True(productState.LastSourceUploadTime >= targetDate.AddHours(12));
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_2026不应读取HBSales()
    {
        var targetDate = new DateTime(2026, 4, 5);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 10m, 1);
        await SeedSaleAsync("POSM-2026", "POSM-2026-DETAIL", "P-2026", "1004", targetDate.AddHours(9), 1, 10m, "200");
        await SeedHBSalesAsync(6, targetDate, "P-2026", "1004", "200", 9m, 99m, "1");

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var row = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.ProductCode == "P-2026")
            .FirstAsync();
        Assert.NotNull(row);
        Assert.Equal(1, row!.TotalQuantity);
        Assert.Equal(10m, row.TotalAmount);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_POSM支付金额分摊回归()
    {
        var targetDate = new DateTime(2026, 4, 6);
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 72.14m, 3);
        await SeedOrderAsync("POSM-ALLOC", "1004", targetDate.AddHours(9), 3);
        await SeedSaleDetailAsync("POSM-ALLOC", "POSM-ALLOC-1", "P-ALLOC-1", 1, 30m, "112");
        await SeedSaleDetailAsync("POSM-ALLOC", "POSM-ALLOC-2", "P-ALLOC-2", 2, 40m, "113");
        await SeedPaymentAsync("POSM-ALLOC-PAY", "POSM-ALLOC", 72.14m, targetDate.AddHours(9).AddMinutes(1));

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var totalAmount = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate)
            .SumAsync(x => x.TotalAmount);
        Assert.InRange(Math.Abs(totalAmount - 72.14m), 0m, 0.0001m);
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
    public async Task UpdateProductStoreDailyStatistics_老系统负数退货明细直接冲减商品统计()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-LEGACY-RETURN");
        await SeedSaleAsync(
            orderGuid: "ORDER-LEGACY-SALE",
            detailGuid: "DETAIL-LEGACY-SALE",
            productCode: "P-LEGACY-RETURN",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 2,
            actualAmount: 20m,
            supplierCode: "200"
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-LEGACY-RETURN",
            detailGuid: "DETAIL-LEGACY-RETURN",
            productCode: "P-LEGACY-RETURN",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            quantity: -1,
            actualAmount: -10m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 10m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var stat = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1018" && x.ProductCode == "P-LEGACY-RETURN")
            .FirstAsync();
        var state = await LoadRefreshStateAsync(targetDate);

        Assert.NotNull(stat);
        Assert.Equal(1, stat!.TotalQuantity);
        Assert.Equal(10m, stat.TotalAmount);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_补充退货分店没有营业额基准时仍应Failed()
    {
        var targetDate = new DateTime(2026, 5, 2);
        await SeedProductAsync("P-RETURN-MISSING-STORE");
        await SeedSaleAsync(
            orderGuid: "ORDER-RETURN-MISSING-STORE-SALE",
            detailGuid: "DETAIL-RETURN-MISSING-STORE-SALE",
            productCode: "P-RETURN-MISSING-STORE",
            branchCode: "1004",
            orderTime: targetDate.AddHours(9),
            quantity: 2,
            actualAmount: 20m,
            supplierCode: "200"
        );
        await SeedReturnRecordAsync(
            returnOrderGuid: "ORDER-RETURN-MISSING-STORE-RETURN",
            returnDetailGuid: "DETAIL-RETURN-MISSING-STORE-RETURN",
            originalOrderGuid: "ORDER-RETURN-MISSING-STORE-SALE",
            originalDetailGuid: "DETAIL-RETURN-MISSING-STORE-SALE",
            productCode: "P-RETURN-MISSING-STORE",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            returnQuantity: 1m,
            returnAmount: 10m
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1004", 20m, 2);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("1018", state.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("分店营业额统计缺失", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_旧库没有退货表时仍按明细表统计()
    {
        var targetDate = new DateTime(2026, 5, 1);
        _posmDb.DbMaintenance.DropTable<SalesReturnRecord>();
        await SeedProductAsync("P-LEGACY-NO-RETURN-TABLE");
        await SeedSaleAsync(
            orderGuid: "ORDER-LEGACY-NO-TABLE-SALE",
            detailGuid: "DETAIL-LEGACY-NO-TABLE-SALE",
            productCode: "P-LEGACY-NO-RETURN-TABLE",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 2,
            actualAmount: 20m,
            supplierCode: "200"
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-LEGACY-NO-TABLE-RETURN",
            detailGuid: "DETAIL-LEGACY-NO-TABLE-RETURN",
            productCode: "P-LEGACY-NO-RETURN-TABLE",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            quantity: -1,
            actualAmount: -10m,
            supplierCode: "200"
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 10m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var stat = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1018" && x.ProductCode == "P-LEGACY-NO-RETURN-TABLE")
            .FirstAsync();
        var state = await LoadRefreshStateAsync(targetDate);

        Assert.NotNull(stat);
        Assert.Equal(1, stat!.TotalQuantity);
        Assert.Equal(10m, stat.TotalAmount);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_新系统退货表补充冲减商品统计()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-NEW-RETURN");
        await SeedSaleAsync(
            orderGuid: "ORDER-NEW-SALE",
            detailGuid: "DETAIL-NEW-SALE",
            productCode: "P-NEW-RETURN",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 821,
            actualAmount: 3922.80m,
            supplierCode: "200"
        );
        await SeedReturnRecordAsync(
            returnOrderGuid: "ORDER-NEW-RETURN",
            returnDetailGuid: "DETAIL-NEW-RETURN",
            originalOrderGuid: "ORDER-NEW-SALE",
            originalDetailGuid: "DETAIL-NEW-SALE",
            productCode: "P-NEW-RETURN",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            returnQuantity: 19m,
            returnAmount: 110.40m
        );
        // 分店营业额保留销售毛额；商品统计会从补充退货表扣减为净额。
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 3922.80m, 821);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var stat = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1018" && x.ProductCode == "P-NEW-RETURN")
            .FirstAsync();
        var storeStat = await _localDb.Queryable<StoreSalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1018")
            .FirstAsync();
        var state = await LoadRefreshStateAsync(targetDate);

        Assert.NotNull(stat);
        Assert.Equal(802, stat!.TotalQuantity);
        Assert.Equal(3812.40m, stat.TotalAmount);
        Assert.NotNull(storeStat);
        Assert.Equal(821, storeStat!.TotalQuantity);
        Assert.Equal(3922.80m, storeStat.TotalAmount);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_扣除补充退货后仍超容差应Failed()
    {
        var targetDate = new DateTime(2026, 5, 3);
        await SeedProductAsync("P-RETURN-REAL-DIFF");
        await SeedSaleAsync(
            orderGuid: "ORDER-RETURN-REAL-DIFF-SALE",
            detailGuid: "DETAIL-RETURN-REAL-DIFF-SALE",
            productCode: "P-RETURN-REAL-DIFF",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 22,
            actualAmount: 220m,
            supplierCode: "200"
        );
        await SeedReturnRecordAsync(
            returnOrderGuid: "ORDER-RETURN-REAL-DIFF-RETURN",
            returnDetailGuid: "DETAIL-RETURN-REAL-DIFF-RETURN",
            originalOrderGuid: "ORDER-RETURN-REAL-DIFF-SALE",
            originalDetailGuid: "DETAIL-RETURN-REAL-DIFF-SALE",
            productCode: "P-RETURN-REAL-DIFF",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            returnQuantity: 11m,
            returnAmount: 110m
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 400m, 22);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var state = await LoadRefreshStateAsync(targetDate);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Failed, state!.Status);
        Assert.Contains("金额差 180", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_补充退货设备码带空格时与摘要使用同一分店()
    {
        var targetDate = new DateTime(2026, 5, 4);
        await SeedProductAsync("P-RETURN-DEVICE-TRIM");
        await SeedDeviceRegistrationAsync("DEVICE-RETURN-TRIM", "1018");
        await SeedSaleAsync(
            orderGuid: "ORDER-RETURN-DEVICE-TRIM-SALE",
            detailGuid: "DETAIL-RETURN-DEVICE-TRIM-SALE",
            productCode: "P-RETURN-DEVICE-TRIM",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 22,
            actualAmount: 220m,
            supplierCode: "200"
        );
        await SeedReturnRecordAsync(
            returnOrderGuid: "ORDER-RETURN-DEVICE-TRIM-RETURN",
            returnDetailGuid: "DETAIL-RETURN-DEVICE-TRIM-RETURN",
            originalOrderGuid: "ORDER-RETURN-DEVICE-TRIM-SALE",
            originalDetailGuid: "DETAIL-RETURN-DEVICE-TRIM-SALE",
            productCode: "P-RETURN-DEVICE-TRIM",
            branchCode: null,
            orderTime: targetDate.AddHours(10),
            returnQuantity: 11m,
            returnAmount: 110m,
            deviceCode: " DEVICE-RETURN-TRIM "
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 220m, 22);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var stat = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row =>
                row.Date == targetDate
                && row.BranchCode == "1018"
                && row.ProductCode == "P-RETURN-DEVICE-TRIM"
            )
            .FirstAsync();
        var state = await LoadRefreshStateAsync(targetDate);

        Assert.NotNull(stat);
        Assert.Equal(11, stat!.TotalQuantity);
        Assert.Equal(110m, stat.TotalAmount);
        Assert.NotNull(state);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
    }

    [Fact]
    public async Task UpdateProductStoreDailyStatistics_双表同一退货明细不重复冲减()
    {
        var targetDate = new DateTime(2026, 5, 1);
        await SeedProductAsync("P-DUP-RETURN");
        await SeedSaleAsync(
            orderGuid: "ORDER-DUP-SALE",
            detailGuid: "DETAIL-DUP-SALE",
            productCode: "P-DUP-RETURN",
            branchCode: "1018",
            orderTime: targetDate.AddHours(9),
            quantity: 2,
            actualAmount: 20m,
            supplierCode: "200"
        );
        await SeedSaleAsync(
            orderGuid: "ORDER-DUP-RETURN",
            detailGuid: "DETAIL-DUP-RETURN",
            productCode: "P-DUP-RETURN",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            quantity: -1,
            actualAmount: -10m,
            supplierCode: "200"
        );
        await SeedReturnRecordAsync(
            returnOrderGuid: "ORDER-DUP-RETURN",
            returnDetailGuid: "DETAIL-DUP-RETURN",
            originalOrderGuid: "ORDER-DUP-SALE",
            originalDetailGuid: "DETAIL-DUP-SALE",
            productCode: "P-DUP-RETURN",
            branchCode: "1018",
            orderTime: targetDate.AddHours(10),
            returnQuantity: 1m,
            returnAmount: 10m,
            insertOrder: false
        );
        await SeedStoreSalesStatisticAsync(targetDate, "1018", 10m, 1);

        await CreateService().UpdateProductStoreDailyStatistics(targetDate);

        var stat = await _localDb.Queryable<ProductStoreDailySalesStatistic>()
            .Where(x => x.Date == targetDate && x.BranchCode == "1018" && x.ProductCode == "P-DUP-RETURN")
            .FirstAsync();
        var state = await LoadRefreshStateAsync(targetDate);

        Assert.NotNull(stat);
        Assert.Equal(1, stat!.TotalQuantity);
        Assert.Equal(10m, stat.TotalAmount);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state!.Status);
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
    public async Task SubmitProductStoreDailyRecalculationAsync_允许31个唯一日期但拒绝32个()
    {
        var service = CreateService();
        var firstDate = new DateTime(2026, 6, 1);
        var thirtyOneDates = Enumerable.Range(0, 31).Select(offset => firstDate.AddDays(offset)).ToList();

        var accepted = await service.SubmitProductStoreDailyRecalculationAsync(
            thirtyOneDates.Concat(thirtyOneDates),
            "admin"
        );

        Assert.Equal(31, accepted.SubmittedDates.Count);
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitProductStoreDailyRecalculationAsync(
                Enumerable.Range(0, 32).Select(offset => firstDate.AddDays(offset)),
                "admin"
            )
        );
        Assert.Contains("一次最多重算 31 天", error.Message);
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
    public void ProductRecalculation_Submit与Run范围包含2025时应强制单并发()
    {
        var resolver = typeof(SalesStatisticsJobService).GetMethod(
            "ResolveProductStatisticMaxConcurrency",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(resolver);

        Assert.Equal(1, Assert.IsType<int>(resolver!.Invoke(
            null,
            new object[] { new[] { new DateTime(2025, 12, 31), new DateTime(2026, 1, 1) }, 8 }
        )));
        Assert.Equal(4, Assert.IsType<int>(resolver.Invoke(
            null,
            new object[] { new[] { new DateTime(2026, 1, 1), new DateTime(2026, 1, 2) }, 4 }
        )));
    }

    [Fact]
    public void FullRefreshConcurrent与ByMonths_范围包含2025时应强制单并发()
    {
        var resolver = typeof(SalesStatisticsJobService).GetMethod(
            "ResolveFullRefreshMaxConcurrency",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(resolver);

        Assert.Equal(1, Assert.IsType<int>(resolver!.Invoke(
            null,
            new object[] { new DateTime(2024, 12, 1), new DateTime(2026, 1, 1), 8 }
        )));
        Assert.Equal(6, Assert.IsType<int>(resolver.Invoke(
            null,
            new object[] { new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), 6 }
        )));
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

    private SalesStatisticsJobService CreateService(IServiceScopeFactory? serviceScopeFactory = null)
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
            serviceScopeFactory ?? Mock.Of<IServiceScopeFactory>(),
            new HBSalesRecordSqlSugarContext(_hbSalesDb)
        );
    }

    private ServiceProvider CreateRollingRefreshServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<SqlSugarContext>(_ => CreateSqlSugarContext(_localDb));
        services.AddScoped<POSMSqlSugarContext>(_ => CreatePosmSqlSugarContext(_posmDb));
        services.AddScoped<HBSalesRecordSqlSugarContext>(_ => new HBSalesRecordSqlSugarContext(_hbSalesDb));
        services.AddScoped<ILogger<SalesStatisticsJobService>>(_ =>
            NullLogger<SalesStatisticsJobService>.Instance
        );
        services.AddScoped<ILogger<ScheduledTaskLeaseService>>(_ =>
            NullLogger<ScheduledTaskLeaseService>.Instance
        );
        services.Configure<ScheduledTaskOptions>(_ => { });
        services.AddScoped<ScheduledTaskLeaseService>();
        return services.BuildServiceProvider();
    }

    private static async Task<bool> InvokeRunLeasedProductStoreDailyRefreshAsync(
        SalesStatisticsJobService service,
        DateTime date
    )
    {
        var method = typeof(SalesStatisticsJobService).GetMethod(
            "RunLeasedProductStoreDailyRefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var task = method!.Invoke(service, new object[] { date }) as Task<bool>;
        Assert.NotNull(task);
        return await task!;
    }

    private async Task InvokeUpdate2025StoreAndProductStatisticsAtomicallyAsync(
        SalesStatisticsJobService service,
        DateTime date,
        DateTime? sourceWatermarkOverride
    )
    {
        var method = typeof(SalesStatisticsJobService).GetMethod(
            "Update2025StoreAndProductStatisticsAtomically",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var task = method!.Invoke(
            service,
            new object?[]
            {
                CreateSqlSugarContext(_localDb),
                CreatePosmSqlSugarContext(_posmDb),
                new HBSalesRecordSqlSugarContext(_hbSalesDb),
                NullLogger<SalesStatisticsJobService>.Instance,
                date,
                sourceWatermarkOverride,
                null,
                null,
                false,
                null,
            }
        ) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private async Task SeedHBSalesAsync(
        int id,
        DateTime date,
        string? productCode,
        string? branchCode,
        string supplierCode,
        decimal quantity,
        decimal amount,
        string? documentType,
        DateTime? modifiedAt = null,
        string? barcode = null,
        bool useDefaultBarcode = true,
        string? itemNumber = null,
        DateTime? mainCheckoutDate = null
    )
    {
        var salesOrderNo = $"HB-ORDER-{id}";
        await _hbSalesDb.Insertable(new SalesOrderMain
        {
            ID = id,
            B销售单号 = salesOrderNo,
            B分店代码 = branchCode,
            B单据类型 = documentType,
            // 主表索引窗口以结账日期为入口；未特化的测试数据保持与明细日期一致。
            B结账日期 = mainCheckoutDate ?? date,
            FGC_LastModifyDate = modifiedAt,
        }).ExecuteCommandAsync();
        await _hbSalesDb.Insertable(new SalesOrderDetailRecord
        {
            ID = id,
            B销售单号 = salesOrderNo,
            B分店代码 = branchCode,
            B结账日期 = date.Date,
            B产品编号 = productCode,
            B供应商ID = supplierCode,
            B货号 = itemNumber,
            B商品名 = $"{productCode} 名称",
            B条形码 = useDefaultBarcode ? barcode ?? $"{productCode}-BAR" : barcode,
            B数量 = quantity,
            B合计金额 = amount,
            FGC_LastModifyDate = modifiedAt,
        }).ExecuteCommandAsync();
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

    private async Task SeedReturnRecordAsync(
        string returnOrderGuid,
        string returnDetailGuid,
        string originalOrderGuid,
        string originalDetailGuid,
        string productCode,
        string? branchCode,
        DateTime orderTime,
        decimal returnQuantity,
        decimal returnAmount,
        bool insertOrder = true,
        string? deviceCode = null
    )
    {
        if (insertOrder)
        {
            await _posmDb.Insertable(new SalesOrder
            {
                OrderGuid = returnOrderGuid,
                BranchCode = branchCode,
                DeviceCode = deviceCode,
                OrderTime = orderTime,
                Status = 1,
                LastUploadTime = orderTime.AddMinutes(5),
            }).ExecuteCommandAsync();
        }

        await _posmDb.Insertable(new SalesReturnRecord
        {
            ReturnDetailGuid = returnDetailGuid,
            ReturnOrderGuid = returnOrderGuid,
            OriginalOrderGuid = originalOrderGuid,
            OriginalOrderDetailGuid = originalDetailGuid,
            ProductCode = productCode,
            ReturnQuantity = returnQuantity,
            ReturnAmount = returnAmount,
            CreatedTime = orderTime.AddMinutes(6),
            UpdatedTime = orderTime.AddMinutes(7),
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

    private async Task SeedProductLookupAsync(
        string productCode,
        string itemNumber,
        string barcode,
        bool isActive = true,
        bool isDeleted = false
    )
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"{productCode}-lookup-uuid",
            ProductCode = productCode,
            ItemNumber = itemNumber,
            Barcode = barcode,
            ProductName = productCode,
            LocalSupplierCode = "200",
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductSetCodeAsync(
        string productCode,
        string setBarcode,
        bool isActive = true,
        bool isDeleted = false
    )
    {
        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = $"{productCode}-{setBarcode}-set",
            ProductCode = productCode,
            SetProductCode = productCode,
            SetItemNumber = productCode,
            SetBarcode = setBarcode,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreMultiCodeProductAsync(
        string storeCode,
        string productCode,
        string multiBarcode,
        bool isActive = true,
        bool isDeleted = false
    )
    {
        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = $"{storeCode}-{productCode}-{multiBarcode}-multi",
            StoreCode = storeCode,
            ProductCode = productCode,
            MultiCodeProductCode = productCode,
            StoreMultiCodeProductCode = productCode,
            MultiBarcode = multiBarcode,
            IsActive = isActive,
            IsDeleted = isDeleted,
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
        string? errorMessage = null,
        DateTime? lastSourceUploadTime = null,
        string statisticType = SalesStatisticType.ProductStoreDaily
    )
    {
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = statisticType,
            Date = date.Date,
            Status = status,
            SourceTimeZone = "POSM_LOCAL",
            JobId = jobId,
            RequestedBy = jobId == null ? null : "admin",
            RequestedAtUtc = requestedAtUtc,
            StartedAtUtc = startedAtUtc,
            LastCheckedAtUtc = lastCheckedAtUtc ?? requestedAtUtc ?? startedAtUtc,
            ErrorMessage = errorMessage,
            LastSourceUploadTime = lastSourceUploadTime,
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
        _hbSalesDb.Dispose();
        _hbSalesConnection.Dispose();
        if (File.Exists(_localDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        if (File.Exists(_posmDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
        if (File.Exists(_hbSalesDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_hbSalesDbPath);
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
