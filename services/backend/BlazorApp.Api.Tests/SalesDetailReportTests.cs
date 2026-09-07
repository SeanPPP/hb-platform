using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// SalesDetail 一次聚合读取的本地行为契约。测试只使用两个 SQLite 文件，不访问远程数据库。
/// </summary>
public sealed class SalesDetailReportTests : IDisposable
{
    private readonly string _localPath = Path.Combine(Path.GetTempPath(), $"sales-detail-report-local-{Guid.NewGuid():N}.db");
    private readonly string _posmPath = Path.Combine(Path.GetTempPath(), $"sales-detail-report-posm-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;
    private readonly HashSet<DateTime> _states = new();

    public SalesDetailReportTests()
    {
        _localConnection = new SqliteConnection($"Data Source={_localPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmPath}");
        _localConnection.Open();
        _posmConnection.Open();
        _localDb = new SqlSugarClient(Config(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(Config(_posmConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(new[]
        {
            typeof(Store), typeof(HBLocalSupplier), typeof(ChinaSupplier), typeof(Product),
            typeof(ProductStoreDailySalesStatistic), typeof(SalesStatisticRefreshState),
        });
        _posmDb.CodeFirst.InitTables(new[] { typeof(PosmProductSupplierMapping) });
    }

    [Fact]
    public async Task 同一天本期同期仍各保留一份并按数量分页排序()
    {
        var day = new DateTime(2026, 7, 1);
        await SeedCatalogAsync("CN1", "国内一");
        await SeedProductAsync("P-HIGH");
        await SeedStatisticAsync(day, "S1", "200", "P-HIGH", 20, 200m);
        await SeedMappingAsync("P-HIGH", "CN1");

        var response = await CreateService().GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.China, new() { "S1" }, pageSize: 1,
            sections: new[] { SalesDetailSection.Products });

        Assert.NotNull(response.Data);
        var row = Assert.Single(response.Data!.Products!.Rows);
        Assert.Equal(200m, row.Revenue);
        Assert.Equal(200m, row.CompareRevenue);
        Assert.Equal(20, row.Quantity);
        Assert.Equal(20, row.CompareQuantity);
        Assert.Equal(1, response.Data.Products.Total);

        var englishSearch = await CreateService().GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.China, new() { "S1" }, search: "English",
            sections: new[] { SalesDetailSection.Products });
        Assert.Equal("P-HIGH", Assert.Single(englishSearch.Data!.Products!.Rows).Code);
    }

    [Fact]
    public async Task 四栏反向筛选分母和客单字段保持业务口径()
    {
        var day = new DateTime(2026, 7, 2);
        await _localDb.Insertable(new HBLocalSupplier { Guid = "local-200", LocalSupplierCode = "200", Name = "HB仓库" }).ExecuteCommandAsync();
        await SeedCatalogAsync("CN1", "国内一");
        await SeedCatalogAsync("CN2", "国内二");
        await SeedProductAsync("P-HIGH");
        await SeedProductAsync("P-LOW");
        await SeedStatisticAsync(day, "S1", "200", "P-HIGH", 20, 200m);
        await SeedStatisticAsync(day, "S1", "200", "P-LOW", 5, 50m);
        await SeedStatisticAsync(day, "S1", "A1", "P-HIGH", 10, 100m);
        await SeedMappingAsync("P-HIGH", "CN1");
        await SeedMappingAsync("P-LOW", "CN1");

        var service = CreateService();
        var china = await service.GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.China, new() { "S1" }, selectedSupplierCode: "CN1",
            selectedProductCode: "P-HIGH", pageSize: 1);

        Assert.Equal(SalesStatisticRefreshStatus.Fresh, china.StatisticStatus);
        Assert.NotNull(china.Data!.Suppliers);
        Assert.All(china.Data.Suppliers!.Rows, row => Assert.Equal(1, row.OrderCount));
        Assert.All(china.Data.Suppliers.Rows, row => Assert.Equal(200m, row.AverageTransaction));
        var cn1 = Assert.Single(china.Data.Suppliers.Rows);
        Assert.Equal("CN1", cn1.Code);
        Assert.Equal("国内一", cn1.Name);
        var branch = Assert.Single(china.Data.Branches!.Rows);
        Assert.Equal(1, branch.OrderCount);
        Assert.Equal(200m, branch.AverageTransaction);
        Assert.Equal(200m / 250m, cn1.Share!.Value, 6); // 国内标签的供应商占国内分母。
        Assert.Equal(200m / 350m, cn1.ChinaShare!.Value, 6);

        var productPage = china.Data.Products!;
        Assert.Equal("P-HIGH", Assert.Single(productPage.Rows).Code);
        Assert.Equal(1, china.Data.Summary!.Summary!.OrderCount);
        Assert.Equal(200m, china.Data.Summary.Summary.AverageTransaction);

        // 未锁定商品时只有日汇总的订单行数，不能把跨商品订单数相加后冒充去重结果。
        var unscoped = await service.GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.China, new() { "S1" }, selectedSupplierCode: "CN1",
            sections: new[] { SalesDetailSection.Summary });
        Assert.Null(unscoped.Data!.Summary!.Summary!.OrderCount);
        Assert.Null(unscoped.Data.Summary.Summary.AverageTransaction);

        var australia = await service.GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, new() { "S1" }, pageSize: 1,
            sections: new[] { SalesDetailSection.Products });
        Assert.Equal("P-HIGH", Assert.Single(australia.Data!.Products!.Rows).Code);
        Assert.Equal(30, australia.Data.Products.Rows[0].Quantity);
        var australiaSuppliers = await service.GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, new() { "S1" }, sections: new[] { SalesDetailSection.Suppliers });
        var localSupplier = Assert.Single(australiaSuppliers.Data!.Suppliers!.Rows, row => row.Code == "200");
        Assert.Equal("HB仓库", localSupplier.Name);
    }

    [Fact]
    public async Task 无销售行但统计状态完整时返回空分页而非未准备()
    {
        var day = new DateTime(2026, 7, 3);
        await SeedStateAsync(day);
        var response = await CreateService().GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, new() { "S1" });

        Assert.Equal(SalesStatisticRefreshStatus.Fresh, response.StatisticStatus);
        Assert.Empty(response.Data!.Products!.Rows);
        Assert.Equal(0, response.Data.Products.Total);
        Assert.Equal(0m, response.Data.Products.Summary!.Revenue);
    }

    [Fact]
    public async Task 统计水位按Utc序列化并保留Z后缀()
    {
        var day = new DateTime(2026, 9, 7);
        var completedAtUtc = new DateTime(2026, 9, 7, 3, 32, 59, DateTimeKind.Utc);
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            Date = day,
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Status = SalesStatisticRefreshStatus.Fresh,
            LastAggregatedAtUtc = completedAtUtc,
            CompletedAtUtc = completedAtUtc,
            SourceProductVersion = "utc-version",
        }).ExecuteCommandAsync();

        var response = await CreateService().GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, new() { "S1" });

        Assert.Equal(DateTimeKind.Utc, response.StatisticUpdatedAt!.Value.Kind);
        Assert.Contains("2026-09-07T03:32:59Z", JsonSerializer.Serialize(response));
    }

    [Fact]
    public void SQLServerBuilder先聚合窄事实再延迟连接元信息()
    {
        var method = typeof(SalesDashboardReactService).GetMethod(
            "BuildSalesDetailReportSql",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var sql = Assert.IsType<string>(method.Invoke(null, new object?[]
        {
            true,
            null,
            Range(new DateTime(2026, 7, 4), new DateTime(2026, 7, 4)),
            SalesDetailKind.China,
            new List<string> { "S1" },
            null,
            null,
            null,
            "English barcode",
            1,
            20,
            Enum.GetValues<SalesDetailSection>().ToHashSet(),
            null,
        }));

        var groupBy = sql.IndexOf("GROUP BY [Period], [RawSupplierCode]", StringComparison.Ordinal);
        var productJoin = sql.IndexOf("OUTER APPLY (SELECT TOP (1) p0.[ProductName]", StringComparison.Ordinal);
        Assert.True(groupBy >= 0);
        Assert.True(productJoin > groupBy);
        Assert.DoesNotContain("GROUP BY [Period], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode], [BranchCode], [ProductCode], [ProductImage]", sql);
        Assert.DoesNotContain("GROUP BY [Period], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode], [BranchCode], [ProductCode], [EnglishName]", sql);
        Assert.Contains("MAX([StatisticProductName]) [StatisticProductName]", sql);
        Assert.Contains("MAX([StatisticBarcode]) [StatisticBarcode]", sql);
        Assert.Contains("p0.[Barcode]", sql);
        Assert.Contains("[StatisticBarcode] LIKE @sdrSearch0", sql);
        Assert.Contains("pSearch.[Barcode] LIKE @sdrSearch0", sql);
        Assert.Contains("EXISTS (SELECT 1 FROM [Product] pSearch", sql);
        Assert.Contains("NULLIF(LTRIM(RTRIM(local.[Name])), '')", sql);
        Assert.Contains("THEN 'hotbargain' ELSE f.[AustralianSupplierCode] END", sql);
        Assert.Contains("COUNT([TotalCost]) [CostedRowCount]", sql);
        Assert.Contains("ORDER BY a.[Quantity] DESC, a.[CompareQuantity] DESC, a.[ProductCode] ASC", sql);
        Assert.Contains("DROP TABLE #SalesDetailFacts;", sql);

        var narrowSql = Assert.IsType<string>(method.Invoke(null, new object?[]
        {
            true,
            null,
            Range(new DateTime(2026, 7, 4), new DateTime(2026, 7, 4)),
            SalesDetailKind.China,
            new List<string> { "S1" },
            null,
            null,
            null,
            null,
            1,
            20,
            Enum.GetValues<SalesDetailSection>().ToHashSet(),
            null,
        }));
        Assert.DoesNotContain("COUNT(DISTINCT CASE", narrowSql);
        Assert.Contains("MIN(CASE WHEN [Period]=0 THEN [ProductCode] END)", narrowSql);
        Assert.Contains("SUM(s.[TotalAmount]) [Revenue]", narrowSql);
        Assert.Contains("GROUP BY periods.[Period]", narrowSql);
        Assert.Contains("CAST(NULL AS nvarchar(255)) [StatisticProductName]", narrowSql);
        Assert.Contains("stat.[StatisticProductName]", narrowSql);
        Assert.Contains("NULLIF(LTRIM(RTRIM(p.[ProductName])), '') IS NULL AND NULLIF(LTRIM(RTRIM(a.[StatisticProductName])), '') IS NULL", narrowSql);
        Assert.Contains("@sdrCompareStart", narrowSql);
        Assert.Contains("s0.[BranchCode] IN (@sdrBranch0)", narrowSql);
        Assert.Contains("SELECT MAX(sName.[StoreName])", narrowSql);
        Assert.Contains("ORDER BY [Revenue] DESC, [CompareRevenue] DESC, [Code] ASC", narrowSql);
        Assert.Contains("NULLIF(LTRIM(RTRIM((SELECT MAX(lName.[Name])", narrowSql);
        Assert.Contains("THEN 'hotbargain' ELSE f.[SupplierCode] END", narrowSql);
        Assert.DoesNotContain("LEFT JOIN [LocalSupplier]", narrowSql);
        Assert.DoesNotContain("LEFT JOIN [Store] store", narrowSql);
        Assert.Contains("ORDER BY a.[Quantity] DESC, a.[CompareQuantity] DESC, a.[ProductCode] ASC", narrowSql);
        Assert.Contains("GROUP BY GROUPING SETS", narrowSql);
        Assert.Contains("SELECT * INTO #SalesDetailAggregates FROM Grouped", narrowSql);
        Assert.Contains("FROM [#SalesDetailFacts] f WHERE [AustralianSupplierCode] IS NOT NULL AND f.[BranchCode] IN (@sdrBranch0)", narrowSql);
        Assert.Contains("DROP TABLE #SalesDetailAggregates;", narrowSql);
        Assert.Contains("SELECT COUNT(DISTINCT [ProductCode]) FROM #SalesDetailAggregates WHERE [GroupType]=3", narrowSql);

        var productsOnlySql = Assert.IsType<string>(method.Invoke(null, new object?[]
        {
            true,
            null,
            Range(new DateTime(2026, 7, 4), new DateTime(2026, 7, 4)),
            SalesDetailKind.China,
            new List<string> { "S1" },
            null,
            null,
            null,
            null,
            1,
            20,
            new HashSet<SalesDetailSection> { SalesDetailSection.Products },
            null,
        }));
        Assert.Contains("GROUP BY GROUPING SETS", productsOnlySql);
        Assert.Contains("SELECT TOP 0 CAST(NULL AS nvarchar(50)) [Code]", productsOnlySql);

        var hugePageSql = Assert.IsType<string>(method.Invoke(null, new object?[]
        {
            true,
            null,
            Range(new DateTime(2026, 7, 4), new DateTime(2026, 7, 4)),
            SalesDetailKind.China,
            new List<string> { "S1" },
            null,
            null,
            null,
            null,
            int.MaxValue,
            20,
            new HashSet<SalesDetailSection> { SalesDetailSection.Products },
            null,
        }));
        Assert.Contains("OFFSET 42949672920 ROWS", hugePageSql);

        var legacyHugePageSql = Assert.IsType<string>(method.Invoke(null, new object?[]
        {
            false,
            null,
            Range(new DateTime(2026, 7, 4), new DateTime(2026, 7, 4)),
            SalesDetailKind.China,
            new List<string> { "S1" },
            null,
            null,
            null,
            null,
            int.MaxValue,
            20,
            new HashSet<SalesDetailSection> { SalesDetailSection.Products },
            null,
        }));
        Assert.Contains("LIMIT 20 OFFSET 42949672920", legacyHugePageSql);
    }

    [Fact]
    public async Task 国内原始供应商在澳洲栏按本地供应商解析并支持名称搜索()
    {
        var day = new DateTime(2026, 7, 6);
        await _localDb.Insertable(new HBLocalSupplier { Guid = "local-200-search", LocalSupplierCode = "200", Name = "HB仓库" }).ExecuteCommandAsync();
        await SeedCatalogAsync("CN1", "国内一");
        await _localDb.Insertable(new Product
        {
            UUID = "uuid-local-search", ProductCode = "P-LOCAL", ProductName = "HB商品",
            EnglishName = "Local English", LocalSupplierCode = "200", ItemNumber = "local-item",
        }).ExecuteCommandAsync();
        await SeedStatisticAsync(day, "S1", "CN1", "P-LOCAL", 3, 30m);

        var service = CreateService();
        var byCode = await service.GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, new() { "S1" }, search: "200",
            sections: new[] { SalesDetailSection.Products });
        var byName = await service.GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, new() { "S1" }, search: "HB仓库",
            sections: new[] { SalesDetailSection.Products });

        Assert.Equal(SalesStatisticRefreshStatus.Fresh, byCode.StatisticStatus);
        Assert.Equal("P-LOCAL", Assert.Single(byCode.Data!.Products!.Rows).Code);
        Assert.Equal("P-LOCAL", Assert.Single(byName.Data!.Products!.Rows).Code);
    }

    [Theory]
    [InlineData(SalesStatisticRefreshStatus.Queued, true, "published", SalesStatisticRefreshStatus.Fresh)]
    [InlineData(SalesStatisticRefreshStatus.Running, true, "published", SalesStatisticRefreshStatus.Fresh)]
    [InlineData(SalesStatisticRefreshStatus.Queued, true, null, SalesStatisticRefreshStatus.Pending)]
    [InlineData(SalesStatisticRefreshStatus.Running, true, null, SalesStatisticRefreshStatus.Pending)]
    [InlineData(SalesStatisticRefreshStatus.Running, false, null, SalesStatisticRefreshStatus.Pending)]
    [InlineData(SalesStatisticRefreshStatus.Failed, true, "published", SalesStatisticRefreshStatus.Failed)]
    public async Task 单一商品快照按已发布状态读取且不受供应商汇总失败影响(string state, bool published, string? version, string expected)
    {
        var day = new DateTime(2026, 7, 7);
        await SeedProductAsync("P-PUBLISHED");
        await SeedStatisticAsync(day, "S1", "A1", "P-PUBLISHED", 3, 30m);
        await _localDb.Updateable<SalesStatisticRefreshState>()
            .SetColumns(row => new SalesStatisticRefreshState
            {
                Status = state,
                LastAggregatedAtUtc = published ? DateTime.UtcNow : null,
                CompletedAtUtc = published ? DateTime.UtcNow : null,
                SourceProductVersion = version,
            }).Where(row => row.Date == day && row.StatisticType == SalesStatisticType.ProductStoreDaily)
            .ExecuteCommandAsync();
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            Date = day, StatisticType = SalesStatisticType.AustralianSupplierStoreSales,
            Status = SalesStatisticRefreshStatus.Failed,
        }).ExecuteCommandAsync();

        var response = await CreateService().GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, new() { "S1" });
        Assert.Equal(expected, response.StatisticStatus);
        if (expected == SalesStatisticRefreshStatus.Fresh)
        {
            Assert.Equal(30m, response.Data!.Summary!.Summary!.Revenue);
            Assert.Equal("P-PUBLISHED", Assert.Single(response.Data.Products!.Rows).Code);
            var page = await CreateService().GetSalesDetailReportAsync(
                Range(day, day), SalesDetailKind.Australia, new() { "S1" },
                sections: new[] { SalesDetailSection.Products });
            Assert.Equal(response.CacheVersion, page.CacheVersion);
        }
        else Assert.Null(response.Data!.Products);
    }

    [Fact]
    public async Task 分店单商品客单保留且商品反查不要求额外选择供应商()
    {
        var day = new DateTime(2026, 7, 8);
        await SeedProductAsync("P-ONLY");
        await SeedStatisticAsync(day, "S1", "A1", "P-ONLY", 2, 20m);
        var service = CreateService();
        var all = await service.GetSalesDetailReportAsync(Range(day, day), SalesDetailKind.Australia);
        Assert.Equal(1, Assert.Single(all.Data!.Branches!.Rows).OrderCount);
        Assert.Null(all.Data.Branches.Summary!.OrderCount);
        var selected = await service.GetSalesDetailReportAsync(
            Range(day, day), SalesDetailKind.Australia, selectedProductCode: "P-ONLY");
        var row = Assert.Single(selected.Data!.Branches!.Rows);
        Assert.Equal(1, row.OrderCount);
        Assert.Equal(20m, row.AverageTransaction);
    }

    private static DateRangeDto Range(DateTime start, DateTime end) => new()
    {
        StartDate = start, EndDate = end, CompareStartDate = start, CompareEndDate = end,
    };

    private async Task SeedCatalogAsync(string code, string name)
        => await _localDb.Insertable(new ChinaSupplier { Guid = $"guid-{code}", SupplierCode = code, SupplierName = name }).ExecuteCommandAsync();

    private async Task SeedProductAsync(string code)
        => await _localDb.Insertable(new Product { UUID = $"uuid-{code}", ProductCode = code, ProductName = code, EnglishName = $"English {code}", ItemNumber = $"item-{code}" }).ExecuteCommandAsync();

    private async Task SeedStatisticAsync(DateTime date, string branch, string supplier, string product, int quantity, decimal amount)
    {
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date, BranchCode = branch, SupplierCode = supplier, ProductCode = product, ProductName = product,
            TotalQuantity = quantity, TotalAmount = amount, OrderCount = 1, CostSource = "Test", UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await SeedStateAsync(date);
    }

    private async Task SeedStateAsync(DateTime date)
    {
        if (!_states.Add(date.Date)) return;
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily, Date = date.Date,
            Status = SalesStatisticRefreshStatus.Fresh, LastAggregatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedMappingAsync(string product, string supplier)
        => await _posmDb.Insertable(new PosmProductSupplierMapping { ProductCode = product, LocalSupplierCode = "200", ChinaSupplierCode = supplier, LastUpdateTime = DateTime.UtcNow }).ExecuteCommandAsync();

    private SalesDashboardReactService CreateService()
    {
        var local = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(local, _localDb);
        var posm = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(posm, _posmDb);
        return new SalesDashboardReactService(local, posm, null!, NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions()), null, new ConfigurationBuilder().Build());
    }

    private static ConnectionConfig Config(string connectionString) => new()
    {
        ConnectionString = connectionString, DbType = DbType.Sqlite,
        IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
    };

    public void Dispose()
    {
        _localConnection.Dispose(); _posmConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localPath); SqliteTempFileCleanup.DeleteIfExists(_posmPath);
    }
}
