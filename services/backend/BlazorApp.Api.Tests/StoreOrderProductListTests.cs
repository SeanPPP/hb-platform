using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderProductListTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;
    private readonly List<string> _sqlLogs = new();

    public StoreOrderProductListTests()
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
        _db.Aop.OnLogExecuting = (sql, parameters) => _sqlLogs.Add(FormatSqlLog(parameters, sql));

        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(WarehouseCategory),
            typeof(ProductGrade),
            typeof(HBLocalSupplier),
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(Store),
            typeof(DomesticProduct),
            typeof(ChinaSupplier),
            typeof(CPT_DIC_外购客户信息表),
            typeof(ProductLocation),
            typeof(Location)
        );
        _db.Ado.ExecuteCommand("DROP TABLE ProductGrade");
        _db.Ado.ExecuteCommand(
            """
            CREATE TABLE ProductGrade (
                Id TEXT PRIMARY KEY NOT NULL,
                ProductCode TEXT NOT NULL,
                Grade TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedAt TEXT NULL,
                UpdatedBy TEXT NULL,
                IsDeleted INTEGER NULL
            )
            """
        );
    }

    [Fact]
    public async Task GetPagedListAsync_DoesNotDuplicateProductsWhenProductHasMultipleGrades()
    {
        await SeedProductWithGradesAsync("P001", "ITEM-001", "A", "B");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P001", item.ProductCode);
        Assert.Equal("ITEM-001-BAR", item.Barcode);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetPagedListAsync_GradeFilterKeepsMatchingProduct()
    {
        await SeedProductWithGradesAsync("P001", "ITEM-001", "A", "B");
        await SeedProductWithGradesAsync("P002", "ITEM-002", "C");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            Grade = "B",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P001", item.ProductCode);
        Assert.Equal("B", item.Grade);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeExistingWarehouseProducts_ReturnsOnlyProductMasterRows()
    {
        await SeedLocalSupplierAsync("200", "Hot Bargain");
        await SeedProductAsync("P001", "ITEM-001", localSupplierCode: "200", purchasePrice: 2.5m);
        await SeedProductAsync("P002", "ITEM-002", localSupplierCode: "201", purchasePrice: 3.5m);
        await SeedProductAsync("P003", "ITEM-003", localSupplierCode: "200", purchasePrice: 4.5m);
        await SeedWarehouseProductAsync("P002", isDeleted: false);
        await SeedWarehouseProductAsync("P003", isDeleted: true);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            LocalSupplierCode = "200",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        Assert.Equal(2, result.Total);
        Assert.Collection(
            result.Items,
            first =>
            {
                Assert.Equal("P001", first.ProductCode);
                Assert.Equal("ITEM-001-BAR", first.Barcode);
                Assert.Equal("200", first.LocalSupplierCode);
                Assert.Equal("Hot Bargain", first.LocalSupplierName);
                Assert.Equal(2.5m, first.ImportPrice);
                Assert.Equal(0, first.OEMPrice);
                Assert.Equal(1, first.MinOrderQuantity);
                Assert.Equal(0, first.StockQuantity);
            },
            second => Assert.Equal("P003", second.ProductCode)
        );
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeOrderGUID_RemovesProductsAlreadyInOrder()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedProductAsync("P002", "ITEM-002");
        await SeedDeletedOrderDetailAsync("ORDER-001", "P001", isDeleted: false);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            ExcludeOrderGUID = "ORDER-001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P002", item.ProductCode);
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeExistingWarehouseProducts_AllowsSoftDeletedWarehouseRecord()
    {
        await SeedLocalSupplierAsync("200", "Hot Bargain");
        await SeedProductAsync("P010", "ITEM-010", localSupplierCode: "200", purchasePrice: 6.5m);
        await SeedWarehouseProductAsync("P010", isDeleted: true);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            LocalSupplierCode = "200",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P010", item.ProductCode);
        Assert.Equal("ITEM-010-BAR", item.Barcode);
        Assert.Equal(6.5m, item.ImportPrice);
        Assert.Equal(0, item.OEMPrice);
        Assert.Equal(1, item.MinOrderQuantity);
        Assert.Equal(0, item.StockQuantity);
    }

    [Fact]
    public async Task GetPagedListAsync_DefaultQueryStillReturnsWarehouseProductsOnly()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedProductAsync("P002", "ITEM-002");
        await SeedProductAsync("P003", "ITEM-003");
        await SeedWarehouseProductAsync("P001", isDeleted: false, oemPrice: 10m, importPrice: 7m);
        await SeedWarehouseProductAsync("P003", isDeleted: false, isActive: false);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P001", item.ProductCode);
        Assert.Equal("ITEM-001-BAR", item.Barcode);
        Assert.Equal(10m, item.OEMPrice);
        Assert.Equal(7m, item.ImportPrice);
    }

    [Fact]
    public async Task GetPagedListAsync_QuickAddQueryIncludesInactiveWarehouseProductsWhenFlagEnabled()
    {
        await SeedProductAsync("P-QUICK-ACTIVE", "QUICK-ACTIVE");
        await SeedWarehouseProductAsync("P-QUICK-ACTIVE");
        await SeedProductAsync("P-QUICK-INACTIVE-PRODUCT", "QUICK-INACTIVE-PRODUCT", isActive: false);
        await SeedWarehouseProductAsync("P-QUICK-INACTIVE-PRODUCT");
        await SeedProductAsync("P-QUICK-INACTIVE-WAREHOUSE", "QUICK-INACTIVE-WAREHOUSE");
        await SeedWarehouseProductAsync("P-QUICK-INACTIVE-WAREHOUSE", isActive: false);
        await SeedProductAsync("P-QUICK-DELETED-PRODUCT", "QUICK-DELETED-PRODUCT", isDeleted: true);
        await SeedWarehouseProductAsync("P-QUICK-DELETED-PRODUCT");
        await SeedProductAsync("P-QUICK-DELETED-WAREHOUSE", "QUICK-DELETED-WAREHOUSE");
        await SeedWarehouseProductAsync("P-QUICK-DELETED-WAREHOUSE", isDeleted: true);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ItemNumber = "QUICK",
            IncludeInactiveWarehouseProducts = true,
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var productCodes = result.Items.Select(item => item.ProductCode).ToList();
        Assert.Contains("P-QUICK-ACTIVE", productCodes);
        Assert.Contains("P-QUICK-INACTIVE-PRODUCT", productCodes);
        Assert.Contains("P-QUICK-INACTIVE-WAREHOUSE", productCodes);
        Assert.DoesNotContain("P-QUICK-DELETED-PRODUCT", productCodes);
        Assert.DoesNotContain("P-QUICK-DELETED-WAREHOUSE", productCodes);
    }

    [Fact]
    public async Task GetPagedListAsync_IncludeInactiveFlagDoesNotBypassStatusOutsideQuickAddShape()
    {
        await SeedProductAsync(
            "P-ABUSE-ACTIVE",
            "ABUSE-ACTIVE",
            productName: "Inactive Search Active"
        );
        await SeedWarehouseProductAsync("P-ABUSE-ACTIVE");
        await SeedProductAsync(
            "P-ABUSE-INACTIVE",
            "ABUSE-INACTIVE",
            isActive: false,
            productName: "Inactive Search Hidden"
        );
        await SeedWarehouseProductAsync("P-ABUSE-INACTIVE");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ItemNumber = "ABUSE",
            ProductName = "Inactive Search",
            IncludeInactiveWarehouseProducts = true,
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var productCodes = result.Items.Select(item => item.ProductCode).ToList();
        Assert.Contains("P-ABUSE-ACTIVE", productCodes);
        Assert.DoesNotContain("P-ABUSE-INACTIVE", productCodes);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerIncludesInactiveWarehouseProductsAndFiltersDomesticSupplier()
    {
        await SeedChinaSupplierAsync("CN001", "义乌一号");
        await SeedChinaSupplierAsync("CN002", "义乌二号");
        await SeedProductAsync("P-INACTIVE", "ITEM-INACTIVE");
        await SeedWarehouseProductAsync("P-INACTIVE", isActive: false, oemPrice: 11m, importPrice: 8m);
        await SeedDomesticProductAsync("P-INACTIVE", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN001");
        await SeedProductAsync("P-OTHER-SUPPLIER", "ITEM-OTHER-SUPPLIER");
        await SeedWarehouseProductAsync("P-OTHER-SUPPLIER");
        await SeedDomesticProductAsync("P-OTHER-SUPPLIER", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN002");
        await SeedProductAsync("P-IN-ORDER", "ITEM-IN-ORDER");
        await SeedWarehouseProductAsync("P-IN-ORDER");
        await SeedDomesticProductAsync("P-IN-ORDER", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN001");
        await SeedDeletedOrderDetailAsync("ORDER-PICKER", "P-IN-ORDER", isDeleted: false);
        await SeedProductAsync("P-DELETED-WAREHOUSE", "ITEM-DELETED-WAREHOUSE");
        await SeedWarehouseProductAsync("P-DELETED-WAREHOUSE", isDeleted: true);
        await SeedDomesticProductAsync("P-DELETED-WAREHOUSE", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN001");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            SupplierCode = "CN001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P-INACTIVE", item.ProductCode);
        Assert.Equal("CN001", item.DomesticSupplierCode);
        Assert.Equal("义乌一号", item.DomesticSupplierName);
        Assert.Equal(11m, item.OEMPrice);
        Assert.Equal(8m, item.ImportPrice);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerResolvesDomesticSupplierByItemNumberWhenProductCodesDiffer()
    {
        await SeedChinaSupplierAsync("CN001", "义乌一号");
        await SeedChinaSupplierAsync("CN002", "义乌二号");
        await SeedProductAsync("P-HB008", "HB008-01", barcode: "9528500822001");
        await SeedWarehouseProductAsync("P-HB008");
        await SeedDomesticProductAsync(
            "DP-HB008-01",
            unitVolume: 0.1m,
            packingQuantity: 12,
            supplierCode: "CN001",
            hbProductNo: "HB008-01",
            barcode: "9528500822001"
        );

        var included = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            SupplierCode = "CN001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(included.Items);
        Assert.Equal("P-HB008", item.ProductCode);
        Assert.Equal("CN001", item.DomesticSupplierCode);
        Assert.Equal("义乌一号", item.DomesticSupplierName);

        var excluded = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            SupplierCode = "CN002",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        Assert.Empty(excluded.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerUnifiedKeywordMatchesProductName()
    {
        await SeedProductAsync("P-NAME", "TABLE-01", productName: "Kids Chair + Table");
        await SeedWarehouseProductAsync("P-NAME");
        await SeedProductAsync("P-ITEM", "CHAIR-ITEM", productName: "Unrelated Product");
        await SeedWarehouseProductAsync("P-ITEM");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            ItemNumber = "Kids",
            ProductName = "Kids",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P-NAME", item.ProductCode);
        Assert.Equal("Kids Chair + Table", item.ProductName);
    }

    [Fact]
    public async Task ScanLookupProductsAsync_UsesSingleFieldLookupWhenBarcodeMatches()
    {
        await SeedProductAsync("P-BAR", "ITEM-BAR", barcode: "SCAN-001");
        await SeedWarehouseProductAsync("P-BAR");
        await SeedProductAsync("P-ITEM", "SCAN-001", barcode: "OTHER-BAR");
        await SeedWarehouseProductAsync("P-ITEM");
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupProductsAsync(new StoreOrderScanLookupRequestDto
        {
            Barcode = "SCAN-001",
        });

        Assert.True(result.Success, result.Message);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("P-BAR", item.ProductCode);
        Assert.Equal("barcode", result.Data.MatchType);
        Assert.DoesNotContain(
            _sqlLogs,
            sql => sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ScanLookupProductsAsync_FallsBackToItemNumberAfterBarcodeMiss()
    {
        await SeedProductAsync("P-ITEM", "SCAN-002", barcode: "OTHER-BAR");
        await SeedWarehouseProductAsync("P-ITEM");
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupProductsAsync(new StoreOrderScanLookupRequestDto
        {
            Barcode = "SCAN-002",
        });

        Assert.True(result.Success, result.Message);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("P-ITEM", item.ProductCode);
        Assert.Equal("fallback", result.Data.MatchType);
        Assert.Equal(2, _sqlLogs.Count);
        Assert.DoesNotContain(
            _sqlLogs,
            sql => sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ScanLookupProductsAsync_FallsBackToProductCodeAfterBarcodeAndItemNumberMiss()
    {
        await SeedProductAsync("SCAN-003", "ITEM-CODE", barcode: "OTHER-BAR");
        await SeedWarehouseProductAsync("SCAN-003");
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupProductsAsync(new StoreOrderScanLookupRequestDto
        {
            Barcode = "SCAN-003",
        });

        Assert.True(result.Success, result.Message);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("SCAN-003", item.ProductCode);
        Assert.Equal("fallback", result.Data.MatchType);
        Assert.Equal(3, _sqlLogs.Count);
        Assert.DoesNotContain(
            _sqlLogs,
            sql => sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task BatchLookupProductsAsync_MatchesInactiveProductAndWarehouseRowsButKeepsDeletedRowsHidden()
    {
        await SeedProductAsync("P-INACTIVE-PRODUCT", "ITEM-INACTIVE-PRODUCT", isActive: false);
        await SeedWarehouseProductAsync("P-INACTIVE-PRODUCT");
        await SeedProductAsync("P-INACTIVE-WAREHOUSE", "ITEM-INACTIVE-WAREHOUSE");
        await SeedWarehouseProductAsync("P-INACTIVE-WAREHOUSE", isActive: false);
        await SeedProductAsync("P-DELETED-PRODUCT", "ITEM-DELETED-PRODUCT", isDeleted: true);
        await SeedWarehouseProductAsync("P-DELETED-PRODUCT");
        await SeedProductAsync("P-DELETED-WAREHOUSE", "ITEM-DELETED-WAREHOUSE");
        await SeedWarehouseProductAsync("P-DELETED-WAREHOUSE", isDeleted: true);

        var result = await CreateService().BatchLookupProductsAsync(new StoreOrderBatchLookupRequestDto
        {
            Codes = new List<string>
            {
                "ITEM-INACTIVE-PRODUCT",
                "ITEM-INACTIVE-WAREHOUSE",
                "ITEM-DELETED-PRODUCT",
                "ITEM-DELETED-WAREHOUSE",
            },
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(
            "P-INACTIVE-PRODUCT",
            result.Data.Single(item => item.LookupCode == "ITEM-INACTIVE-PRODUCT").Product?.ProductCode
        );
        Assert.Equal(
            "P-INACTIVE-WAREHOUSE",
            result.Data.Single(item => item.LookupCode == "ITEM-INACTIVE-WAREHOUSE").Product?.ProductCode
        );
        Assert.Null(result.Data.Single(item => item.LookupCode == "ITEM-DELETED-PRODUCT").Product);
        Assert.Null(result.Data.Single(item => item.LookupCode == "ITEM-DELETED-WAREHOUSE").Product);
    }

    [Fact]
    public async Task PasteReplaceOrderLinesAsync_HandlesReplaceAppendSkipAndInvalidQuantities()
    {
        await SeedStoreOrderAsync("ORDER-PASTE-ACTION");
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-REPLACE", "ITEM-REPLACE", quantity: 5m, allocQuantity: 2m);
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-APPEND", "ITEM-APPEND", quantity: 4m, allocQuantity: 6m);
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-SKIP", "ITEM-SKIP", quantity: 3m, allocQuantity: 7m);
        await SeedProductAsync("P-NEW-ZERO", "ITEM-NEW-ZERO");
        await SeedWarehouseProductAsync("P-NEW-ZERO");
        await SeedProductAsync("P-NEW-VALID", "ITEM-NEW-VALID");
        await SeedWarehouseProductAsync("P-NEW-VALID");

        var result = await CreateService().PasteReplaceOrderLinesAsync(new PasteReplaceOrderLinesDto
        {
            OrderGUID = "ORDER-PASTE-ACTION",
            TargetField = StoreOrderPasteTargetFields.AllocQuantity,
            Items = new List<ProductQuantityDto>
            {
                new() { ProductCode = "P-REPLACE", Quantity = 10m, Action = "replace" },
                new() { ProductCode = "P-APPEND", Quantity = 5m, Action = "append" },
                new() { ProductCode = "P-SKIP", Quantity = 99m, Action = "skip" },
                new() { ProductCode = "P-NEW-ZERO", Quantity = 0m, Action = "replace" },
                new() { ProductCode = "P-NEW-VALID", Quantity = 8m, Action = "append" },
            },
        });

        Assert.True(result.Success, result.Message);

        var rows = await _db.Queryable<WareHouseOrderDetails>()
            .Where(row => row.OrderGUID == "ORDER-PASTE-ACTION")
            .ToListAsync();

        Assert.Equal(10m, rows.Single(row => row.ProductCode == "P-REPLACE").AllocQuantity);
        Assert.Equal(11m, rows.Single(row => row.ProductCode == "P-APPEND").AllocQuantity);
        Assert.Equal(7m, rows.Single(row => row.ProductCode == "P-SKIP").AllocQuantity);
        Assert.DoesNotContain(rows, row => row.ProductCode == "P-NEW-ZERO");
        Assert.Equal(8m, rows.Single(row => row.ProductCode == "P-NEW-VALID").AllocQuantity);
    }

    [Fact]
    public async Task PasteReplaceOrderLinesAsync_ReturnsFailureForUnknownExplicitAction()
    {
        await SeedStoreOrderAsync("ORDER-PASTE-UNKNOWN");
        await SeedOrderLineAsync("ORDER-PASTE-UNKNOWN", "P-UNKNOWN", "ITEM-UNKNOWN", quantity: 4m, allocQuantity: 6m);

        var result = await CreateService().PasteReplaceOrderLinesAsync(new PasteReplaceOrderLinesDto
        {
            OrderGUID = "ORDER-PASTE-UNKNOWN",
            TargetField = StoreOrderPasteTargetFields.AllocQuantity,
            Items = new List<ProductQuantityDto>
            {
                new() { ProductCode = "P-UNKNOWN", Quantity = 99m, Action = "mystery" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("Unsupported paste action", result.Message);

        var row = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-PASTE-UNKNOWN" && item.ProductCode == "P-UNKNOWN")
            .FirstAsync();
        Assert.Equal(6m, row.AllocQuantity);
    }

    [Fact]
    public async Task 首页预热轻量查询_不执行Count且只取首屏商品()
    {
        for (var index = 1; index <= 25; index++)
        {
            var itemNumber = $"ITEM-{index:D3}";
            var productCode = $"P{index:D3}";
            await SeedProductAsync(productCode, itemNumber);
            await SeedWarehouseProductAsync(productCode, oemPrice: index, importPrice: index / 2m);
        }

        var service = CreateService();
        var warmUpMethod = typeof(StoreOrderReactService).GetMethod(
            "GetHomePageWarmUpPageAsync",
            BindingFlags.Instance | BindingFlags.Public
        );

        Assert.NotNull(warmUpMethod);

        _sqlLogs.Clear();
        var task =
            (Task<PagedListReactDto<StoreOrderProductDto>>)warmUpMethod!.Invoke(
                service,
                new object[] { 18, CancellationToken.None }
            )!;
        var result = await task;

        Assert.Equal(18, result.Items.Count);
        Assert.Equal(18, result.PageSize);
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task AddToCartAsync_加购后应使用聚合重算主表金额并返回最新购物车()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedWarehouseProductAsync("P001", oemPrice: 3m, importPrice: 2m);
        await SeedDomesticProductAsync("P001", unitVolume: 0.12m, packingQuantity: 12);

        var service = CreateService();

        _sqlLogs.Clear();
        var result = await service.AddToCartAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 5,
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(5, result.Data.TotalQuantity);
        Assert.Equal(15m, result.Data.TotalAmount);
        Assert.Equal(10m, result.Data.TotalImportAmount);
        Assert.Equal(1, result.Data.TotalSKU);
        Assert.Contains(_sqlLogs, log => log.Contains("SUM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProductsDynamicDataAsync_返回购物车数量和每个商品最近历史订单()
    {
        await SeedStoreOrderAsync("ORDER-OLD", flowStatus: 1, orderDate: new DateTime(2026, 5, 1));
        await SeedStoreOrderAsync("ORDER-NEW", flowStatus: 1, orderDate: new DateTime(2026, 6, 1), insertStore: false);
        await SeedStoreOrderAsync("ORDER-CART", flowStatus: 0, orderDate: new DateTime(2026, 6, 2), insertStore: false);
        await SeedOrderDetailOnlyAsync("ORDER-OLD", "P001", quantity: 2m, allocQuantity: 1m);
        await SeedOrderDetailOnlyAsync("ORDER-NEW", "P001", quantity: 7m, allocQuantity: 3m);
        await SeedOrderDetailOnlyAsync("ORDER-CART", "P001", quantity: 5m, allocQuantity: 0m);

        var result = await CreateService().GetProductsDynamicDataAsync(new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "S001",
            ProductCodes = new List<string> { "P001", "P002" },
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Collection(
            result.Data,
            first =>
            {
                Assert.Equal("P001", first.ProductCode);
                Assert.Equal(5m, first.CartQuantity);
                Assert.Equal(new DateTime(2026, 6, 1), first.LastOrderDate);
                Assert.Equal(7m, first.LastQuantity);
                Assert.Equal(3m, first.LastAllocQuantity);
            },
            second =>
            {
                Assert.Equal("P002", second.ProductCode);
                Assert.Equal(0m, second.CartQuantity);
                Assert.Null(second.LastOrderDate);
            }
        );
    }

    [Fact]
    public async Task GetProductsDynamicDataAsync_动态数据查询应在数据库侧聚合购物车和最近订单()
    {
        await SeedStoreOrderAsync(
            "ORDER-CART-1",
            flowStatus: 0,
            orderDate: new DateTime(2026, 6, 1)
        );
        await SeedStoreOrderAsync(
            "ORDER-CART-2",
            flowStatus: 0,
            orderDate: new DateTime(2026, 6, 2),
            insertStore: false
        );
        await SeedOrderDetailOnlyAsync("ORDER-CART-1", "P001", quantity: 2m, allocQuantity: 0m);
        await SeedOrderDetailOnlyAsync("ORDER-CART-2", "P001", quantity: 3m, allocQuantity: 0m);

        for (var index = 1; index <= 20; index++)
        {
            var orderGuid = $"ORDER-HISTORY-{index:D2}";
            await SeedStoreOrderAsync(
                orderGuid,
                flowStatus: 1,
                orderDate: new DateTime(2026, 5, index),
                insertStore: false
            );
            await SeedOrderDetailOnlyAsync(orderGuid, "P001", quantity: index, allocQuantity: index - 1);
        }

        _sqlLogs.Clear();

        var result = await CreateService().GetProductsDynamicDataAsync(new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "S001",
            ProductCodes = new List<string> { "P001" },
        });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!);
        Assert.Equal(5m, item.CartQuantity);
        Assert.Equal(new DateTime(2026, 5, 20), item.LastOrderDate);
        Assert.Equal(20m, item.LastQuantity);
        Assert.Equal(19m, item.LastAllocQuantity);
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("SUM", StringComparison.OrdinalIgnoreCase)
                && log.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("MAX", StringComparison.OrdinalIgnoreCase)
                && log.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetProductsDynamicDataAsync_最近订单跨度大时不应回查宽日期窗口()
    {
        await SeedStoreOrderAsync(
            "ORDER-P001-OLD",
            flowStatus: 1,
            orderDate: new DateTime(2025, 1, 1)
        );
        await SeedOrderDetailOnlyAsync("ORDER-P001-OLD", "P001", quantity: 1m, allocQuantity: 1m);

        for (var index = 1; index <= 30; index++)
        {
            var orderGuid = $"ORDER-P002-{index:D2}";
            await SeedStoreOrderAsync(
                orderGuid,
                flowStatus: 1,
                orderDate: new DateTime(2026, 5, index),
                insertStore: false
            );
            await SeedOrderDetailOnlyAsync(orderGuid, "P002", quantity: index, allocQuantity: index);
        }

        _sqlLogs.Clear();

        var result = await CreateService().GetProductsDynamicDataAsync(new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "S001",
            ProductCodes = new List<string> { "P001", "P002" },
        });

        Assert.True(result.Success);
        Assert.Collection(
            result.Data!,
            first => Assert.Equal(new DateTime(2025, 1, 1), first.LastOrderDate),
            second =>
            {
                Assert.Equal("P002", second.ProductCode);
                Assert.Equal(new DateTime(2026, 5, 30), second.LastOrderDate);
                Assert.Equal(30m, second.LastQuantity);
            }
        );
        Assert.DoesNotContain(
            _sqlLogs,
            log =>
                log.Contains(">=", StringComparison.Ordinal)
                && log.Contains("OrderDate", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeOrderGUID_AlsoAppliesToDefaultWarehouseQuery()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedProductAsync("P002", "ITEM-002");
        await SeedWarehouseProductAsync("P001");
        await SeedWarehouseProductAsync("P002");
        await SeedDeletedOrderDetailAsync("ORDER-001", "P001", isDeleted: false);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P002", item.ProductCode);
    }

    [Fact]
    public async Task GetOrderDetailAsync_ReturnsRequestedPageAndKeepsWholeOrderTotals()
    {
        await SeedStoreOrderAsync("ORDER-001");
        await SeedOrderLineAsync("ORDER-001", "P001", "ITEM-001", quantity: 10m, allocQuantity: 4m);
        await SeedOrderLineAsync("ORDER-001", "P002", "ITEM-002", quantity: 20m, allocQuantity: 8m);
        await SeedOrderLineAsync("ORDER-001", "P003", "ITEM-003", quantity: 30m, allocQuantity: 12m);

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-001",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 2,
                SortBy = "itemNumber",
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.Equal(3, result.Data.ItemsTotal);
        Assert.Equal(1, result.Data.PageNumber);
        Assert.Equal(2, result.Data.PageSize);
        Assert.Equal(60, result.Data.TotalQuantity);
        Assert.Equal(24, result.Data.TotalAllocQuantity);
        Assert.Equal(3, result.Data.TotalSKU);
        Assert.Equal(new[] { "ITEM-001", "ITEM-002" }, result.Data.Items.Select(item => item.ItemNumber));
    }

    [Fact]
    public async Task GetOrderDetailAsync_DeduplicatesLocationCodesForCurrentPage()
    {
        await SeedStoreOrderAsync("ORDER-002");
        await SeedOrderLineAsync("ORDER-002", "P001", "ITEM-001", quantity: 1m, allocQuantity: 1m);
        await SeedLocationAsync("P001", "L-A", "A-01");
        await SeedLocationAsync("P001", "L-B", "B-01");
        await SeedLocationAsync("P001", "L-A-DUP", "A-01");

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-002",
            new StoreOrderDetailQueryDto { PageNumber = 1, PageSize = 50 }
        );

        Assert.NotNull(result.Data);
        var item = Assert.Single(result.Data.Items);
        Assert.Equal("A-01, B-01", item.LocationCode);
    }

    [Fact]
    public async Task GetOrderDetailAsync_UsesDatabasePaging_AndSupportsInactiveStatFilter()
    {
        await SeedStoreOrderAsync("ORDER-003");
        await SeedOrderLineAsync("ORDER-003", "P001", "ITEM-001", quantity: 1m, allocQuantity: 1m);
        await SeedOrderLineAsync(
            "ORDER-003",
            "P002",
            "ITEM-002",
            quantity: 2m,
            allocQuantity: 1m,
            isActive: true,
            warehouseIsActive: false
        );
        await SeedOrderLineAsync("ORDER-003", "P003", "ITEM-003", quantity: 3m, allocQuantity: 1m);

        _sqlLogs.Clear();
        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-003",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 2,
                PageSize = 1,
                SortBy = "productCode",
                SortDescending = false,
            }
        );

        Assert.NotNull(result.Data);
        Assert.Equal("P002", Assert.Single(result.Data.Items).ProductCode);
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                && !log.Contains("COUNT(1)", StringComparison.OrdinalIgnoreCase)
        );

        var inactiveResult = await CreateService().GetOrderDetailAsync(
            "ORDER-003",
            new StoreOrderDetailQueryDto
            {
                StatFilter = "inactive",
                PageNumber = 1,
                PageSize = 50,
            }
        );

        Assert.NotNull(inactiveResult.Data);
        var inactiveItem = Assert.Single(inactiveResult.Data.Items);
        Assert.Equal("P002", inactiveItem.ProductCode);
        Assert.False(inactiveItem.IsActive);

        var activeResult = await CreateService().GetOrderDetailAsync(
            "ORDER-003",
            new StoreOrderDetailQueryDto
            {
                StatFilter = "active",
                PageNumber = 1,
                PageSize = 50,
            }
        );

        Assert.NotNull(activeResult.Data);
        Assert.DoesNotContain(activeResult.Data.Items, item => item.ProductCode == "P002");
    }

    [Fact]
    public async Task GetOrderListAsync_ReturnsOutboundDate()
    {
        var outboundDate = new DateTime(2026, 6, 7);
        await SeedStoreOrderAsync("ORDER-OUT-LIST", outboundDate: outboundDate);
        await SeedOrderLineAsync("ORDER-OUT-LIST", "P001", "ITEM-001", quantity: 3m, allocQuantity: 2m);

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(outboundDate, item.OutboundDate);
    }

    [Fact]
    public async Task GetOrderListAsync_FiltersByDetailItemNumber()
    {
        await SeedStoreOrderAsync("ORDER-MATCH", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync("ORDER-MATCH", "P-MATCH", "HB-ITEM-889", quantity: 3m, allocQuantity: 2m);
        await SeedStoreOrderAsync("ORDER-OTHER", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync("ORDER-OTHER", "P-OTHER", "HB-ITEM-100", quantity: 3m, allocQuantity: 2m);

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            Keyword = "ITEM-889",
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("ORDER-MATCH", item.OrderGUID);
    }

    [Fact]
    public async Task GetOrderListAsync_DoesNotDuplicateOrdersWhenMultipleDetailsMatchItemNumber()
    {
        await SeedStoreOrderAsync("ORDER-DUP", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync("ORDER-DUP", "P-DUP-1", "DUP-ITEM", quantity: 1m, allocQuantity: 1m);
        await SeedOrderLineAsync("ORDER-DUP", "P-DUP-2", "DUP-ITEM", quantity: 2m, allocQuantity: 2m);

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            Keyword = "DUP-ITEM",
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        Assert.Equal(1, result.Total);
        Assert.Equal("ORDER-DUP", Assert.Single(result.Items).OrderGUID);
    }

    [Fact]
    public async Task GetOrderListAsync_IgnoresDeletedDetailAndDeletedProductWhenFilteringByItemNumber()
    {
        await SeedStoreOrderAsync("ORDER-DELETED-DETAIL", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync(
            "ORDER-DELETED-DETAIL",
            "P-DELETED-DETAIL",
            "REMOVED-ITEM",
            quantity: 1m,
            allocQuantity: 1m,
            isDeleted: true
        );
        await SeedStoreOrderAsync("ORDER-DELETED-PRODUCT", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-DELETED-PRODUCT",
            "P-DELETED-PRODUCT",
            "REMOVED-ITEM",
            quantity: 1m,
            allocQuantity: 1m,
            productIsDeleted: true
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            Keyword = "REMOVED-ITEM",
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetUsedBranchesAsync_SortsByStoreNameThenCode()
    {
        await SeedStoreAsync("store-z", "S010", "Zulu");
        await SeedStoreAsync("store-a2", "S002", "Alpha");
        await SeedStoreAsync("store-a1", "S001", "Alpha");
        await SeedStoreOrderAsync("ORDER-Z", storeCode: "S010", insertStore: false);
        await SeedStoreOrderAsync("ORDER-A2", storeCode: "S002", insertStore: false);
        await SeedStoreOrderAsync("ORDER-A1", storeCode: "S001", insertStore: false);

        var result = await CreateService().GetUsedBranchesAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(new[] { "S001", "S002", "S010" }, result.Data.Select(item => item.Code));
    }

    [Fact]
    public async Task GetUnmatchedStoreOrderGroupsAsync_GroupsOnlyOrdersWithoutLocalStoreMatch()
    {
        const string hqGuid = "11111111-1111-1111-1111-111111111111";
        await SeedStoreAsync("local-store-guid", "S001", "本地一店");
        await SeedStoreAsync("local-store-guid-2", "S002", "本地二店");
        await SeedStoreOrderAsync("ORDER-CODE", storeCode: "S001", insertStore: false);
        await SeedStoreOrderAsync("ORDER-GUID", storeCode: "local-store-guid-2", insertStore: false);
        await SeedStoreOrderAsync("ORDER-HQ-1", storeCode: hqGuid, insertStore: false, orderDate: new DateTime(2026, 6, 10));
        await SeedStoreOrderAsync("ORDER-HQ-2", storeCode: hqGuid, insertStore: false, orderDate: new DateTime(2026, 6, 12));
        await SeedStoreOrderAsync("ORDER-UNKNOWN", storeCode: "UNKNOWN-GUID", insertStore: false, orderDate: new DateTime(2026, 6, 11));
        await SeedExternalCustomerAsync(hqGuid, "Ada - Tas - Kingston");

        var result = await CreateService().GetUnmatchedStoreOrderGroupsAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(new[] { hqGuid, "UNKNOWN-GUID" }, result.Data.Select(item => item.SourceStoreCode));
        var hqGroup = Assert.Single(result.Data, item => item.SourceStoreCode == hqGuid);
        Assert.Equal("Ada - Tas - Kingston", hqGroup.SourceStoreName);
        Assert.Equal(2, hqGroup.OrderCount);
        Assert.Equal(new DateTime(2026, 6, 12), hqGroup.LatestOrderDate);
    }

    [Fact]
    public async Task BatchMapStoreOrderStoreCodeAsync_UpdatesOnlyUnmatchedOrderHeaders()
    {
        const string hqGuid = "22222222-2222-2222-2222-222222222222";
        await SeedStoreAsync("target-store-guid", "S100", "目标分店");
        await SeedStoreAsync("matched-store-guid", "S200", "已匹配分店");
        await SeedStoreOrderAsync("ORDER-HQ-1", storeCode: hqGuid, insertStore: false);
        await SeedStoreOrderAsync("ORDER-HQ-2", storeCode: hqGuid, insertStore: false);
        await SeedStoreOrderAsync("ORDER-MATCHED", storeCode: "S200", insertStore: false);
        await SeedDeletedOrderDetailAsync("ORDER-HQ-1", "P-DETAIL", storeCode: hqGuid, isDeleted: false);

        var result = await CreateService().BatchMapStoreOrderStoreCodeAsync(
            new BatchMapStoreOrderStoreCodeDto
            {
                Mappings = new List<StoreOrderStoreCodeMappingDto>
                {
                    new() { SourceStoreCode = hqGuid, TargetStoreCode = "S100" },
                    new() { SourceStoreCode = "S200", TargetStoreCode = "S100" },
                },
            }
        );

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.UpdatedCount);
        Assert.Equal(1, result.Data.SkippedCount);

        var fixedOrders = await _db.Queryable<WareHouseOrder>()
            .Where(item => new[] { "ORDER-HQ-1", "ORDER-HQ-2" }.Contains(item.OrderGUID))
            .ToListAsync();
        Assert.All(fixedOrders, item => Assert.Equal("S100", item.StoreCode));

        var matchedOrder = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-MATCHED")
            .FirstAsync();
        Assert.Equal("S200", matchedOrder.StoreCode);

        var untouchedDetail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-HQ-1")
            .FirstAsync();
        Assert.Equal(hqGuid, untouchedDetail.StoreCode);
    }

    [Fact]
    public async Task BatchMapStoreOrderStoreCodeAsync_FailsWhenTargetStoreMissing()
    {
        const string hqGuid = "33333333-3333-3333-3333-333333333333";
        await SeedStoreOrderAsync("ORDER-HQ", storeCode: hqGuid, insertStore: false);

        var result = await CreateService().BatchMapStoreOrderStoreCodeAsync(
            new BatchMapStoreOrderStoreCodeDto
            {
                Mappings = new List<StoreOrderStoreCodeMappingDto>
                {
                    new() { SourceStoreCode = hqGuid, TargetStoreCode = "MISSING" },
                },
            }
        );

        Assert.False(result.Success);
        Assert.Contains("目标分店不存在", result.Message);

        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-HQ")
            .FirstAsync();
        Assert.Equal(hqGuid, order.StoreCode);
    }

    [Fact]
    public async Task BatchMapStoreOrderStoreCodeAsync_AllowsInactiveTargetStoreForHistoricalRepair()
    {
        const string hqGuid = "44444444-4444-4444-4444-444444444444";
        await SeedStoreAsync("inactive-target-store-guid", "S300", "停用目标分店", isActive: false);
        await SeedStoreOrderAsync("ORDER-INACTIVE-TARGET", storeCode: hqGuid, insertStore: false);

        var result = await CreateService().BatchMapStoreOrderStoreCodeAsync(
            new BatchMapStoreOrderStoreCodeDto
            {
                Mappings = new List<StoreOrderStoreCodeMappingDto>
                {
                    new() { SourceStoreCode = hqGuid, TargetStoreCode = "S300" },
                },
            }
        );

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.UpdatedCount);
        Assert.Equal(0, result.Data.SkippedCount);

        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-INACTIVE-TARGET")
            .FirstAsync();
        Assert.Equal("S300", order.StoreCode);
    }

    [Theory]
    [InlineData("totalOrderAmount", true, "ORDER-HIGH", "ORDER-MID")]
    [InlineData("totalOrderAmount", false, "ORDER-LOW", "ORDER-MID")]
    [InlineData("totalQuantity", true, "ORDER-HIGH", "ORDER-MID")]
    [InlineData("totalQuantity", false, "ORDER-LOW", "ORDER-MID")]
    [InlineData("totalAllocQuantity", true, "ORDER-HIGH", "ORDER-MID")]
    [InlineData("totalAllocQuantity", false, "ORDER-LOW", "ORDER-MID")]
    public async Task GetOrderListAsync_SortsAggregateFieldsWithoutRawSql(
        string sortBy,
        bool sortDescending,
        string firstOrderGuid,
        string secondOrderGuid
    )
    {
        await SeedStoreOrderAsync("ORDER-LOW", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync("ORDER-LOW", "P-LOW", "ITEM-LOW", quantity: 1m, allocQuantity: 1m);
        await SeedStoreOrderAsync("ORDER-MID", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync("ORDER-MID", "P-MID", "ITEM-MID", quantity: 3m, allocQuantity: 2m);
        await SeedStoreOrderAsync("ORDER-HIGH", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync("ORDER-HIGH", "P-HIGH", "ITEM-HIGH", quantity: 5m, allocQuantity: 4m);

        _sqlLogs.Clear();
        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 2,
            StatusList = new List<int> { 1 },
            SortBy = sortBy,
            SortDescending = sortDescending,
        });

        Assert.Equal(3, result.Total);
        Assert.Collection(
            result.Items,
            first => Assert.Equal(firstOrderGuid, first.OrderGUID),
            second => Assert.Equal(secondOrderGuid, second.OrderGUID)
        );
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("WareHouseOrder.OrderGUID", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetOrderListAsync_AggregateSortIgnoresDeletedDetails()
    {
        await SeedStoreOrderAsync("ORDER-DELETED-TOTAL", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync(
            "ORDER-DELETED-TOTAL",
            "P-DELETED-TOTAL",
            "ITEM-DELETED-TOTAL",
            quantity: 100m,
            allocQuantity: 100m,
            isDeleted: true
        );
        await SeedOrderLineAsync(
            "ORDER-DELETED-TOTAL",
            "P-DELETED-ACTIVE",
            "ITEM-DELETED-ACTIVE",
            quantity: 1m,
            allocQuantity: 1m
        );
        await SeedStoreOrderAsync("ORDER-ACTIVE-TOTAL", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-ACTIVE-TOTAL",
            "P-ACTIVE-TOTAL",
            "ITEM-ACTIVE-TOTAL",
            quantity: 3m,
            allocQuantity: 3m
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 2,
            StatusList = new List<int> { 1 },
            SortBy = "importTotalAmount",
            SortDescending = true,
        });

        Assert.Collection(
            result.Items,
            first =>
            {
                Assert.Equal("ORDER-ACTIVE-TOTAL", first.OrderGUID);
                Assert.Equal(3, first.TotalQuantity);
            },
            second =>
            {
                Assert.Equal("ORDER-DELETED-TOTAL", second.OrderGUID);
                Assert.Equal(1, second.TotalQuantity);
            }
        );
    }

    [Fact]
    public async Task GetOrderListAsync_ChunksAggregateDetailLookupForLargeResultSets()
    {
        await SeedStoreAsync("STORE-GUID-001", "S001", "测试门店");
        await SeedStoreOrdersForAggregateSortAsync(1201);

        _sqlLogs.Clear();
        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 1,
            StatusList = new List<int> { 1 },
            SortBy = "totalQuantity",
            SortDescending = true,
        });

        Assert.Equal(1201, result.Total);
        Assert.Equal("ORDER-1200", Assert.Single(result.Items).OrderGUID);
        Assert.True(
            _sqlLogs.Count(log =>
                log.Contains("FROM `WareHouseOrderDetails`", StringComparison.OrdinalIgnoreCase)
            ) >= 4
        );
    }

    [Fact]
    public async Task GetOrderListAsync_FiltersMainColumnFields()
    {
        var outboundDate = new DateTime(2026, 6, 10);
        var createdAt = new DateTime(2026, 6, 1, 8, 0, 0);
        var updatedAt = new DateTime(2026, 6, 12, 9, 30, 0);
        await SeedStoreOrderAsync(
            "ORDER-COLUMN-MATCH",
            flowStatus: 1,
            outboundDate: outboundDate,
            insertStore: true,
            remarks: "Fragile gift bags",
            createdAt: createdAt,
            updatedBy: "stephanie",
            updatedAt: updatedAt
        );
        await SeedOrderLineAsync(
            "ORDER-COLUMN-MATCH",
            "P-COLUMN-MATCH",
            "ITEM-COLUMN-MATCH",
            quantity: 3m,
            allocQuantity: 2m
        );
        await SeedStoreOrderAsync(
            "ORDER-COLUMN-OTHER",
            flowStatus: 1,
            outboundDate: outboundDate.AddDays(5),
            insertStore: false,
            remarks: "Other note",
            createdAt: createdAt.AddDays(-10),
            updatedBy: "dong",
            updatedAt: updatedAt.AddDays(5)
        );
        await SeedOrderLineAsync(
            "ORDER-COLUMN-OTHER",
            "P-COLUMN-OTHER",
            "ITEM-COLUMN-OTHER",
            quantity: 3m,
            allocQuantity: 2m
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
            ColumnFilters = new StoreOrderListColumnFilterDto
            {
                OrderNo = "MATCH",
                OutboundDateStart = outboundDate,
                OutboundDateEnd = outboundDate,
                Remarks = "gift",
                CreatedAtStart = createdAt.Date,
                CreatedAtEnd = createdAt.Date,
                UpdatedBy = "steph",
                UpdatedAtStart = updatedAt.Date,
                UpdatedAtEnd = updatedAt.Date,
            },
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("ORDER-COLUMN-MATCH", item.OrderGUID);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetOrderListAsync_FiltersAggregateAndVolumeColumnsBeforePaging()
    {
        await SeedStoreOrderAsync("ORDER-LOW-FILTER", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync(
            "ORDER-LOW-FILTER",
            "P-LOW-FILTER",
            "ITEM-LOW-FILTER",
            quantity: 1m,
            allocQuantity: 1m
        );
        await SeedStoreOrderAsync("ORDER-MID-FILTER", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-MID-FILTER",
            "P-MID-FILTER",
            "ITEM-MID-FILTER",
            quantity: 3m,
            allocQuantity: 2m
        );
        await SeedStoreOrderAsync("ORDER-HIGH-FILTER", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-HIGH-FILTER",
            "P-HIGH-FILTER",
            "ITEM-HIGH-FILTER",
            quantity: 5m,
            allocQuantity: 4m
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 1,
            StatusList = new List<int> { 1 },
            SortBy = "totalQuantity",
            SortDescending = true,
            ColumnFilters = new StoreOrderListColumnFilterDto
            {
                TotalQuantityMin = 3m,
                TotalQuantityMax = 5m,
                TotalOrderAmountMin = 6m,
                TotalOrderAmountMax = 10m,
                TotalOrderVolumeMin = 3m,
                TotalOrderVolumeMax = 5m,
                TotalAllocVolumeMin = 2m,
                TotalAllocVolumeMax = 4m,
                TotalAllocQuantityMin = 2m,
                TotalAllocQuantityMax = 4m,
                ImportTotalAmountMin = 4m,
                ImportTotalAmountMax = 8m,
            },
        });

        Assert.Equal(2, result.Total);
        Assert.Equal("ORDER-HIGH-FILTER", Assert.Single(result.Items).OrderGUID);
    }

    [Fact]
    public async Task GetOrderDetailAsync_ReturnsOutboundDate()
    {
        var outboundDate = new DateTime(2026, 6, 8);
        await SeedStoreOrderAsync("ORDER-OUT-DETAIL", outboundDate: outboundDate);
        await SeedOrderLineAsync("ORDER-OUT-DETAIL", "P001", "ITEM-001", quantity: 3m, allocQuantity: 2m);

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-OUT-DETAIL",
            new StoreOrderDetailQueryDto { PageNumber = 1, PageSize = 20 }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(outboundDate, result.Data.OutboundDate);
    }

    [Fact]
    public async Task UpdateOrderOutboundDateAsync_CanCompletePickingOrderAndOnlyUpdatesOutboundFields()
    {
        var originalOrderDate = new DateTime(2026, 6, 1);
        var outboundDate = new DateTime(2026, 6, 9);
        await SeedStoreOrderAsync(
            "ORDER-OUT-UPDATE",
            flowStatus: 3,
            orderDate: originalOrderDate,
            outboundDate: new DateTime(2026, 6, 2)
        );

        var result = await CreateService().UpdateOrderOutboundDateAsync(new UpdateOrderOutboundDateDto
        {
            OrderGuid = "ORDER-OUT-UPDATE",
            OutboundDate = outboundDate,
            CompleteOrder = true,
        });

        Assert.True(result.Success, result.Message);
        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-OUT-UPDATE")
            .FirstAsync();
        Assert.Equal(outboundDate, order.OutboundDate);
        Assert.Equal(2, order.FlowStatus);
        Assert.Equal(originalOrderDate, order.OrderDate);
    }

    [Fact]
    public async Task UpdateOrderOutboundDateAsync_ReturnsFailureWhenOrderDoesNotExist()
    {
        var result = await CreateService().UpdateOrderOutboundDateAsync(new UpdateOrderOutboundDateDto
        {
            OrderGuid = "ORDER-MISSING",
            OutboundDate = new DateTime(2026, 6, 10),
            CompleteOrder = true,
        });

        Assert.False(result.Success);
        Assert.Equal("Order not found", result.Message);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private async Task SeedProductWithGradesAsync(
        string productCode,
        string itemNumber,
        params string[] grades
    )
    {
        await SeedProductAsync(productCode, itemNumber);
        await SeedWarehouseProductAsync(productCode);

        foreach (var grade in grades)
        {
            await _db.Insertable(new ProductGrade
            {
                Id = $"{productCode}-{grade}",
                ProductCode = productCode,
                Grade = grade,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }
    }

    private async Task SeedProductAsync(
        string productCode,
        string itemNumber,
        string? barcode = null,
        string? localSupplierCode = null,
        decimal? purchasePrice = null,
        bool isActive = true,
        bool isDeleted = false,
        string? productName = null
    )
    {
        await _db.Insertable(new Product
        {
            UUID = $"{productCode}-uuid",
            ProductCode = productCode,
            ProductName = productName ?? $"商品 {productCode}",
            ItemNumber = itemNumber,
            Barcode = barcode ?? $"{itemNumber}-BAR",
            LocalSupplierCode = localSupplierCode,
            PurchasePrice = purchasePrice,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedWarehouseProductAsync(
        string productCode,
        bool isDeleted = false,
        decimal oemPrice = 10m,
        decimal importPrice = 7m,
        bool isActive = true
    )
    {
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            OEMPrice = oemPrice,
            ImportPrice = importPrice,
            StockQuantity = 20,
            MinOrderQuantity = 1,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreOrderAsync(
        string orderGuid,
        int flowStatus = 1,
        DateTime? orderDate = null,
        DateTime? outboundDate = null,
        bool insertStore = true,
        string storeCode = "S001",
        string? remarks = null,
        DateTime? createdAt = null,
        string? updatedBy = null,
        DateTime? updatedAt = null
    )
    {
        if (insertStore)
        {
            await SeedStoreAsync("STORE-GUID-001", storeCode, "测试门店");
        }

        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            StoreCode = storeCode,
            OrderNo = $"{orderGuid}-NO",
            OrderDate = orderDate ?? new DateTime(2026, 6, 1),
            OutboundDate = outboundDate,
            FlowStatus = flowStatus,
            Remarks = remarks,
            CreatedAt = createdAt ?? new DateTime(2026, 6, 1),
            UpdatedBy = updatedBy,
            UpdatedAt = updatedAt,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeGuid, string storeCode, string storeName, bool isActive = true)
    {
        await _db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = storeCode,
            StoreName = storeName,
            Address = "测试地址",
            IsActive = isActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreOrdersForAggregateSortAsync(int count)
    {
        var orders = Enumerable.Range(0, count)
            .Select(index => new WareHouseOrder
            {
                OrderGUID = $"ORDER-{index:0000}",
                StoreCode = "S001",
                OrderNo = $"ORDER-{index:0000}-NO",
                OrderDate = new DateTime(2026, 6, 1),
                FlowStatus = 1,
                IsDeleted = false,
            })
            .ToList();

        var details = Enumerable.Range(0, count)
            .Select(index => new WareHouseOrderDetails
            {
                DetailGUID = $"ORDER-{index:0000}-P{index:0000}",
                OrderGUID = $"ORDER-{index:0000}",
                StoreCode = "S001",
                ProductCode = $"P{index:0000}",
                Quantity = index,
                AllocQuantity = index,
                ImportPrice = 2m,
                ImportAmount = index * 2m,
                OEMPrice = 3m,
                OEMAmount = index * 3m,
                IsDeleted = false,
            })
            .ToList();

        await _db.Insertable(orders).ExecuteCommandAsync();
        await _db.Insertable(details).ExecuteCommandAsync();
    }

    private async Task SeedOrderLineAsync(
        string orderGuid,
        string productCode,
        string itemNumber,
        decimal quantity,
        decimal allocQuantity,
        bool isActive = true,
        bool warehouseIsActive = true,
        bool isDeleted = false,
        bool productIsDeleted = false
    )
    {
        await SeedProductAsync(productCode, itemNumber, isActive: isActive, isDeleted: productIsDeleted);
        await SeedWarehouseProductAsync(productCode, importPrice: 2m, isActive: warehouseIsActive);
        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            UnitVolume = 1m,
            PackingQuantity = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-{productCode}",
            OrderGUID = orderGuid,
            StoreCode = "S001",
            ProductCode = productCode,
            Quantity = quantity,
            AllocQuantity = allocQuantity,
            ImportPrice = 2m,
            ImportAmount = allocQuantity * 2m,
            OEMPrice = 3m,
            OEMAmount = quantity * 3m,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedOrderDetailOnlyAsync(
        string orderGuid,
        string productCode,
        decimal quantity,
        decimal allocQuantity
    )
    {
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-{productCode}",
            OrderGUID = orderGuid,
            StoreCode = "S001",
            ProductCode = productCode,
            Quantity = quantity,
            AllocQuantity = allocQuantity,
            ImportPrice = 2m,
            ImportAmount = quantity * 2m,
            OEMPrice = 3m,
            OEMAmount = quantity * 3m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDomesticProductAsync(
        string productCode,
        decimal unitVolume,
        int packingQuantity,
        string? supplierCode = null,
        string? hbProductNo = null,
        string? barcode = null
    )
    {
        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            SupplierCode = supplierCode,
            HBProductNo = hbProductNo,
            Barcode = barcode,
            UnitVolume = unitVolume,
            PackingQuantity = packingQuantity,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedChinaSupplierAsync(string supplierCode, string supplierName)
    {
        await _db.Insertable(new ChinaSupplier
        {
            Guid = $"{supplierCode}-guid",
            SupplierCode = supplierCode,
            SupplierName = supplierName,
            Status = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedLocationAsync(string productCode, string locationGuid, string locationCode)
    {
        await _db.Insertable(new Location
        {
            LocationGuid = locationGuid,
            LocationCode = locationCode,
            LocationType = 1,
            Status = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _db.Insertable(new ProductLocation
        {
            Guid = $"{productCode}-{locationGuid}",
            ProductCode = productCode,
            LocationGuid = locationGuid,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private static string FormatSqlLog(SugarParameter[]? parameters, string sql)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return sql;
        }

        var result = sql;
        foreach (var parameter in parameters.OrderByDescending(item => item.ParameterName.Length))
        {
            var value = parameter.Value?.ToString() ?? "NULL";
            result = result.Replace(parameter.ParameterName, value, StringComparison.Ordinal);
        }

        return result;
    }

    private async Task SeedLocalSupplierAsync(string code, string name)
    {
        await _db.Insertable(new HBLocalSupplier
        {
            Guid = $"{code}-guid",
            LocalSupplierCode = code,
            Name = name,
            Status = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDeletedOrderDetailAsync(
        string orderGuid,
        string productCode,
        bool isDeleted,
        string? storeCode = null
    )
    {
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-{productCode}",
            OrderGUID = orderGuid,
            ProductCode = productCode,
            StoreCode = storeCode,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedExternalCustomerAsync(string hguid, string customerName)
    {
        await _db.Insertable(new CPT_DIC_外购客户信息表
        {
            HGUID = hguid,
            客户名称 = customerName,
            状态 = 1,
        }).ExecuteCommandAsync();
    }

    private StoreOrderReactService CreateService()
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, _db);

        var service = new StoreOrderReactService(
            context,
            NullLogger<StoreOrderReactService>.Instance,
            new HttpContextAccessor(),
            Mock.Of<IOrderNumberGenerator>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            Mock.Of<IInvoiceEmailService>()
        );

        var hqField = typeof(StoreOrderReactService).GetField(
            "_createHqConnection",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        hqField!.SetValue(service, () => _db);

        return service;
    }
}
