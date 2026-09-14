using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.ProductInsights;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductBranchSalesServiceTests : IDisposable
{
    private readonly string _mainPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly string _posmPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _mainConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _mainDb;
    private readonly SqlSugarClient _posmDb;

    public ProductBranchSalesServiceTests()
    {
        _mainConnection = new SqliteConnection($"Data Source={_mainPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmPath}");
        _mainConnection.Open();
        _posmConnection.Open();
        _mainDb = new SqlSugarClient(Config(_mainConnection.ConnectionString));
        _posmDb = new SqlSugarClient(Config(_posmConnection.ConnectionString));
        _mainDb.CodeFirst.InitTables(typeof(Store), typeof(ProductStoreDailySalesStatistic));
        _posmDb.CodeFirst.InitTables(typeof(POSM_设备注册信息表));
    }

    [Fact]
    public async Task 启用POS全集补齐零销量并排除非POS和停用POS门店()
    {
        await SeedStoreAsync("S1", "Alpha");
        await SeedStoreAsync("S2", "Beta");
        await SeedStoreAsync("S3", "No POS");
        await SeedStoreAsync("S4", "Disabled POS");
        await SeedPosAsync("S1", enabled: true);
        await SeedPosAsync("S2", enabled: true);
        await SeedPosAsync("S4", enabled: false);
        await SeedSaleAsync("S1", new DateTime(2026, 9, 1), 5, 12.5m, new DateTime(2026, 9, 4, 2, 3, 4, DateTimeKind.Utc));
        await SeedSaleAsync("S3", new DateTime(2026, 9, 1), 99, 999m, new DateTime(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc));

        var result = await CreateService().GetAsync(" P1 ", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), null);

        Assert.Equal("all-pos", result.Scope);
        Assert.Equal(2, result.TotalPosStoreCount);
        Assert.Equal(2, result.IncludedStoreCount);
        Assert.Equal(5, result.Quantity);
        Assert.Equal(12.5m, result.Amount);
        Assert.Collection(result.Rows,
            row => { Assert.Equal("S1", row.StoreCode); Assert.Equal(5, row.Quantity); Assert.Equal(12.5m, row.Amount); },
            row => { Assert.Equal("S2", row.StoreCode); Assert.Equal(0, row.Quantity); Assert.Equal(0m, row.Amount); });
        Assert.Equal(new DateTime(2026, 9, 4, 2, 3, 4, DateTimeKind.Utc), result.SalesStatisticLastUpdatedAt);
    }

    [Fact]
    public async Task 授权范围只聚合允许的POS门店且保留完整POS总数()
    {
        await SeedStoreAsync("S1", "Alpha");
        await SeedStoreAsync("S2", "Beta");
        await SeedPosAsync("S1", enabled: true);
        await SeedPosAsync("S2", enabled: true);
        await SeedSaleAsync("S1", new DateTime(2026, 9, 1), 5, 10m, DateTime.UtcNow);
        await SeedSaleAsync("S2", new DateTime(2026, 9, 1), 8, 16m, DateTime.UtcNow);

        var result = await CreateService().GetAsync("P1", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), new[] { "s2", "S999" });

        Assert.Equal("authorized-pos", result.Scope);
        Assert.Equal(2, result.TotalPosStoreCount);
        Assert.Equal(1, result.IncludedStoreCount);
        Assert.Equal(8, result.Quantity);
        Assert.Equal(16m, result.Amount);
        Assert.Equal("S2", Assert.Single(result.Rows).StoreCode);
    }

    [Fact]
    public async Task 日期首尾都按业务日包含且非法范围抛出错误()
    {
        await SeedStoreAsync("S1", "Alpha");
        await SeedPosAsync("S1", enabled: true);
        await SeedSaleAsync("S1", new DateTime(2026, 9, 1), 2, 4m, DateTime.UtcNow);
        await SeedSaleAsync("S1", new DateTime(2026, 9, 2), 3, 6m, DateTime.UtcNow);
        await SeedSaleAsync("S1", new DateTime(2026, 9, 3), 7, 14m, DateTime.UtcNow);

        var result = await CreateService().GetAsync("P1", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), null);

        Assert.Equal(5, result.Quantity);
        Assert.Equal(10m, result.Amount);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().GetAsync("P1", new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 2), null));
    }

    private ProductBranchSalesService CreateService() => new(Context(_mainDb), PosmContext(_posmDb), TimeProvider.System);

    private Task SeedStoreAsync(string code, string name) => _mainDb.Insertable(new Store
    {
        StoreGUID = Guid.NewGuid().ToString(), StoreCode = code, StoreName = name, IsActive = true, IsDeleted = false,
    }).ExecuteCommandAsync();

    private Task SeedPosAsync(string storeCode, bool enabled) => _posmDb.Insertable(new POSM_设备注册信息表
    {
        设备硬件识别码 = Guid.NewGuid().ToString(), 系统设备编号 = Guid.NewGuid().ToString("N"), 分店代码 = storeCode,
        设备类型 = "POS", 设备系统 = "Windows", 设备状态 = enabled ? 1 : 0, 设备授权码 = "test",
    }).ExecuteCommandAsync();

    private Task SeedSaleAsync(string storeCode, DateTime date, int quantity, decimal amount, DateTime updatedAt) =>
        _mainDb.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date, BranchCode = storeCode, SupplierCode = "S", ProductCode = "P1", TotalQuantity = quantity,
            TotalAmount = amount, OrderCount = 1, UpdateTime = updatedAt,
        }).ExecuteCommandAsync();

    private static ConnectionConfig Config(string connectionString) => new()
    {
        ConnectionString = connectionString, DbType = DbType.Sqlite, IsAutoCloseConnection = false,
        InitKeyType = InitKeyType.Attribute,
    };

    private static SqlSugarContext Context(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext PosmContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _mainConnection.Dispose();
        _posmConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_mainPath);
        SqliteTempFileCleanup.DeleteIfExists(_posmPath);
    }
}
