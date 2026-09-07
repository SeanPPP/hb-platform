using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDetailLookupReuseTests : IDisposable
{
    private readonly string _localPath = Path.Combine(Path.GetTempPath(), $"sales-detail-local-{Guid.NewGuid():N}.db");
    private readonly string _posmPath = Path.Combine(Path.GetTempPath(), $"sales-detail-posm-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;
    private readonly HashSet<DateTime> _seededRefreshDates = new();

    public SalesDetailLookupReuseTests()
    {
        _localConnection = new SqliteConnection($"Data Source={_localPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmPath}");
        _localConnection.Open();
        _posmConnection.Open();
        _localDb = new SqlSugarClient(Config(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(Config(_posmConnection.ConnectionString));
        _localDb.CodeFirst.InitTables<ChinaSupplier, Product, ProductStoreDailySalesStatistic, SalesStatisticRefreshState>();
        _posmDb.CodeFirst.InitTables<PosmProductSupplierMapping>();
    }

    [Fact]
    public async Task 单次商品明细读取只查询一次目录和映射并保留搜索及数量排序()
    {
        var date = new DateTime(2026, 7, 1);
        var compareDate = new DateTime(2025, 7, 1);
        await SeedCatalogAsync("CN-OLD", "旧国内供应商");
        await SeedCatalogAsync("CN-OTHER", "旧国内供应商");
        await SeedProductAsync("P-LOW");
        await SeedProductAsync("P-HIGH");
        await SeedStatisticAsync(date, "P-LOW", 10);
        await SeedStatisticAsync(date, "P-HIGH", 20);
        await SeedStatisticAsync(compareDate, "P-LOW", 2);
        await SeedStatisticAsync(compareDate, "P-HIGH", 1);
        await SeedMappingAsync("P-LOW", "CN-OLD");
        await SeedMappingAsync("P-HIGH", "CN-OLD");

        var catalogReads = 0;
        var mappingReads = 0;
        _localDb.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.Contains("ChinaSupplier", StringComparison.OrdinalIgnoreCase))
                catalogReads++;
        };
        _posmDb.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.Contains("posm_product_supplier_mapping", StringComparison.OrdinalIgnoreCase))
                mappingReads++;
        };

        var result = await CreateService().GetSalesDetailColumnsAsync(
            new DateRangeDto
            {
                StartDate = date,
                EndDate = date,
                CompareStartDate = compareDate,
                CompareEndDate = compareDate,
            },
            SalesDetailKind.China,
            SalesDetailSection.Products,
            branchCodes: new List<string> { "S1" },
            search: "旧国内供应商",
            pageSize: 1
        );

        Assert.Equal(1, catalogReads);
        Assert.Equal(1, mappingReads);
        Assert.Equal(2, result.Total);
        var row = Assert.Single(result.Rows);
        Assert.Equal("P-HIGH", row.Code);
        Assert.Equal(20, row.Quantity);
    }

    [Fact]
    public async Task 新请求重新读取映射并立即看见更新()
    {
        var date = new DateTime(2026, 7, 1);
        await SeedCatalogAsync("CN-OLD", "旧国内供应商");
        await SeedCatalogAsync("CN-NEW", "新国内供应商");
        await SeedProductAsync("P-UPDATE");
        await SeedStatisticAsync(date, "P-UPDATE", 7);
        await SeedMappingAsync("P-UPDATE", "CN-OLD");

        var range = new DateRangeDto { StartDate = date, EndDate = date };
        var service = CreateService();
        var first = await service.GetSalesDetailColumnsAsync(
            range,
            SalesDetailKind.China,
            SalesDetailSection.Products,
            branchCodes: new List<string> { "S1" },
            search: "旧国内供应商"
        );
        Assert.Equal("P-UPDATE", Assert.Single(first.Rows).Code);

        await _posmDb.Updateable<PosmProductSupplierMapping>()
            .SetColumns(row => row.ChinaSupplierCode == "CN-NEW")
            .Where(row => row.ProductCode == "P-UPDATE")
            .ExecuteCommandAsync();

        // 同一个服务也要为下一次完整读取建立新上下文；不同 search 令结果 cache key 不同。
        var second = await service.GetSalesDetailColumnsAsync(
            range,
            SalesDetailKind.China,
            SalesDetailSection.Products,
            branchCodes: new List<string> { "S1" },
            search: "新国内供应商"
        );
        Assert.Equal("P-UPDATE", Assert.Single(second.Rows).Code);
    }

    private async Task SeedCatalogAsync(string code, string name)
    {
        await _localDb.Insertable(new ChinaSupplier
        {
            Guid = $"guid-{code}",
            SupplierCode = code,
            SupplierName = name,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductAsync(string code)
    {
        await _localDb.Insertable(new Product
        {
            UUID = code,
            ProductCode = code,
            ProductName = code,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStatisticAsync(DateTime date, string productCode, int quantity)
    {
        await _localDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date,
            BranchCode = "S1",
            SupplierCode = "200",
            ProductCode = productCode,
            ProductName = productCode,
            TotalQuantity = quantity,
            TotalAmount = quantity,
            OrderCount = 1,
            CostSource = "Test",
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        if (_seededRefreshDates.Add(date.Date))
        {
            await _localDb.Insertable(new SalesStatisticRefreshState
            {
                StatisticType = SalesStatisticType.ProductStoreDaily,
                Date = date.Date,
                Status = SalesStatisticRefreshStatus.Fresh,
                LastAggregatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
            }).ExecuteCommandAsync();
        }
    }

    private async Task SeedMappingAsync(string productCode, string chinaSupplierCode)
    {
        await _posmDb.Insertable(new PosmProductSupplierMapping
        {
            ProductCode = productCode,
            LocalSupplierCode = "200",
            ChinaSupplierCode = chinaSupplierCode,
            LastUpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private SalesDashboardReactService CreateService()
    {
        var local = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(local, _localDb);
        var posm = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(posm, _posmDb);
        return new SalesDashboardReactService(
            local,
            posm,
            null!,
            NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            null,
            new ConfigurationBuilder().Build()
        );
    }

    private static ConnectionConfig Config(string connectionString) => new()
    {
        ConnectionString = connectionString,
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = false,
        InitKeyType = InitKeyType.Attribute,
    };

    public void Dispose()
    {
        _localConnection.Dispose();
        _posmConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localPath);
        SqliteTempFileCleanup.DeleteIfExists(_posmPath);
    }
}
