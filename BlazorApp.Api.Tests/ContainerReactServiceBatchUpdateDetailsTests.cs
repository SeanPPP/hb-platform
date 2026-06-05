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
            typeof(Container),
            typeof(ContainerDetail),
            typeof(DomesticProduct),
            typeof(WarehouseProduct),
            typeof(Product),
            typeof(StoreRetailPrice)
        );
    }

    [Fact]
    public async Task ContainerReactServiceUpdateContainerAsync_状态变化_应更新货柜主表状态并保留头部字段更新()
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = "OOCU5568972",
                ContainerNumber = "OOCU5568972",
                ActualArrivalDate = new DateTime(2026, 6, 15),
                ExchangeRate = 4.5m,
                ShippingFee = 100m,
                Status = 0,
                Remarks = "旧备注",
            }
        ).ExecuteCommandAsync();
        var service = CreateService();

        var success = await service.UpdateContainerAsync(
            "OOCU5568972",
            new UpdateContainerDto
            {
                实际到货日期 = new DateTime(2026, 6, 16),
                汇率 = 4.6m,
                运费 = 1280m,
                备注 = "运输中",
                状态 = 1,
            }
        );

        var container = await _localDb.Queryable<Container>()
            .SingleAsync(x => x.ContainerCode == "OOCU5568972");
        Assert.True(success);
        Assert.Equal(1, container.Status);
        Assert.Equal(new DateTime(2026, 6, 16), container.ActualArrivalDate);
        Assert.Equal(4.6m, container.ExchangeRate);
        Assert.Equal(1280m, container.ShippingFee);
        Assert.Equal("运输中", container.Remarks);
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
    public async Task BatchUpdateDetailsAsync_国内价格和贴牌价格变化_应更新货柜明细()
    {
        await SeedDetailAndProductAsync("D-DOMESTIC-OEM", "P-DOMESTIC-OEM", englishName: "Old English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-DOMESTIC-OEM",
                    国内价格 = 11.60m,
                    贴牌价格 = 6.99m,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-DOMESTIC-OEM");

        Assert.Equal(1, totalUpdated);
        Assert.Equal(11.60m, detail.DomesticPrice);
        Assert.Equal(6.99m, detail.OEMPrice);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_装箱体积和统计字段变化_应更新货柜明细()
    {
        await SeedDetailAndProductAsync("D-PACKING-VOLUME", "P-PACKING-VOLUME", englishName: "Old English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-PACKING-VOLUME",
                    单件装箱数 = 48m,
                    单件体积 = 0.118m,
                    装柜数量 = 96m,
                    合计装柜体积 = 0.236m,
                    合计装柜金额 = 1336.32m,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-PACKING-VOLUME");

        Assert.Equal(1, totalUpdated);
        Assert.Equal(48m, detail.PackingQuantity);
        Assert.Equal(0.118m, detail.UnitVolume);
        Assert.Equal(96m, detail.LoadingQuantity);
        Assert.Equal(0.236m, detail.TotalVolume);
        Assert.Equal(1336.32m, detail.TotalAmount);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_统计字段变化_应同步刷新货柜主表汇总()
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = "C-SUMMARY",
                ContainerNumber = "C-SUMMARY",
                TotalPieces = 99m,
                TotalQuantity = 99m,
                TotalAmount = 99m,
                TotalVolume = 99m,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new List<ContainerDetail>
            {
                new()
                {
                    DetailCode = "D-SUMMARY-1",
                    ContainerCode = "C-SUMMARY",
                    ProductCode = "P-SUMMARY-1",
                    LoadingPieces = 2m,
                    LoadingQuantity = 20m,
                    TotalAmount = 100m,
                    TotalVolume = 0.5m,
                    IsDeleted = false,
                },
                new()
                {
                    DetailCode = "D-SUMMARY-2",
                    ContainerCode = "C-SUMMARY",
                    ProductCode = "P-SUMMARY-2",
                    LoadingPieces = 3m,
                    LoadingQuantity = 30m,
                    TotalAmount = 150m,
                    TotalVolume = 0.75m,
                    IsDeleted = false,
                },
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new DomesticProduct
            {
                ProductCode = "P-SUMMARY-1",
                HBProductNo = "P-SUMMARY-1",
                ProductName = "汇总商品",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await SeedRelatedPriceRowsAsync("P-SUMMARY-1");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SUMMARY-1",
                    装柜数量 = 48m,
                    合计装柜体积 = 0.66m,
                    合计装柜金额 = 464.64m,
                    进口价格 = 2.10m,
                    SkipRelatedProductSync = true,
                },
            }
        );

        var container = await _localDb.Queryable<Container>()
            .SingleAsync(x => x.ContainerCode == "C-SUMMARY");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SUMMARY-1");

        Assert.Equal(1, totalUpdated);
        Assert.Equal(5m, container.TotalPieces);
        Assert.Equal(78m, container.TotalQuantity);
        Assert.Equal(614.64m, container.TotalAmount);
        Assert.Equal(1.41m, container.TotalVolume);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
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
    public async Task BatchUpdateDetailsAsync_跳过关联同步_应只更新货柜明细()
    {
        await SeedDetailAndProductAsync("D-SKIP-SYNC", "P-SKIP-SYNC", englishName: "Old English");
        await SeedRelatedPriceRowsAsync("P-SKIP-SYNC");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SKIP-SYNC",
                    进口价格 = 8.88m,
                    贴牌价格 = 9.99m,
                    IsActive = false,
                    SkipRelatedProductSync = true,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-SKIP-SYNC");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SKIP-SYNC");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-SKIP-SYNC");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-SKIP-SYNC")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(8.88m, detail.ImportPrice);
        Assert.Equal(9.99m, detail.OEMPrice);
        Assert.False(detail.IsActive);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
        Assert.Equal(2.22m, warehouseProduct.OEMPrice);
        Assert.True(warehouseProduct.IsActive);
        Assert.Equal(1.11m, product.PurchasePrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(1.11m, row.PurchasePrice));
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
