using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerReactServiceBatchUpdateDetailsTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hbSalesDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hbSalesDb;

    public ContainerReactServiceBatchUpdateDetailsTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hbSalesConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _localConnection.Open();
        _hbSalesConnection.Open();
        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hbSalesDb = new SqlSugarScope(CreateConnectionConfig(_hbSalesConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(
            typeof(ContainerDetail),
            typeof(DomesticProduct),
            typeof(WarehouseProduct),
            typeof(Product),
            typeof(StoreRetailPrice)
        );
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_仅英文名称变化_应回写DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-EN-ONLY", "P-EN-ONLY", englishName: null);
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-EN-ONLY", 英文名称 = "Large Strawberry" },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-EN-ONLY");
        Assert.Equal(1, totalUpdated);
        Assert.Equal("Large Strawberry", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_空白英文名称_不覆盖DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-BLANK-EN", "P-BLANK-EN", englishName: "Existing English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-BLANK-EN", 英文名称 = "   " },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-BLANK-EN");
        Assert.Equal(0, totalUpdated);
        Assert.Equal("Existing English", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_同一商品多条明细_应聚合回写名称并统计请求行()
    {
        await SeedDetailAndProductAsync("D-SAME-1", "P-SAME", englishName: "Old English");
        await SeedDetailAsync("D-SAME-2", "P-SAME");
        await SeedDetailAsync("D-SAME-3", "P-SAME");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-SAME-1", 商品名称 = "聚合中文名" },
                new() { HGUID = "D-SAME-2", 英文名称 = "First English" },
                new() { HGUID = "D-SAME-3", 英文名称 = "Last English" },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-SAME");
        Assert.Equal(3, totalUpdated);
        Assert.Equal("聚合中文名", product.ProductName);
        Assert.Equal("Last English", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_价格和英文名称同时变化_应同时更新明细和DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-PRICE-EN", "P-PRICE-EN", englishName: "Old English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-PRICE-EN", 进口价格 = 3.45m, 英文名称 = "Translated Name" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-PRICE-EN");
        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-PRICE-EN");
        Assert.Equal(1, totalUpdated);
        Assert.Equal(3.45m, detail.ImportPrice);
        Assert.Equal("Translated Name", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_价格贴牌和上下架变化_应保持关联表同步()
    {
        await SeedDetailAndProductAsync("D-SYNC-PRICE", "P-SYNC-PRICE", englishName: "Old English");
        await SeedRelatedPriceRowsAsync("P-SYNC-PRICE");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SYNC-PRICE",
                    进口价格 = 8.88m,
                    贴牌价格 = 9.99m,
                    IsActive = false,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-SYNC-PRICE");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SYNC-PRICE");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-SYNC-PRICE");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-SYNC-PRICE")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(8.88m, detail.ImportPrice);
        Assert.Equal(9.99m, detail.OEMPrice);
        Assert.False(detail.IsActive);
        Assert.Equal(8.88m, warehouseProduct.ImportPrice);
        Assert.Equal(9.99m, warehouseProduct.OEMPrice);
        Assert.False(warehouseProduct.IsActive);
        Assert.Equal(8.88m, product.PurchasePrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(8.88m, row.PurchasePrice));
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_明细或商品不存在_不抛异常()
    {
        await SeedDetailAsync("D-NO-PRODUCT", productCode: "P-MISSING");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-MISSING", 英文名称 = "Missing Detail" },
                new() { HGUID = "D-NO-PRODUCT", 英文名称 = "Missing Product" },
            }
        );

        Assert.Equal(0, totalUpdated);
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hbSalesDb.Dispose();
        _localConnection.Dispose();
        _hbSalesConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hbSalesDbPath);
    }

    private ContainerReactService CreateService()
    {
        return new ContainerReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(),
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<ContainerReactService>.Instance,
            Mock.Of<IContainerHqSyncService>()
        );
    }

    private async Task SeedDetailAndProductAsync(
        string detailCode,
        string productCode,
        string? englishName
    )
    {
        await SeedDetailAsync(detailCode, productCode);
        await _localDb.Insertable(
            new DomesticProduct
            {
                ProductCode = productCode,
                HBProductNo = productCode,
                ProductName = $"商品 {productCode}",
                EnglishProductName = englishName,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedRelatedPriceRowsAsync(string productCode)
    {
        await _localDb.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                ImportPrice = 1.11m,
                OEMPrice = 2.22m,
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new Product
            {
                ProductCode = productCode,
                ProductName = $"本地商品 {productCode}",
                PurchasePrice = 1.11m,
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new List<StoreRetailPrice>
            {
                new()
                {
                    StoreCode = "001",
                    ProductCode = productCode,
                    PurchasePrice = 1.11m,
                    IsActive = true,
                },
                new()
                {
                    StoreCode = "002",
                    ProductCode = productCode,
                    PurchasePrice = 1.11m,
                    IsActive = true,
                },
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedDetailAsync(string detailCode, string? productCode)
    {
        await _localDb.Insertable(
            new ContainerDetail
            {
                DetailCode = detailCode,
                ContainerCode = "C-TEST",
                ProductCode = productCode,
                ImportPrice = 1.23m,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext()
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);
        return context;
    }

    private static HBSalesSqlSugarContext CreateHBSalesSqlSugarContext(SqlSugarScope db)
    {
        var context = (HBSalesSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HBSalesSqlSugarContext));
        var dbField = typeof(HBSalesSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }
}
