using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductSyncServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public ProductSyncServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables(typeof(WarehouseProduct), typeof(Product), typeof(StoreRetailPrice));
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_只改零售价时应保留原上下架状态()
    {
        await SeedWarehouseProductAsync("P-PRICE-ONLY", oemPrice: 2m, isActive: false);
        var service = CreateService();

        var priceOnlyResult = await service.BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-PRICE-ONLY", OEMPrice = 3.5m },
                },
            }
        );
        var afterPriceOnly = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-PRICE-ONLY");

        Assert.True(priceOnlyResult.Success, priceOnlyResult.Message);
        Assert.Equal(3.5m, afterPriceOnly.OEMPrice);
        Assert.False(afterPriceOnly.IsActive);

        var statusResult = await service.BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-PRICE-ONLY", IsActive = true },
                },
            }
        );
        var afterStatus = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-PRICE-ONLY");

        Assert.True(statusResult.Success, statusResult.Message);
        Assert.True(afterStatus.IsActive);
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_商品编码未命中时应按货号匹配()
    {
        await SeedProductAsync("P-BY-ITEM", "ITEM-BY-ITEM");
        await SeedWarehouseProductAsync("P-BY-ITEM", oemPrice: 2m, isActive: false);
        var service = CreateService();

        var result = await service.BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-MISSING", ItemNumber = "ITEM-BY-ITEM", OEMPrice = 4m },
                },
            }
        );
        var warehouseProduct = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-BY-ITEM");

        Assert.True(result.Success, result.Message);
        Assert.Equal(4m, warehouseProduct.OEMPrice);
        Assert.False(warehouseProduct.IsActive);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task SeedWarehouseProductAsync(string productCode, decimal oemPrice, bool isActive)
    {
        await _db.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                OEMPrice = oemPrice,
                IsActive = isActive,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedProductAsync(string productCode, string itemNumber)
    {
        await _db.Insertable(
            new Product
            {
                UUID = $"UUID-{productCode}",
                ProductCode = productCode,
                ItemNumber = itemNumber,
            }
        ).ExecuteCommandAsync();
    }

    private ProductSyncService CreateService() =>
        new(CreateSqlSugarContext(_db), NullLogger<ProductSyncService>.Instance);

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
