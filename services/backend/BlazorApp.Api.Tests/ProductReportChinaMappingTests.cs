using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductReportChinaMappingTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"report-china-map-{Guid.NewGuid():N}.db");
    private readonly SqlSugarClient _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public ProductReportChinaMappingTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={_path}", DbType = DbType.Sqlite,
            IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables<PosmProductSupplierMapping>();
    }

    [Fact]
    public async Task 只读取期间商品映射但保留整份有效映射的供应商代码()
    {
        _db.Insertable(new[]
        {
            Row("current", "C1"), Row("compare", "C2"), Row("outside-period", "C3"),
            Row("deleted", "DELETED", deleted: true), Row("local", "LOCAL", local: "250"),
            Row("no-china", ""), Row(" ", "BLANK-PRODUCT"),
        }).ExecuteCommand();
        var service = CreateService();
        var oldMap = await service.GetChinaSupplierProductMapAsync();
        var selected = new HashSet<string>(new[] { "current", "compare" }, StringComparer.OrdinalIgnoreCase);

        var result = await service.ReadReportChinaSupplierMappingsAsync(selected, includeAllSupplierCodes: true);

        Assert.Equal(oldMap.Where(row => selected.Contains(row.Key)).OrderBy(row => row.Key), result.ProductMap.OrderBy(row => row.Key));
        Assert.Equal(oldMap.Values.Distinct().OrderBy(value => value), result.SupplierCodes.OrderBy(value => value));
        Assert.Contains("C3", result.SupplierCodes);
        Assert.Equal("C1", result.ProductMap["CURRENT"]);
    }

    [Fact]
    public async Task 没有遗留商品时仍保留只由全局映射识别的直写供应商()
    {
        _db.Insertable(Row("outside-period", "C3")).ExecuteCommand();
        var result = await CreateService().ReadReportChinaSupplierMappingsAsync(new(), includeAllSupplierCodes: true);
        Assert.Empty(result.ProductMap);
        Assert.Equal(new[] { "C3" }, result.SupplierCodes);
    }

    [Fact]
    public async Task 跨多个查询批次保留所有请求商品且不读额外商品()
    {
        var rows = Enumerable.Range(0, 1002).Select(index => Row($"P{index:0000}", $"C{index % 3}")).ToArray();
        _db.Insertable(rows).ExecuteCommand();
        var requested = rows.Take(1001).Select(row => row.ProductCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = await CreateService().ReadReportChinaSupplierMappingsAsync(requested, includeAllSupplierCodes: false);

        Assert.Equal(1001, result.ProductMap.Count);
        Assert.All(rows.Take(1001), row => Assert.Equal(row.ChinaSupplierCode, result.ProductMap[row.ProductCode]));
        Assert.DoesNotContain("P1001", result.ProductMap.Keys);
        Assert.Empty(result.SupplierCodes);
    }

    [Fact]
    public async Task 指定供应商且期间没有遗留商品时无需读取映射表()
    {
        _db.Aop.OnLogExecuting = (_, _) => throw new InvalidOperationException("空商品集合不应发起映射查询");
        var result = await CreateService().ReadReportChinaSupplierMappingsAsync(new(), includeAllSupplierCodes: false);
        Assert.Empty(result.ProductMap);
        Assert.Empty(result.SupplierCodes);
    }

    private static PosmProductSupplierMapping Row(string product, string china, bool deleted = false, string local = "200") => new()
    {
        ProductCode = product, ChinaSupplierCode = china, LocalSupplierCode = local, IsDeleted = deleted,
    };

    private SalesDashboardReactService CreateService()
    {
        var local = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(local, _db);
        var posm = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(posm, _db);
        return new SalesDashboardReactService(local, posm, null!, NullLogger<SalesDashboardReactService>.Instance,
            _cache, null, new ConfigurationBuilder().Build());
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_path);
    }
}
