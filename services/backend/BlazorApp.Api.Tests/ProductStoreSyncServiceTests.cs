using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductStoreSyncServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public ProductStoreSyncServiceTests()
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
        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(ProductSetCode),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct)
        );
    }

    [Fact]
    public async Task SyncProductsToStoresAsync_Type1有效子项不依赖ProductType也应同步到门店()
    {
        await _db.Insertable(new Product
        {
            UUID = "UUID-TYPE1",
            ProductCode = "P-TYPE1",
            ItemNumber = "ITEM-TYPE1",
            ProductType = 0,
            PurchasePrice = 10m,
            RetailPrice = 20m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = "SET-TYPE1",
            ProductCode = "P-TYPE1",
            SetProductCode = "CHILD-TYPE1",
            SetItemNumber = "ITEM-CHILD-TYPE1",
            SetRetailPrice = 20m,
            SetPurchasePrice = 10m,
            SetType = 1,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var response = await CreateService().SyncProductsToStoresAsync(
            new SyncProductsToStoresRequest
            {
                ProductCodes = new List<string> { "P-TYPE1" },
                StoreCodes = new List<string> { "S01" },
                SyncPurchasePrice = true,
                SyncRetailPrice = true,
                SyncIsAutoPricing = false,
                SyncIsSpecialProduct = false,
                SyncDiscountRate = false,
            }
        );

        var storeMultiCode = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(item =>
                item.StoreCode == "S01"
                && item.ProductCode == "P-TYPE1"
                && item.MultiCodeProductCode == "CHILD-TYPE1"
            );

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data!.StoreMultiCodeProductCreatedCount);
        Assert.Equal(10m, storeMultiCode.PurchasePrice);
        Assert.Equal(20m, storeMultiCode.MultiCodeRetailPrice);
    }

    [Fact]
    public async Task SyncProductsToStoresAsync_Type2有效子项不依赖ProductType且最终等于门店主成本()
    {
        await _db.Insertable(new Product
        {
            UUID = "UUID-TYPE2",
            ProductCode = "P-TYPE2",
            ItemNumber = "ITEM-TYPE2",
            ProductType = 0,
            PurchasePrice = 10m,
            RetailPrice = 20m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = "SET-TYPE2",
            ProductCode = "P-TYPE2",
            SetProductCode = "CHILD-TYPE2",
            SetItemNumber = "ITEM-CHILD-TYPE2",
            SetRetailPrice = 20m,
            SetPurchasePrice = 999m,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var response = await CreateService().SyncProductsToStoresAsync(
            new SyncProductsToStoresRequest
            {
                ProductCodes = new List<string> { "P-TYPE2" },
                StoreCodes = new List<string> { "S01" },
                SyncPurchasePrice = true,
                SyncRetailPrice = true,
            }
        );

        var storeMultiCode = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(item =>
                item.StoreCode == "S01"
                && item.ProductCode == "P-TYPE2"
                && item.MultiCodeProductCode == "CHILD-TYPE2"
            );

        Assert.True(response.Success, response.Message);
        // Type2 子项不复制总部历史成本，必须由统一服务回写为当前门店主成本。
        Assert.Equal(10m, storeMultiCode.PurchasePrice);
        Assert.Equal(20m, storeMultiCode.MultiCodeRetailPrice);
    }

    [Fact]
    public async Task SyncProductsToStoresAsync_相同子项编码不得复用其他父商品的门店投影()
    {
        await _db.Insertable(new[]
        {
            new Product
            {
                UUID = "UUID-PARENT-A",
                ProductCode = "PARENT-A",
                ItemNumber = "ITEM-PARENT-A",
                ProductType = 1,
                PurchasePrice = 12m,
                RetailPrice = 24m,
                IsDeleted = false,
            },
            new Product
            {
                UUID = "UUID-PARENT-B",
                ProductCode = "PARENT-B",
                ItemNumber = "ITEM-PARENT-B",
                ProductType = 1,
                PurchasePrice = 8m,
                RetailPrice = 16m,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = "SET-PARENT-A",
            ProductCode = "PARENT-A",
            SetProductCode = "SHARED-CHILD",
            SetRetailPrice = 24m,
            SetPurchasePrice = 12m,
            SetType = 1,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = "STORE-PARENT-B",
            StoreCode = "S01",
            ProductCode = "PARENT-B",
            MultiCodeProductCode = "SHARED-CHILD",
            MultiCodeRetailPrice = 16m,
            PurchasePrice = 8m,
            MultiBarcode = "B-UNCHANGED",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var response = await CreateService().SyncProductsToStoresAsync(
            new SyncProductsToStoresRequest
            {
                ProductCodes = new List<string> { "PARENT-A" },
                StoreCodes = new List<string> { "S01" },
                SyncPurchasePrice = true,
                SyncRetailPrice = true,
            }
        );

        var rows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(row => row.StoreCode == "S01" && row.MultiCodeProductCode == "SHARED-CHILD")
            .OrderBy(row => row.ProductCode)
            .ToListAsync();

        Assert.True(response.Success, response.Message);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.ProductCode == "PARENT-A" && row.PurchasePrice == 12m);
        Assert.Contains(rows, row =>
            row.ProductCode == "PARENT-B"
            && row.PurchasePrice == 8m
            && row.MultiBarcode == "B-UNCHANGED"
        );
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

    private ProductStoreSyncService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = _connection.ConnectionString }
            )
            .Build();
        return new ProductStoreSyncService(
            CreateSqlSugarContext(_db),
            configuration,
            NullLogger<ProductStoreSyncService>.Instance
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
