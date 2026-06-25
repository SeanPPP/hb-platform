using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductSetCodeReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public ProductSetCodeReactServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(ProductSetCode),
            typeof(HBLocalSupplier)
        );
    }

    [Fact]
    public async Task GetGridDataAsync_按ProductCode只返回当前商品多码并同步Total()
    {
        await SeedProductSetCodesAsync();
        var service = CreateService();

        var result = await service.GetGridDataAsync(new ProductSetCodeGridRequestDto
        {
            ProductCode = "P-A",
            StartRow = 0,
            PageSize = 20,
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Total);
        Assert.Equal(new[] { "set-a-2", "set-a-1" }, result.Items!.Select(item => item.SetCodeId));
        Assert.All(result.Items!, item => Assert.Equal("P-A", item.ProductCode));
    }

    [Fact]
    public async Task GetGridDataAsync_兼容FilterModelProductCode筛选()
    {
        await SeedProductSetCodesAsync();
        var service = CreateService();

        var result = await service.GetGridDataAsync(new ProductSetCodeGridRequestDto
        {
            StartRow = 0,
            PageSize = 20,
            FilterModel = new Dictionary<string, FilterModelDto>
            {
                ["productCode"] = new()
                {
                    FilterType = "text",
                    Type = "equals",
                    Filter = "P-B",
                },
            },
        });

        Assert.True(result.Success);
        var item = Assert.Single(result.Items!);
        Assert.Equal(1, result.Total);
        Assert.Equal("set-b-1", item.SetCodeId);
        Assert.Equal("P-B", item.ProductCode);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private ProductSetCodeReactService CreateService()
    {
        return new ProductSetCodeReactService(
            CreateSqlSugarContext(_db),
            Mock.Of<IStoreRetailPriceReactService>(),
            NullLogger<ProductSetCodeReactService>.Instance
        );
    }

    private async Task SeedProductSetCodesAsync()
    {
        await _db.Insertable(new[]
        {
            new HBLocalSupplier { Guid = "supplier-200", LocalSupplierCode = "200", Name = "Hot Bargain" },
            new HBLocalSupplier { Guid = "supplier-225", LocalSupplierCode = "225", Name = "MNB" },
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            BuildProduct("P-A", "ITEM-A", "BAR-A", "200"),
            BuildProduct("P-B", "ITEM-B", "BAR-B", "225"),
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            BuildSetCode("set-a-1", "P-A", "A-1", DateTime.UtcNow.AddMinutes(-2)),
            BuildSetCode("set-a-2", "P-A", "A-2", DateTime.UtcNow.AddMinutes(-1)),
            BuildSetCode("set-b-1", "P-B", "B-1", DateTime.UtcNow),
        }).ExecuteCommandAsync();
    }

    private static Product BuildProduct(
        string productCode,
        string itemNumber,
        string barcode,
        string supplierCode
    ) => new()
    {
        ProductCode = productCode,
        ProductName = productCode,
        ItemNumber = itemNumber,
        Barcode = barcode,
        LocalSupplierCode = supplierCode,
        IsDeleted = false,
    };

    private static ProductSetCode BuildSetCode(
        string setCodeId,
        string productCode,
        string setBarcode,
        DateTime updatedAt
    ) => new()
    {
        SetCodeId = setCodeId,
        ProductCode = productCode,
        SetProductCode = $"{setCodeId}-product",
        SetItemNumber = $"{setCodeId}-item",
        SetBarcode = setBarcode,
        SetPurchasePrice = 1.23m,
        SetRetailPrice = 2.99m,
        IsActive = true,
        IsDeleted = false,
        UpdatedAt = updatedAt,
        UpdatedBy = "test",
    };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
