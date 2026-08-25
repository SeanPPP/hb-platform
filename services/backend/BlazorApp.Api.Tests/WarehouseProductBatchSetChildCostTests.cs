using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseProductBatchSetChildCostTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public WarehouseProductBatchSetChildCostTests()
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
            typeof(ProductLocation),
            typeof(Location)
        );
    }

    [Fact]
    public async Task BatchUpdateAsync_仓库回退成本变化同步Type2()
    {
        await SeedType2Async("P-BATCH");
        var result = await CreateService().BatchUpdateAsync(new BatchUpdateRequest
        {
            Products = new List<WarehouseProductBatchDto>
            {
                new() { ProductCode = "P-BATCH", ImportPrice = 12m },
            },
        });

        Assert.True(result.Success, result.ErrorMessage);
        await AssertType2CostAsync(12m);
    }

    [Fact]
    public async Task IncrementalSaveAsync_仓库回退成本变化同步Type2()
    {
        await SeedType2Async("P-INCREMENTAL");
        var result = await CreateService().IncrementalSaveAsync(new IncrementalSaveRequest
        {
            Products = new List<WarehouseProductBatchDto>
            {
                new() { ProductCode = "P-INCREMENTAL", ImportPrice = 13m },
            },
        });

        Assert.True(result.Success, result.ErrorMessage);
        await AssertType2CostAsync(13m);
    }

    [Fact]
    public async Task BulkSetPriceAsync_进口价变化同步Type2()
    {
        await SeedType2Async("P-BULK");
        var result = await CreateService().BulkSetPriceAsync(new BulkSetPriceRequest
        {
            ProductCodes = new List<string> { "P-BULK" },
            PriceType = "IMPORT",
            Price = 14m,
        });

        Assert.True(result.Success, result.ErrorMessage);
        await AssertType2CostAsync(14m);
    }

    private async Task SeedType2Async(string productCode)
    {
        await _db.Insertable(new Product
        {
            UUID = $"{productCode}-UUID",
            ProductCode = productCode,
            PurchasePrice = 0m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            ImportPrice = 10m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = $"SRP-{productCode}",
            StoreCode = "S01",
            ProductCode = productCode,
            PurchasePrice = 0m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = $"SET-{productCode}",
            ProductCode = productCode,
            SetProductCode = $"CHILD-{productCode}",
            SetItemNumber = $"ITEM-{productCode}",
            SetPurchasePrice = 99m,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = $"SMC-{productCode}",
            StoreCode = "S01",
            ProductCode = productCode,
            MultiCodeProductCode = $"CHILD-{productCode}",
            PurchasePrice = 99m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task AssertType2CostAsync(decimal expected)
    {
        Assert.Equal(expected, (await _db.Queryable<ProductSetCode>().SingleAsync()).SetPurchasePrice);
        Assert.Equal(expected, (await _db.Queryable<StoreMultiCodeProduct>().SingleAsync()).PurchasePrice);
    }

    private WarehouseProductBatchService CreateService() => new(
        CreateContext(_db),
        NullLogger<WarehouseProductBatchService>.Instance,
        WarehouseProductChangeHistoryTestDouble.CreateNoop(),
        Mock.Of<ICurrentUserService>()
    );

    private static SqlSugarContext CreateContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
