using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
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
        await SeedWarehouseProductAsync("P001", isDeleted: false, oemPrice: 10m, importPrice: 7m);

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
            isActive: false
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
        string? localSupplierCode = null,
        decimal? purchasePrice = null,
        bool isActive = true
    )
    {
        await _db.Insertable(new Product
        {
            UUID = $"{productCode}-uuid",
            ProductCode = productCode,
            ProductName = $"商品 {productCode}",
            ItemNumber = itemNumber,
            Barcode = $"{itemNumber}-BAR",
            LocalSupplierCode = localSupplierCode,
            PurchasePrice = purchasePrice,
            IsActive = isActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedWarehouseProductAsync(
        string productCode,
        bool isDeleted = false,
        decimal oemPrice = 10m,
        decimal importPrice = 7m
    )
    {
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            OEMPrice = oemPrice,
            ImportPrice = importPrice,
            StockQuantity = 20,
            MinOrderQuantity = 1,
            IsActive = true,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreOrderAsync(string orderGuid)
    {
        await _db.Insertable(new Store
        {
            StoreGUID = "STORE-GUID-001",
            StoreCode = "S001",
            StoreName = "测试门店",
            Address = "测试地址",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            StoreCode = "S001",
            OrderNo = $"{orderGuid}-NO",
            OrderDate = new DateTime(2026, 6, 1),
            FlowStatus = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedOrderLineAsync(
        string orderGuid,
        string productCode,
        string itemNumber,
        decimal quantity,
        decimal allocQuantity,
        bool isActive = true
    )
    {
        await SeedProductAsync(productCode, itemNumber, isActive: isActive);
        await SeedWarehouseProductAsync(productCode, importPrice: 2m);
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
        bool isDeleted
    )
    {
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-{productCode}",
            OrderGUID = orderGuid,
            ProductCode = productCode,
            IsDeleted = isDeleted,
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

        return new StoreOrderReactService(
            context,
            NullLogger<StoreOrderReactService>.Instance,
            new HttpContextAccessor(),
            Mock.Of<IOrderNumberGenerator>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            Mock.Of<IInvoiceEmailService>()
        );
    }
}
