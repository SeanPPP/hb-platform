using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerProductCreationServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;
    private readonly string _hbSalesDbPath;
    private readonly SqliteConnection _hbSalesSqliteConnection;
    private readonly SqlSugarScope _hbSalesDb;

    public ContainerProductCreationServiceTests()
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
            typeof(Container),
            typeof(ContainerDetail),
            typeof(DomesticProduct),
            typeof(DomesticSetProduct),
            typeof(Product),
            typeof(ProductSetCode),
            typeof(WarehouseProduct),
            typeof(Store),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct),
            typeof(ChinaSupplier),
            typeof(ProductLocation),
            typeof(Location),
            typeof(ProductGrade),
            typeof(WarehouseCategory)
        );

        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesSqliteConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _hbSalesSqliteConnection.Open();
        _hbSalesDb = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = _hbSalesSqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _hbSalesDb.CodeFirst.InitTables(typeof(CPT_DIC_商品套装信息表));
    }

    [Fact]
    public async Task ExecuteAsync_CreatesNormalProductAndStoreRetailPrices()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D001", "C001", "P001", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P001", "HB001", "商品一", "Product One", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-1",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item => item.ProductCode == "P001" && item.DetailHguid == "D001");
        var product = await _db.Queryable<Product>().SingleAsync(p => p.ProductCode == "P001");
        var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(p => p.ProductCode == "P001");
        var storeRetailPrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(p => p.ProductCode == "P001");

        Assert.Equal(0, product.ProductType);
        Assert.Equal(1.2m, product.PurchasePrice);
        Assert.Equal(3.4m, product.RetailPrice);
        Assert.Equal(1.2m, warehouseProduct.ImportPrice);
        Assert.Equal(3.4m, warehouseProduct.OEMPrice);
        Assert.Equal(1.2m, storeRetailPrice.PurchasePrice);
        Assert.Equal(3.4m, storeRetailPrice.StoreRetailPriceValue);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesSetProductWhenRelationIsComplete()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-SET-OK", "C001", "P-SET-OK", "套装商品", 2.2m, 5.5m);
        await InsertDomesticProductAsync("P-SET-OK", "HB-SET-OK", "完整套装", "Complete Set", 1);
        await InsertDomesticSetProductAsync("P-SET-OK", "SET-CODE-1", "HB-SET-OK-A");

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-set-ok",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-OK" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);

        var setCode = await _db.Queryable<ProductSetCode>()
            .FirstAsync(item => item.ProductCode == "P-SET-OK");
        Assert.NotNull(setCode);
        Assert.Equal("SET-CODE-1", setCode.SetProductCode);

        var product = await _db.Queryable<Product>().SingleAsync(p => p.ProductCode == "P-SET-OK");
        Assert.Equal(1, product.ProductType);

        var storeMultiCode = await _db.Queryable<StoreMultiCodeProduct>()
            .FirstAsync(item => item.ProductCode == "P-SET-OK");
        Assert.NotNull(storeMultiCode);
        Assert.Equal("SET-CODE-1", storeMultiCode.MultiCodeProductCode);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesSetRelationsFromSameMixedGroupChildrenWhenMainOnlySelected()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync(
            "D-SET-MAIN",
            "C001",
            "P-SET-MAIN",
            "套装商品",
            10m,
            18.8m,
            mixedGroupCode: "MG-001"
        );
        await InsertDomesticProductAsync("P-SET-MAIN", "HB-SET-MAIN", "主套装", "Main Set", 1);
        await InsertContainerDetailAsync(
            "D-SET-CHILD-A",
            "C001",
            "P-SET-CHILD-A",
            "套装子商品",
            2.2m,
            4m,
            domesticPrice: 12.5m,
            mixedGroupCode: "MG-001",
            setQuantity: 2m
        );
        await InsertDomesticProductAsync("P-SET-CHILD-A", "HB-SET-CHILD-A", "套装子项", "Set Child A", 0);
        await InsertContainerDetailAsync(
            "D-SET-CHILD-B",
            "C001",
            "P-SET-CHILD-B",
            "套装子商品",
            9.9m,
            6m,
            domesticPrice: 13.5m,
            mixedGroupCode: "MG-001",
            setQuantity: 1m
        );
        await InsertDomesticProductAsync("P-SET-CHILD-B", "HB-SET-CHILD-B", "套装子项B", "Set Child B", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-set-main-only",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-MAIN" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);

        var product = await _db.Queryable<Product>().SingleAsync(p => p.ProductCode == "P-SET-MAIN");
        var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(p => p.ProductCode == "P-SET-MAIN");
        var domesticSets = await _db.Queryable<DomesticSetProduct>()
            .Where(item => item.ProductCode == "P-SET-MAIN")
            .OrderBy(item => item.SetProductCode)
            .ToListAsync();
        var productSetCodes = await _db.Queryable<ProductSetCode>()
            .Where(item => item.ProductCode == "P-SET-MAIN")
            .OrderBy(item => item.SetProductCode)
            .ToListAsync();
        var storeMultiCodes = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(item => item.ProductCode == "P-SET-MAIN")
            .OrderBy(item => item.MultiCodeProductCode)
            .ToListAsync();

        Assert.Equal("HB-SET-MAIN", product.ItemNumber);
        Assert.Equal("P-SET-MAIN", warehouseProduct.ProductCode);
        Assert.Equal(2, domesticSets.Count);
        Assert.Equal(2, productSetCodes.Count);
        Assert.Equal(2, storeMultiCodes.Count);
        Assert.Contains(domesticSets, item =>
            item.SetProductCode == "P-SET-CHILD-A"
            && item.ProductNo == "HB-SET-MAIN"
            && item.SetProductNo == "HB-SET-CHILD-A"
            && item.SetBarcode == "BAR-HB-SET-CHILD-A"
            && item.DomesticPrice == 12.5m
            && item.ImportPrice == 2.2m
            && item.OEMPrice == 4m
        );
        Assert.Contains(productSetCodes, item =>
            item.SetProductCode == "P-SET-CHILD-A"
            && item.SetItemNumber == "HB-SET-CHILD-A"
            && item.SetRetailPrice == 4m
            && item.SetPurchasePrice == 4m
        );
        Assert.Contains(productSetCodes, item =>
            item.SetProductCode == "P-SET-CHILD-B"
            && item.SetItemNumber == "HB-SET-CHILD-B"
            && item.SetRetailPrice == 6m
            && item.SetPurchasePrice == 6m
        );
        Assert.Contains(storeMultiCodes, item =>
            item.StoreCode == "S001"
            && item.MultiCodeProductCode == "P-SET-CHILD-A"
            && item.MultiCodeRetailPrice == 4m
            && item.PurchasePrice == 4m
        );
        Assert.Contains(storeMultiCodes, item =>
            item.StoreCode == "S001"
            && item.MultiCodeProductCode == "P-SET-CHILD-B"
            && item.MultiCodeRetailPrice == 6m
            && item.PurchasePrice == 6m
        );
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCountSelectedSetChildWhenSameBatchCreatesMainSet()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync(
            "D-SET-MAIN-BATCH",
            "C001",
            "P-SET-MAIN-BATCH",
            "套装商品",
            8.8m,
            18.8m,
            mixedGroupCode: "MG-002"
        );
        await InsertDomesticProductAsync("P-SET-MAIN-BATCH", "HB-SET-MAIN-BATCH", "批量套装", "Batch Set", 1);
        await InsertContainerDetailAsync(
            "D-SET-CHILD-BATCH",
            "C001",
            "P-SET-CHILD-BATCH",
            "套装子商品",
            2.2m,
            6.6m,
            mixedGroupCode: "MG-002"
        );
        await InsertDomesticProductAsync("P-SET-CHILD-BATCH", "HB-SET-CHILD-BATCH", "批量子项", "Batch Child", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-set-main-child",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-MAIN-BATCH", "D-SET-CHILD-BATCH" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item => item.ProductCode == "P-SET-MAIN-BATCH");
        Assert.Equal(0, await _db.Queryable<Product>().Where(p => p.ProductCode == "P-SET-CHILD-BATCH").CountAsync());
        Assert.Equal(1, await _db.Queryable<ProductSetCode>().Where(p => p.ProductCode == "P-SET-MAIN-BATCH").CountAsync());
        Assert.Equal(1, await _db.Queryable<StoreMultiCodeProduct>().Where(p => p.ProductCode == "P-SET-MAIN-BATCH").CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_CompletesSetCodesForExistingSetProductFromLocalSetChildTable()
    {
        await InsertActiveStoreAsync("S001");
        await InsertExistingProductAsync("P-SET-EXISTS", "HB-SET-EXISTS", 8.8m, 18.8m);
        await InsertExistingWarehouseProductAsync("P-SET-EXISTS", 1.1m, 2.2m, 3.3m);
        await InsertContainerDetailAsync(
            "D-SET-EXISTS",
            "C001",
            "P-SET-EXISTS",
            "套装商品",
            8.8m,
            18.8m,
            mixedGroupCode: "MG-EXISTS"
        );
        await InsertDomesticProductAsync("P-SET-EXISTS", "HB-SET-EXISTS", "已存在套装", "Existing Set", 1);
        await InsertDomesticSetProductAsync("P-SET-EXISTS", "P-SET-EXISTS-CHILD", "HB-SET-EXISTS-CHILD");

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-existing-set-complete",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-EXISTS" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item =>
            item.ProductCode == "P-SET-EXISTS" && item.Message == "套装子码已补齐"
        );
        Assert.Equal(1, await _db.Queryable<Product>().Where(p => p.ProductCode == "P-SET-EXISTS").CountAsync());
        Assert.Equal(1, await _db.Queryable<WarehouseProduct>().Where(p => p.ProductCode == "P-SET-EXISTS").CountAsync());

        var domesticSet = await _db.Queryable<DomesticSetProduct>()
            .SingleAsync(item => item.ProductCode == "P-SET-EXISTS");
        var productSetCode = await _db.Queryable<ProductSetCode>()
            .SingleAsync(item => item.ProductCode == "P-SET-EXISTS");
        var storeMultiCode = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(item => item.ProductCode == "P-SET-EXISTS");

        Assert.Equal("P-SET-EXISTS-CHILD", domesticSet.SetProductCode);
        Assert.Equal("HB-SET-EXISTS-CHILD", domesticSet.SetProductNo);
        Assert.Equal("P-SET-EXISTS-CHILD", productSetCode.SetProductCode);
        Assert.Equal("HB-SET-EXISTS-CHILD", productSetCode.SetItemNumber);
        Assert.Equal("P-SET-EXISTS-CHILD", storeMultiCode.MultiCodeProductCode);
        Assert.Equal("S001", storeMultiCode.StoreCode);

        var product = await _db.Queryable<Product>().SingleAsync(p => p.ProductCode == "P-SET-EXISTS");
        var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(p => p.ProductCode == "P-SET-EXISTS");
        Assert.Equal(1, product.ProductType);
        Assert.Equal(8.8m, product.PurchasePrice);
        Assert.Equal(18.8m, product.RetailPrice);
        Assert.Equal(1.1m, warehouseProduct.DomesticPrice);
        Assert.Equal(2.2m, warehouseProduct.ImportPrice);
        Assert.Equal(3.3m, warehouseProduct.OEMPrice);
    }

    [Fact]
    public async Task ExecuteAsync_FixesExistingSetProductTypeWithoutChangingMainProductFields()
    {
        await InsertActiveStoreAsync("S001");
        await InsertExistingProductAsync("P-SET-TYPE-FIX", "HB-SET-TYPE-FIX", 8.8m, 18.8m, productType: 0);
        await InsertExistingWarehouseProductAsync("P-SET-TYPE-FIX", 1.1m, 2.2m, 3.3m);
        await InsertContainerDetailAsync(
            "D-SET-TYPE-FIX",
            "C001",
            "P-SET-TYPE-FIX",
            "套装商品",
            9.9m,
            19.9m
        );
        await InsertDomesticProductAsync("P-SET-TYPE-FIX", "HB-SET-TYPE-FIX", "套装类型修复", "Type Fix Set", 1);
        await InsertDomesticSetProductAsync("P-SET-TYPE-FIX", "P-SET-TYPE-FIX-CHILD", "HB-SET-TYPE-FIX-CHILD");

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-existing-set-type-fix",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-TYPE-FIX" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item => item.ProductCode == "P-SET-TYPE-FIX");
        Assert.Equal(1, await _db.Queryable<Product>().Where(p => p.ProductCode == "P-SET-TYPE-FIX").CountAsync());
        Assert.Equal(1, await _db.Queryable<WarehouseProduct>().Where(p => p.ProductCode == "P-SET-TYPE-FIX").CountAsync());

        var product = await _db.Queryable<Product>().SingleAsync(p => p.ProductCode == "P-SET-TYPE-FIX");
        var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(p => p.ProductCode == "P-SET-TYPE-FIX");
        Assert.Equal(1, product.ProductType);
        Assert.Equal("HB-SET-TYPE-FIX", product.ProductName);
        Assert.Equal("HB-SET-TYPE-FIX", product.EnglishName);
        Assert.Equal(8.8m, product.PurchasePrice);
        Assert.Equal(18.8m, product.RetailPrice);
        Assert.Equal(1.1m, warehouseProduct.DomesticPrice);
        Assert.Equal(2.2m, warehouseProduct.ImportPrice);
        Assert.Equal(3.3m, warehouseProduct.OEMPrice);
    }

    [Fact]
    public async Task ExecuteAsync_LoadsExistingSetChildrenFromHqWhenLocalSetChildTableIsEmpty()
    {
        await InsertActiveStoreAsync("S001");
        await InsertExistingProductAsync("P-SET-HQ", "HB-SET-HQ", 8.8m, 18.8m);
        await InsertExistingWarehouseProductAsync("P-SET-HQ", 1.1m, 2.2m, 3.3m);
        await InsertContainerDetailAsync("D-SET-HQ", "C001", "P-SET-HQ", "套装商品", 8.8m, 18.8m);
        await InsertDomesticProductAsync("P-SET-HQ", "HB-SET-HQ", "HQ套装", "HQ Set", 1);
        await InsertHqDomesticSetProductAsync(
            "HQ-SET-CODE-1",
            "P-SET-HQ",
            "HB-SET-HQ-01",
            "BAR-HQ-SET-01",
            domesticPrice: 12.5m,
            importPrice: 2.5m,
            oemPrice: 6.5m
        );

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-existing-set-hq",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-HQ" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item =>
            item.ProductCode == "P-SET-HQ" && item.Message == "套装子码已补齐"
        );

        var domesticSet = await _db.Queryable<DomesticSetProduct>().SingleAsync(x => x.ProductCode == "P-SET-HQ");
        var productSetCode = await _db.Queryable<ProductSetCode>().SingleAsync(x => x.ProductCode == "P-SET-HQ");
        var storeMultiCode = await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.ProductCode == "P-SET-HQ");

        Assert.Equal("HB-SET-HQ-01", domesticSet.SetProductCode);
        Assert.Equal("HB-SET-HQ", domesticSet.ProductNo);
        Assert.Equal("HB-SET-HQ-01", domesticSet.SetProductNo);
        Assert.Equal("BAR-HQ-SET-01", domesticSet.SetBarcode);
        Assert.Equal(12.5m, domesticSet.DomesticPrice);
        Assert.Equal("HB-SET-HQ-01", productSetCode.SetProductCode);
        Assert.Equal(8.8m, productSetCode.SetPurchasePrice);
        Assert.Equal("HB-SET-HQ-01", storeMultiCode.MultiCodeProductCode);
        Assert.Equal(6.5m, storeMultiCode.MultiCodeRetailPrice);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsExistingSetProductWithSetChildNotFoundInsteadOfDuplicate()
    {
        await InsertExistingProductAsync("P-SET-NO-CHILD", "HB-SET-NO-CHILD", 8.8m, 18.8m);
        await InsertExistingWarehouseProductAsync("P-SET-NO-CHILD", 1.1m, 2.2m, 3.3m);
        await InsertContainerDetailAsync(
            "D-SET-NO-CHILD",
            "C001",
            "P-SET-NO-CHILD",
            "套装商品",
            8.8m,
            18.8m,
            mixedGroupCode: "MG-NO-CHILD"
        );
        await InsertDomesticProductAsync("P-SET-NO-CHILD", "HB-SET-NO-CHILD", "无子项套装", "No Child Set", 1);
        await InsertContainerDetailAsync(
            "D-SET-NO-CHILD-GROUP-CHILD",
            "C001",
            "P-SET-NO-CHILD-GROUP-CHILD",
            "套装子商品",
            2.2m,
            6.6m,
            mixedGroupCode: "MG-NO-CHILD"
        );
        await InsertDomesticProductAsync(
            "P-SET-NO-CHILD-GROUP-CHILD",
            "HB-SET-NO-CHILD-GROUP-CHILD",
            "同组子项",
            "Same Group Child",
            0
        );

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-existing-set-no-child",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-NO-CHILD" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Skipped, item =>
            item.ProductCode == "P-SET-NO-CHILD"
            && item.DetailHguid == "D-SET-NO-CHILD"
            && item.ReasonCode == "SET_CHILD_NOT_FOUND"
        );
        Assert.DoesNotContain(result.Skipped, item => item.ReasonCode == "DUPLICATE_PRODUCT_CODE");
    }

    [Fact]
    public async Task ExecuteAsync_CompletesMissingProductSetAndStoreMultiCodesForExistingSetRelation()
    {
        await InsertActiveStoreAsync("S001");
        await InsertActiveStoreAsync("S002");
        await InsertExistingProductAsync("P-SET-PARTIAL", "HB-SET-PARTIAL", 10m, 18.8m);
        await InsertExistingWarehouseProductAsync("P-SET-PARTIAL", 1.1m, 2.2m, 3.3m);
        await InsertContainerDetailAsync(
            "D-SET-PARTIAL",
            "C001",
            "P-SET-PARTIAL",
            "套装商品",
            8.8m,
            18.8m,
            mixedGroupCode: "MG-PARTIAL"
        );
        await InsertDomesticProductAsync("P-SET-PARTIAL", "HB-SET-PARTIAL", "部分套装", "Partial Set", 1);
        await InsertDomesticSetProductAsync("P-SET-PARTIAL", "P-SET-PARTIAL-CHILD", "HB-SET-PARTIAL-CHILD", importPrice: 9.9m, oemPrice: 4m);
        await InsertDomesticSetProductAsync("P-SET-PARTIAL", "P-SET-PARTIAL-CHILD-B", "HB-SET-PARTIAL-CHILD-B", importPrice: 8.8m, oemPrice: 6m);
        await InsertProductSetCodeAsync("P-SET-PARTIAL", "P-SET-PARTIAL-CHILD", "HB-SET-PARTIAL-CHILD", purchasePrice: 9.9m, retailPrice: 4m);
        await InsertStoreMultiCodeAsync("S001", "P-SET-PARTIAL", "P-SET-PARTIAL-CHILD", "BAR-HB-SET-PARTIAL-CHILD");

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-existing-set-partial",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-PARTIAL" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item =>
            item.ProductCode == "P-SET-PARTIAL" && item.Message == "套装子码已补齐"
        );
        Assert.Equal(2, await _db.Queryable<ProductSetCode>().Where(p => p.ProductCode == "P-SET-PARTIAL").CountAsync());
        Assert.Equal(4, await _db.Queryable<StoreMultiCodeProduct>().Where(p => p.ProductCode == "P-SET-PARTIAL").CountAsync());
        var updatedSetCode = await _db.Queryable<ProductSetCode>()
            .SingleAsync(p => p.ProductCode == "P-SET-PARTIAL" && p.SetProductCode == "P-SET-PARTIAL-CHILD");
        var insertedSetCode = await _db.Queryable<ProductSetCode>()
            .SingleAsync(p => p.ProductCode == "P-SET-PARTIAL" && p.SetProductCode == "P-SET-PARTIAL-CHILD-B");
        Assert.Equal(4m, updatedSetCode.SetPurchasePrice);
        Assert.Equal(6m, insertedSetCode.SetPurchasePrice);
        Assert.NotNull(await _db.Queryable<StoreMultiCodeProduct>()
            .FirstAsync(p => p.ProductCode == "P-SET-PARTIAL" && p.StoreCode == "S002"));
    }

    [Fact]
    public async Task ExecuteAsync_DistributesRoundingDifferenceToLastSetChild()
    {
        await InsertActiveStoreAsync("S001");
        await InsertExistingProductAsync("P-SET-ROUND", "HB-SET-ROUND", 10m, 18.8m);
        await InsertExistingWarehouseProductAsync("P-SET-ROUND", 1.1m, 2.2m, 3.3m);
        await InsertContainerDetailAsync("D-SET-ROUND", "C001", "P-SET-ROUND", "套装商品", 10m, 18.8m);
        await InsertDomesticProductAsync("P-SET-ROUND", "HB-SET-ROUND", "舍入套装", "Round Set", 1);
        await InsertDomesticSetProductAsync("P-SET-ROUND", "P-SET-ROUND-A", "HB-SET-ROUND-A", importPrice: 1m, oemPrice: 1m);
        await InsertDomesticSetProductAsync("P-SET-ROUND", "P-SET-ROUND-B", "HB-SET-ROUND-B", importPrice: 1m, oemPrice: 1m);
        await InsertDomesticSetProductAsync("P-SET-ROUND", "P-SET-ROUND-C", "HB-SET-ROUND-C", importPrice: 1m, oemPrice: 1m);

        var result = await CreateService().ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-existing-set-round",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-ROUND" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        var setCodes = await _db.Queryable<ProductSetCode>()
            .Where(p => p.ProductCode == "P-SET-ROUND")
            .OrderBy(p => p.SetProductCode)
            .ToListAsync();
        Assert.Equal(new[] { 3.33m, 3.33m, 3.34m }, setCodes.Select(item => item.SetPurchasePrice.GetValueOrDefault()).ToArray());
        Assert.Equal(10m, setCodes.Sum(item => item.SetPurchasePrice));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCompleteWhenExistingSetCodesAreAlreadyComplete()
    {
        await InsertActiveStoreAsync("S001");
        await InsertExistingProductAsync("P-SET-COMPLETE", "HB-SET-COMPLETE", 8.8m, 18.8m, productType: 1);
        await InsertExistingWarehouseProductAsync("P-SET-COMPLETE", 1.1m, 2.2m, 3.3m);
        await InsertContainerDetailAsync(
            "D-SET-COMPLETE",
            "C001",
            "P-SET-COMPLETE",
            "套装商品",
            8.8m,
            18.8m,
            mixedGroupCode: "MG-COMPLETE"
        );
        await InsertDomesticProductAsync("P-SET-COMPLETE", "HB-SET-COMPLETE", "完整已存在套装", "Complete Existing Set", 1);
        await InsertDomesticSetProductAsync("P-SET-COMPLETE", "P-SET-COMPLETE-CHILD", "HB-SET-COMPLETE-CHILD");
        await InsertProductSetCodeAsync(
            "P-SET-COMPLETE",
            "P-SET-COMPLETE-CHILD",
            "HB-SET-COMPLETE-CHILD",
            purchasePrice: 8.8m,
            retailPrice: 5.5m
        );
        await InsertStoreMultiCodeAsync(
            "S001",
            "P-SET-COMPLETE",
            "P-SET-COMPLETE-CHILD",
            "BAR-HB-SET-COMPLETE-CHILD",
            purchasePrice: 8.8m
        );

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-existing-set-complete-noop",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-COMPLETE" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item =>
            item.ProductCode == "P-SET-COMPLETE" && item.Message == "套装子码已完整"
        );
        Assert.Equal(1, await _db.Queryable<DomesticSetProduct>().Where(p => p.ProductCode == "P-SET-COMPLETE").CountAsync());
        Assert.Equal(1, await _db.Queryable<ProductSetCode>().Where(p => p.ProductCode == "P-SET-COMPLETE").CountAsync());
        Assert.Equal(1, await _db.Queryable<StoreMultiCodeProduct>().Where(p => p.ProductCode == "P-SET-COMPLETE").CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRowsWithMissingEnglishNameAndSetChild()
    {
        await InsertContainerDetailAsync("D-MISSING-EN", "C001", "P-MISSING-EN", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-MISSING-EN", "HB-MISSING-EN", "缺英文", null, 0);
        await InsertContainerDetailAsync("D-SET-CHILD", "C001", "P-SET-CHILD", "套装子商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-SET-CHILD", "HB-SET-CHILD", "套装子", "Set Child", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-2",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-MISSING-EN", "D-SET-CHILD" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-MISSING-EN" && item.ReasonCode == "MISSING_ENGLISH_NAME");
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-SET-CHILD" && item.ReasonCode == "MISSING_SET_RELATION");
    }

    [Fact]
    public async Task ExecuteAsync_UsesChineseNameWrittenByBatchUpdateDetails()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-NAME-SAVED", "C001", "P-NAME-SAVED", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-NAME-SAVED", "HB-NAME-SAVED", null, "Saved Belt", 0);
        var containerReactService = CreateContainerReactService();
        await containerReactService.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-NAME-SAVED", 商品名称 = "保存后的皮带" },
            }
        );
        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-name-saved",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-NAME-SAVED" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.DoesNotContain(result.Skipped, item => item.ReasonCode == "MISSING_CHINESE_NAME");
        var domesticProduct = await _db.Queryable<DomesticProduct>().SingleAsync(p => p.ProductCode == "P-NAME-SAVED");
        Assert.Equal("保存后的皮带", domesticProduct.ProductName);
    }

    [Fact]
    public async Task ExecuteAsync_UsesTargetCategoryWrittenByBatchUpdateDetails()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-CATEGORY-SAVED", "C001", "P-CATEGORY-SAVED", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-CATEGORY-SAVED", "HB-CATEGORY-SAVED", "分类商品", "Category Product", 0);
        await InsertWarehouseCategoryAsync("CAT-CREATED");
        var containerReactService = CreateContainerReactService();
        await containerReactService.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-CATEGORY-SAVED", ProductCategoryGUID = "CAT-CREATED" },
            }
        );
        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-category-saved",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-CATEGORY-SAVED" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        var product = await _db.Queryable<Product>().SingleAsync(p => p.ProductCode == "P-CATEGORY-SAVED");
        Assert.Equal("CAT-CREATED", product.WarehouseCategoryGUID);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRowsWithInvalidRetailPrice()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-INVALID-RETAIL", "C001", "P-INVALID-RETAIL", "普通商品", 1.2m, 0m);
        await InsertDomesticProductAsync("P-INVALID-RETAIL", "HB-INVALID-RETAIL", "无零售价商品", "No Retail Price", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-invalid-retail",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-INVALID-RETAIL" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Contains(result.Skipped, item =>
            item.DetailHguid == "D-INVALID-RETAIL"
            && item.ReasonCode == "INVALID_OEM_PRICE"
            && item.Message == "零售价必须大于 0"
        );
    }

    [Fact]
    public async Task ExecuteAsync_SkipsDuplicateProductCodeAndSetWithoutRelation()
    {
        await _db.Insertable(new Product
        {
            UUID = "LP001",
            ProductCode = "P-EXISTS",
            ItemNumber = "HB-EXISTS",
            ProductName = "已存在",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await InsertContainerDetailAsync("D-EXISTS", "C001", "P-EXISTS", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-EXISTS", "HB-EXISTS", "已存在", "Exists", 0);
        await InsertContainerDetailAsync("D-SET", "C001", "P-SET", "套装商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-SET", "HB-SET", "套装", "Set Product", 1);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-3",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-EXISTS", "D-SET" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-EXISTS" && item.ReasonCode == "DUPLICATE_PRODUCT_CODE");
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-SET" && item.ReasonCode == "MISSING_SET_RELATION");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRowsWithDuplicateLocalItemNumber()
    {
        await _db.Insertable(new Product
        {
            UUID = "LP-DUP-ITEM",
            ProductCode = "P-OTHER",
            ItemNumber = "HB-DUP",
            ProductName = "同货号商品",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await InsertContainerDetailAsync("D-DUP-ITEM", "C001", "P-DUP-ITEM", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-DUP-ITEM", "HB-DUP", "重复货号", "Duplicate Item", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-dup-item",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-DUP-ITEM" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-DUP-ITEM" && item.ReasonCode == "DUPLICATE_ITEM_NUMBER");
        Assert.Equal(0, await _db.Queryable<WarehouseProduct>().Where(p => p.ProductCode == "P-DUP-ITEM").CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SubmitContainer_CreatesNewProductsUpdatesExistingPricesAndCompletesContainer()
    {
        await InsertContainerAsync("C-SUBMIT", status: 1);
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-NEW", "C-SUBMIT", "P-NEW", "普通商品", 1.2m, 3.4m, domesticPrice: 9.9m);
        await InsertDomesticProductAsync("P-NEW", "HB-NEW", "新商品", "New Product", 0);
        await InsertContainerDetailAsync("D-EXISTING", "C-SUBMIT", "P-EXISTING", "普通商品", 5.6m, 7.8m, domesticPrice: 11.2m);
        await InsertDomesticProductAsync("P-EXISTING", "HB-EXISTING", "已有商品来源", "Existing Source", 0);
        await InsertExistingProductAsync("P-EXISTING", "HB-EXISTING", 1.1m, 2.2m);
        await InsertExistingWarehouseProductAsync("P-EXISTING", domesticPrice: 3.3m, importPrice: 4.4m, oemPrice: 5.5m);

        var result = await CreateService().ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "submit-container:C-SUBMIT",
                ContainerGuid = "C-SUBMIT",
                SubmitContainer = true,
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(result.ContainerCompleted);
        Assert.Contains(result.Created, item => item.ProductCode == "P-NEW" && item.DetailHguid == "D-NEW");
        Assert.Contains(result.Updated, item => item.ProductCode == "P-EXISTING" && item.DetailHguid == "D-EXISTING");

        var container = await _db.Queryable<Container>().SingleAsync(item => item.ContainerCode == "C-SUBMIT");
        Assert.Equal(2, container.Status);

        var existingProduct = await _db.Queryable<Product>().SingleAsync(item => item.ProductCode == "P-EXISTING");
        var existingWarehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(item => item.ProductCode == "P-EXISTING");
        var storeRetailPrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(item => item.ProductCode == "P-EXISTING");
        var storeMultiCode = await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(item => item.ProductCode == "P-EXISTING");

        Assert.Equal(5.6m, existingProduct.PurchasePrice);
        Assert.Equal(11.2m, existingWarehouseProduct.DomesticPrice);
        Assert.Equal(5.6m, existingWarehouseProduct.ImportPrice);
        Assert.Equal(7.8m, existingWarehouseProduct.OEMPrice);
        Assert.Equal(0.12m, existingWarehouseProduct.Volume);
        Assert.Equal(5.6m, storeRetailPrice.PurchasePrice);
        Assert.Equal(7.8m, storeRetailPrice.StoreRetailPriceValue);
        Assert.Equal(5.6m, storeMultiCode.PurchasePrice);
        Assert.Equal(7.8m, storeMultiCode.MultiCodeRetailPrice);
    }

    [Fact]
    public async Task ExecuteAsync_SubmitContainer_UpdatesOnlyPricesForExistingProducts()
    {
        await InsertContainerAsync("C-PRICE-ONLY", status: 1);
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-PRICE-ONLY", "C-PRICE-ONLY", "P-PRICE-ONLY", "普通商品", 8.8m, 9.9m, domesticPrice: 10.1m);
        await InsertDomesticProductAsync("P-PRICE-ONLY", "HB-PRICE-ONLY", "国内名称", "Domestic Name", 0);
        await InsertExistingProductAsync("P-PRICE-ONLY", "HB-PRICE-ONLY", 1.1m, 2.2m);
        await InsertExistingWarehouseProductAsync("P-PRICE-ONLY", domesticPrice: 3.3m, importPrice: 4.4m, oemPrice: 5.5m);
        await _db.Updateable<Product>()
            .SetColumns(item => new Product
            {
                ProductName = "保留名称",
                EnglishName = "Keep Name",
                ProductType = 0,
                WarehouseCategoryGUID = "KEEP-CAT",
                IsActive = false,
            })
            .Where(item => item.ProductCode == "P-PRICE-ONLY")
            .ExecuteCommandAsync();
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct
            {
                IsActive = false,
            })
            .Where(item => item.ProductCode == "P-PRICE-ONLY")
            .ExecuteCommandAsync();

        var result = await CreateService().ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "submit-container:C-PRICE-ONLY",
                ContainerGuid = "C-PRICE-ONLY",
                SubmitContainer = true,
            }
        );

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(result.ContainerCompleted);

        var product = await _db.Queryable<Product>().SingleAsync(item => item.ProductCode == "P-PRICE-ONLY");
        var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(item => item.ProductCode == "P-PRICE-ONLY");

        Assert.Equal(8.8m, product.PurchasePrice);
        Assert.Equal("保留名称", product.ProductName);
        Assert.Equal("Keep Name", product.EnglishName);
        Assert.Equal("KEEP-CAT", product.WarehouseCategoryGUID);
        Assert.False(product.IsActive);
        Assert.Equal(10.1m, warehouseProduct.DomesticPrice);
        Assert.Equal(8.8m, warehouseProduct.ImportPrice);
        Assert.Equal(9.9m, warehouseProduct.OEMPrice);
        Assert.False(warehouseProduct.IsActive);
    }

    [Fact]
    public async Task ExecuteAsync_SubmitContainer_DoesNotCompleteContainerWhenFailureExists()
    {
        await InsertContainerAsync("C-FAIL", status: 1);
        await InsertContainerDetailAsync("D-FAIL", "C-FAIL", "P-FAIL", "普通商品", 1.2m, 3.4m);

        var result = await CreateService().ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "submit-container:C-FAIL",
                ContainerGuid = "C-FAIL",
                SubmitContainer = true,
            }
        );

        Assert.False(result.ContainerCompleted);
        Assert.True(result.FailedCount > 0);
        var container = await _db.Queryable<Container>().SingleAsync(item => item.ContainerCode == "C-FAIL");
        Assert.Equal(1, container.Status);
    }

    [Fact]
    public async Task ExecuteAsync_SubmitContainer_LoadsOnlyCurrentContainerDetails()
    {
        await InsertContainerAsync("C-SCOPE", status: 1);
        await InsertContainerAsync("C-OTHER", status: 1);
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-SCOPE", "C-SCOPE", "P-SCOPE", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-SCOPE", "HB-SCOPE", "范围商品", "Scope Product", 0);
        await InsertContainerDetailAsync("D-OTHER", "C-OTHER", "P-OTHER", "普通商品", 2.2m, 4.4m);
        await InsertDomesticProductAsync("P-OTHER", "HB-OTHER", "其他商品", "Other Product", 0);

        var result = await CreateService().ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "submit-container:C-SCOPE",
                ContainerGuid = "C-SCOPE",
                SubmitContainer = true,
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Contains(result.Created, item => item.ProductCode == "P-SCOPE");
        Assert.DoesNotContain(result.Created, item => item.ProductCode == "P-OTHER");
        Assert.NotNull(await _db.Queryable<Product>().FirstAsync(item => item.ProductCode == "P-SCOPE"));
        Assert.Null(await _db.Queryable<Product>().FirstAsync(item => item.ProductCode == "P-OTHER"));
        var currentContainer = await _db.Queryable<Container>().SingleAsync(item => item.ContainerCode == "C-SCOPE");
        var otherContainer = await _db.Queryable<Container>().SingleAsync(item => item.ContainerCode == "C-OTHER");
        Assert.Equal(2, currentContainer.Status);
        Assert.Equal(1, otherContainer.Status);
    }

    [Fact]
    public async Task JobService_ReusesSameOperationContainerAndDetails()
    {
        var executor = new BlockingContainerProductCreationExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IContainerProductCreationExecutorService>(executor);
        var provider = services.BuildServiceProvider();
        var jobService = new ContainerProductCreationJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContainerProductCreationJobService>.Instance
        );
        var request = new ContainerProductCreationJobRequestDto
        {
            OperationId = "op-reuse",
            ContainerGuid = "C001",
            DetailHguids = new List<string> { "D002", "D001" },
        };

        var first = await jobService.StartJobAsync("user-1", request);
        var second = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-reuse",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001", "D002" },
            }
        );
        executor.Release();
        await Task.Delay(50);

        Assert.Equal(first.JobId, second.JobId);
        Assert.True(second.IsDuplicateRequest);
    }

    [Fact]
    public async Task JobService_ReusesSameContainerAndDetailsWhenOperationIdChanges()
    {
        var executor = new BlockingContainerProductCreationExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IContainerProductCreationExecutorService>(executor);
        var provider = services.BuildServiceProvider();
        var jobService = new ContainerProductCreationJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContainerProductCreationJobService>.Instance
        );

        var first = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-reuse-a",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D002", "D001" },
            }
        );
        var second = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-reuse-b",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001", "D002" },
            }
        );
        executor.Release();
        await Task.Delay(50);

        Assert.Equal(first.JobId, second.JobId);
        Assert.True(second.IsDuplicateRequest);
    }

    [Fact]
    public async Task JobService_BlocksCrossUserJobResultAccess()
    {
        var executor = new BlockingContainerProductCreationExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IContainerProductCreationExecutorService>(executor);
        var provider = services.BuildServiceProvider();
        var jobService = new ContainerProductCreationJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContainerProductCreationJobService>.Instance
        );

        var job = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-user-1",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001" },
            }
        );

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => jobService.GetJobAsync("user-2", job.JobId)
        );
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => jobService.StartJobAsync(
                "user-2",
                new ContainerProductCreationJobRequestDto
                {
                    OperationId = "op-user-2",
                    ContainerGuid = "C001",
                    DetailHguids = new List<string> { "D001" },
                }
            )
        );

        executor.Release();
        await Task.Delay(50);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        _hbSalesDb.Dispose();
        _hbSalesSqliteConnection.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
        if (File.Exists(_hbSalesDbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_hbSalesDbPath);
        }
    }

    private async Task InsertActiveStoreAsync(string storeCode)
    {
        await _db.Insertable(new Store
        {
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertContainerAsync(string containerCode, int status)
    {
        await _db.Insertable(new Container
        {
            ContainerCode = containerCode,
            ContainerNumber = containerCode,
            Status = status,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertContainerDetailAsync(
        string detailCode,
        string containerCode,
        string productCode,
        string productType,
        decimal importPrice,
        decimal oemPrice,
        decimal domesticPrice = 10m,
        string? mixedGroupCode = null,
        decimal? setQuantity = null
    )
    {
        await _db.Insertable(new ContainerDetail
        {
            DetailCode = detailCode,
            ContainerCode = containerCode,
            ProductCode = productCode,
            ProductType = productType,
            DomesticPrice = domesticPrice,
            ImportPrice = importPrice,
            OEMPrice = oemPrice,
            MixedGroupCode = mixedGroupCode,
            SetQuantity = setQuantity,
            UnitVolume = 0.12m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertDomesticProductAsync(
        string productCode,
        string itemNumber,
        string? productName,
        string? englishName,
        int productType
    )
    {
        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            HBProductNo = itemNumber,
            ProductName = productName,
            EnglishProductName = englishName,
            Barcode = $"BAR-{itemNumber}",
            ProductType = productType,
            ProductImage = $"/{itemNumber}.jpg",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertDomesticSetProductAsync(
        string productCode,
        string setProductCode,
        string setProductNo,
        decimal importPrice = 2.2m,
        decimal oemPrice = 5.5m
    )
    {
        await _db.Insertable(new DomesticSetProduct
        {
            SetProductCode = setProductCode,
            ProductCode = productCode,
            ProductNo = productCode,
            SetProductNo = setProductNo,
            SetBarcode = $"BAR-{setProductNo}",
            ImportPrice = importPrice,
            OEMPrice = oemPrice,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertHqDomesticSetProductAsync(
        string hguid,
        string productCode,
        string setProductNo,
        string setBarcode,
        decimal domesticPrice,
        decimal importPrice,
        decimal oemPrice
    )
    {
        await _hbSalesDb.Insertable(new CPT_DIC_商品套装信息表
        {
            HGUID = hguid,
            商品编码 = productCode,
            商品小货号 = setProductNo,
            条形码 = setBarcode,
            国内价格 = domesticPrice,
            进口价格 = importPrice,
            贴牌价格 = oemPrice,
            使用状态 = 1,
        }).ExecuteCommandAsync();
    }

    private async Task InsertExistingProductAsync(
        string productCode,
        string itemNumber,
        decimal purchasePrice,
        decimal retailPrice,
        int? productType = null
    )
    {
        await _db.Insertable(new Product
        {
            UUID = $"LP-{productCode}",
            ProductCode = productCode,
            ItemNumber = itemNumber,
            ProductName = itemNumber,
            EnglishName = itemNumber,
            ProductType = productType,
            PurchasePrice = purchasePrice,
            RetailPrice = retailPrice,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertExistingWarehouseProductAsync(
        string productCode,
        decimal domesticPrice,
        decimal importPrice,
        decimal oemPrice
    )
    {
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            DomesticPrice = domesticPrice,
            ImportPrice = importPrice,
            OEMPrice = oemPrice,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertProductSetCodeAsync(
        string productCode,
        string setProductCode,
        string setItemNumber,
        decimal purchasePrice = 2.2m,
        decimal retailPrice = 5.5m
    )
    {
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = setProductCode,
            ProductCode = productCode,
            SetProductCode = setProductCode,
            SetItemNumber = setItemNumber,
            SetBarcode = $"BAR-{setItemNumber}",
            SetPurchasePrice = purchasePrice,
            SetRetailPrice = retailPrice,
            SetQuantity = 1,
            SetType = 1,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertStoreMultiCodeAsync(
        string storeCode,
        string productCode,
        string multiCodeProductCode,
        string multiBarcode,
        decimal purchasePrice = 2.2m,
        decimal retailPrice = 5.5m
    )
    {
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = $"{storeCode}-{multiCodeProductCode}",
            StoreCode = storeCode,
            ProductCode = productCode,
            MultiCodeProductCode = multiCodeProductCode,
            StoreMultiCodeProductCode = storeCode + multiCodeProductCode,
            MultiBarcode = multiBarcode,
            PurchasePrice = purchasePrice,
            MultiCodeRetailPrice = retailPrice,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private ContainerProductCreationExecutorService CreateService()
    {
        var configuration = new ConfigurationBuilder().Build();
        var context = CreateSqlSugarContext(_db);
        var itemBarcodeService = new ItemBarcodeService(
            context,
            NullLogger<ItemBarcodeService>.Instance,
            configuration
        );
        var warehouseService = new ProductWarehouseReactService(
            context,
            CreateHqSqlSugarContext(),
            NullLogger<ProductWarehouseReactService>.Instance,
            configuration,
            itemBarcodeService,
            Mock.Of<IMapper>(),
            Mock.Of<IDataSyncFullService>()
        );

        return new ContainerProductCreationExecutorService(
            context,
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            warehouseService,
            NullLogger<ContainerProductCreationExecutorService>.Instance
        );
    }

    private async Task InsertWarehouseCategoryAsync(string categoryGuid)
    {
        await _db.Insertable(new WarehouseCategory
        {
            CategoryGUID = categoryGuid,
            CategoryName = $"分类 {categoryGuid}",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private ContainerReactService CreateContainerReactService()
    {
        return new ContainerReactService(
            CreateSqlSugarContext(_db),
            CreateHqSqlSugarContext(),
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<ContainerReactService>.Instance,
            Mock.Of<IContainerHqSyncService>(),
            CreateTranslationServiceMock()
        );
    }

    private static ITranslationService CreateTranslationServiceMock()
    {
        var translationService = new Mock<ITranslationService>();
        translationService
            .Setup(service => service.ContainsChinese(It.IsAny<string>()))
            .Returns((string value) => value.Any(ch => ch >= '\u4e00' && ch <= '\u9fff'));
        translationService
            .Setup(service => service.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((List<string> values) => values.ToDictionary(value => value, value => value));
        return translationService.Object;
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext()
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HqSqlSugarContext)
        );
        var dbField = typeof(HqSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);
        return context;
    }

    private static HBSalesSqlSugarContext CreateHBSalesSqlSugarContext(SqlSugarScope db)
    {
        var context = (HBSalesSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HBSalesSqlSugarContext)
        );
        var dbField = typeof(HBSalesSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    private sealed class BlockingContainerProductCreationExecutor
        : IContainerProductCreationExecutorService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ContainerProductCreationResultDto> ExecuteAsync(
            ContainerProductCreationJobRequestDto request,
            CancellationToken cancellationToken = default
        )
        {
            await _release.Task.WaitAsync(cancellationToken);
            return new ContainerProductCreationResultDto();
        }

        public void Release() => _release.TrySetResult();
    }
}
