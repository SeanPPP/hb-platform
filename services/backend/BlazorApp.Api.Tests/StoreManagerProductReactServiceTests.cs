using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreManagerProductReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public StoreManagerProductReactServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(ProductSetCode),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct),
            typeof(Store)
        );
    }

    [Fact]
    public async Task BatchUpdateMultiCodePricesAsync_同组重算失败时回滚整组()
    {
        await SeedSetAsync("P-INCOMPLETE", completeStoreProjection: false);
        var service = CreateService();

        var response = await service.BatchUpdateMultiCodePricesAsync(
            new List<StoreManagerUpdateMultiCodePriceDto>
            {
                new() { UUID = "P-INCOMPLETE-A", MultiCodeRetailPrice = 15m },
            },
            "测试用户"
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(0, response.Data!.SuccessCount);
        Assert.Equal(1, response.Data.FailedCount);
        var persisted = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(row => row.UUID == "P-INCOMPLETE-A");
        Assert.Equal(10m, persisted.MultiCodeRetailPrice);
        Assert.Equal(1m, persisted.PurchasePrice);
    }

    [Fact]
    public async Task BatchUpdateStorePricesAsync_同组重算失败时回滚主价格更新()
    {
        await SeedSetAsync("P-STORE-INCOMPLETE", completeStoreProjection: false);

        var response = await CreateService().BatchUpdateStorePricesAsync(
            new List<StoreManagerUpdatePriceDto>
            {
                new() { UUID = "P-STORE-INCOMPLETE-PRICE", PurchasePrice = 40m },
            },
            "测试用户"
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(0, response.Data!.SuccessCount);
        Assert.Equal(1, response.Data.FailedCount);
        Assert.Equal(
            30m,
            (await _db.Queryable<StoreRetailPrice>()
                .SingleAsync(row => row.UUID == "P-STORE-INCOMPLETE-PRICE"))
                .PurchasePrice
        );
        Assert.Equal(
            1m,
            (await _db.Queryable<StoreMultiCodeProduct>()
                .SingleAsync(row => row.UUID == "P-STORE-INCOMPLETE-A"))
                .PurchasePrice
        );
    }

    [Fact]
    public async Task BatchUpdateMultiCodePricesAsync_不同组保留部分成功语义()
    {
        await SeedSetAsync("P-COMPLETE", completeStoreProjection: true);
        await SeedSetAsync("P-INCOMPLETE", completeStoreProjection: false);
        var service = CreateService();

        var response = await service.BatchUpdateMultiCodePricesAsync(
            new List<StoreManagerUpdateMultiCodePriceDto>
            {
                new() { UUID = "P-COMPLETE-A", MultiCodeRetailPrice = 20m },
                new() { UUID = "P-INCOMPLETE-A", MultiCodeRetailPrice = 15m },
            },
            "测试用户"
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data!.SuccessCount);
        Assert.Equal(1, response.Data.FailedCount);
        var succeeded = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(row => row.UUID == "P-COMPLETE-A");
        var rolledBack = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(row => row.UUID == "P-INCOMPLETE-A");
        Assert.Equal(20m, succeeded.MultiCodeRetailPrice);
        Assert.Equal(15m, succeeded.PurchasePrice);
        Assert.Equal(10m, rolledBack.MultiCodeRetailPrice);
        Assert.Equal(1m, rolledBack.PurchasePrice);
    }

    [Fact]
    public async Task BatchUpdateStorePricesAsync_不同组保留部分成功语义()
    {
        await SeedSetAsync("P-STORE-COMPLETE", completeStoreProjection: true);
        await SeedSetAsync("P-STORE-INCOMPLETE-2", completeStoreProjection: false);

        var response = await CreateService().BatchUpdateStorePricesAsync(
            new List<StoreManagerUpdatePriceDto>
            {
                new() { UUID = "P-STORE-COMPLETE-PRICE", PurchasePrice = 45m },
                new() { UUID = "P-STORE-INCOMPLETE-2-PRICE", PurchasePrice = 40m },
            },
            "测试用户"
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data!.SuccessCount);
        Assert.Equal(1, response.Data.FailedCount);
        Assert.Equal(
            45m,
            (await _db.Queryable<StoreRetailPrice>()
                .SingleAsync(row => row.UUID == "P-STORE-COMPLETE-PRICE"))
                .PurchasePrice
        );
        Assert.Equal(
            15m,
            (await _db.Queryable<StoreMultiCodeProduct>()
                .SingleAsync(row => row.UUID == "P-STORE-COMPLETE-A"))
                .PurchasePrice
        );
        Assert.Equal(
            30m,
            (await _db.Queryable<StoreRetailPrice>()
                .SingleAsync(row => row.UUID == "P-STORE-INCOMPLETE-2-PRICE"))
                .PurchasePrice
        );
        Assert.Equal(
            1m,
            (await _db.Queryable<StoreMultiCodeProduct>()
                .SingleAsync(row => row.UUID == "P-STORE-INCOMPLETE-2-A"))
                .PurchasePrice
        );
    }

    [Fact]
    public async Task BatchUpdateMultiCodePricesAsync_重复UUID按请求顺序在同组提交()
    {
        await SeedSetAsync("P-DUPLICATE", completeStoreProjection: true);
        var service = CreateService();

        var response = await service.BatchUpdateMultiCodePricesAsync(
            new List<StoreManagerUpdateMultiCodePriceDto>
            {
                new() { UUID = "P-DUPLICATE-A", MultiCodeRetailPrice = 12m },
                new() { UUID = "P-DUPLICATE-A", MultiCodeRetailPrice = 14m },
            },
            "测试用户"
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(2, response.Data!.SuccessCount);
        Assert.Equal(0, response.Data.FailedCount);
        var persisted = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(row => row.UUID == "P-DUPLICATE-A");
        Assert.Equal(14m, persisted.MultiCodeRetailPrice);
    }

    [Fact]
    public async Task BatchUpdateMultiCodePricesAsync_SetType一和SetType二都忽略提交成本()
    {
        await SeedSetAsync("P-SET", completeStoreProjection: true);
        await SeedMultiCodeAsync("P-MULTI", setType: 2);
        var service = CreateService();

        var response = await service.BatchUpdateMultiCodePricesAsync(
            new List<StoreManagerUpdateMultiCodePriceDto>
            {
                new() { UUID = "P-SET-A", PurchasePrice = 999m },
                new() { UUID = "P-MULTI-A", PurchasePrice = 7m },
            },
            "测试用户"
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(2, response.Data!.SuccessCount);
        var setChild = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(row => row.UUID == "P-SET-A");
        var normalMultiCode = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(row => row.UUID == "P-MULTI-A");
        Assert.Equal(10m, setChild.PurchasePrice);
        Assert.Equal(30m, normalMultiCode.PurchasePrice);
    }

    [Fact]
    public void 多码价格更新_两类套装均按完整父子键识别()
    {
        var sourcePath = Path.Combine(
            Environment.GetEnvironmentVariable("HB_PLATFORM_ROOT")
                ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."),
            "BlazorApp.Api",
            "Services",
            "React",
            "StoreManagerProductReactService.cs"
        );
        var source = File.ReadAllText(sourcePath);

        Assert.Equal(2, source.Split("x.SetType == 1 || x.SetType == 2").Length - 1);
        Assert.Contains("x.SetProductCode == exists.MultiCodeProductCode", source);
    }

    [Fact]
    public void 批量更新_锁内复读并确认UUID仍属于预读业务组()
    {
        var sourcePath = Path.Combine(
            Environment.GetEnvironmentVariable("HB_PLATFORM_ROOT")
                ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."),
            "BlazorApp.Api",
            "Services",
            "React",
            "StoreManagerProductReactService.cs"
        );
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("在等待业务锁期间已离开目标分组", source);
        Assert.Contains("GroupBy(x => (x.StoreCode, x.ProductCode))", source);
        Assert.Contains("RecalculateStoresLockedAsync", source);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private StoreManagerProductReactService CreateService() =>
        new(
            CreateSqlSugarContext(_db),
            NullLogger<StoreManagerProductReactService>.Instance
        );

    private async Task SeedSetAsync(string productCode, bool completeStoreProjection)
    {
        await _db.Insertable(new Product
        {
            UUID = $"{productCode}-UUID",
            ProductCode = productCode,
            ProductName = productCode,
            PurchasePrice = 30m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode(productCode, "A", 10m, setType: 1),
            BuildSetCode(productCode, "B", 20m, setType: 1),
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = $"{productCode}-PRICE",
            StoreCode = "S01",
            ProductCode = productCode,
            PurchasePrice = 30m,
            StoreRetailPriceValue = 50m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildStoreRow(productCode, "A", 10m, 1m)).ExecuteCommandAsync();
        if (completeStoreProjection)
        {
            await _db.Insertable(BuildStoreRow(productCode, "B", 20m, 1m)).ExecuteCommandAsync();
        }
    }

    private async Task SeedMultiCodeAsync(string productCode, int setType)
    {
        await _db.Insertable(new Product
        {
            UUID = $"{productCode}-UUID",
            ProductCode = productCode,
            ProductName = productCode,
            PurchasePrice = 30m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode(productCode, "A", 10m, setType)).ExecuteCommandAsync();
        await _db.Insertable(BuildStoreRow(productCode, "A", 10m, 1m)).ExecuteCommandAsync();
    }

    private static ProductSetCode BuildSetCode(
        string productCode,
        string childCode,
        decimal retailPrice,
        int setType
    ) => new()
    {
        SetCodeId = $"{productCode}-{childCode}-SET",
        ProductCode = productCode,
        SetProductCode = $"{productCode}-{childCode}-CHILD",
        SetItemNumber = $"ITEM-{productCode}-{childCode}",
        SetBarcode = $"BAR-{productCode}-{childCode}",
        SetPurchasePrice = 1m,
        SetRetailPrice = retailPrice,
        SetQuantity = 1,
        SetType = setType,
        IsActive = true,
        IsDeleted = false,
        CreatedAt = DateTime.UtcNow,
    };

    private static StoreMultiCodeProduct BuildStoreRow(
        string productCode,
        string childCode,
        decimal retailPrice,
        decimal purchasePrice
    ) => new()
    {
        UUID = $"{productCode}-{childCode}",
        StoreCode = "S01",
        ProductCode = productCode,
        MultiCodeProductCode = $"{productCode}-{childCode}-CHILD",
        StoreMultiCodeProductCode = $"S01-{productCode}-{childCode}",
        MultiBarcode = $"BAR-{productCode}-{childCode}",
        PurchasePrice = purchasePrice,
        MultiCodeRetailPrice = retailPrice,
        IsActive = true,
        IsDeleted = false,
        CreatedAt = DateTime.UtcNow,
    };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }
}
