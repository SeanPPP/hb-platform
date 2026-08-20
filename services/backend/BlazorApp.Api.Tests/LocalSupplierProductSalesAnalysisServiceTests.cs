using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public class LocalSupplierProductSalesAnalysisServiceTests : IDisposable
{
    private static readonly DateTime EndDate = DateTime.UtcNow.AddHours(10).Date.AddDays(-1);
    private static readonly DateTime StartDate = EndDate.AddDays(-30);

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public LocalSupplierProductSalesAnalysisServiceTests()
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
            typeof(Store),
            typeof(UserStore),
            typeof(HBLocalSupplier),
            typeof(Product),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct),
            typeof(ProductSetCode),
            typeof(WarehouseCategory),
            typeof(StoreLocalSupplierInvoice),
            typeof(StoreLocalSupplierInvoiceDetails),
            typeof(ProductStoreDailySalesStatistic)
        );
    }

    [Fact]
    public async Task GetCandidatesAsync_历史候选不受日期限制()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertInvoiceWithDetailAsync(
            "h1",
            "B1",
            "SUP",
            "P1",
            EndDate.AddDays(-120),
            quantity: 5m,
            amount: 10m
        );

        var service = CreateService();
        var result = await service.GetCandidatesAsync(
            CreateRequest(),
            new List<string> { "B1" }
        );

        Assert.True(result.Success);
        Assert.Single(result.Data!.Items);
        Assert.Equal("P1", result.Data.Items.Single().ProductCode);
    }

    [Fact]
    public async Task GetSummaryAsync_授权分店隔离()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertStoreAsync("B2", "Branch Two");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertProductAsync("P2", "ITM2", "BC2", "Product Two");
        await InsertInvoiceWithDetailAsync("h1", "B1", "SUP", "P1", EndDate, 2m, 10m);
        await InsertInvoiceWithDetailAsync("h2", "B2", "SUP", "P2", EndDate, 3m, 20m);

        var service = CreateService();
        var result = await service.GetSummaryAsync(
            CreateRequest(),
            new List<string> { "B1" }
        );

        Assert.True(result.Success);
        var productCodes = result.Data!.Items.Select(row => row.ProductCode);
        Assert.Contains("P1", productCodes);
        Assert.DoesNotContain("P2", productCodes);
    }

    [Fact]
    public async Task GetCandidatesAsync_明细分店覆盖主表时仍严格按授权范围过滤()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertStoreAsync("B2", "Branch Two");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertInvoiceWithDetailAsync(
            "h1",
            "B1",
            "SUP",
            "P1",
            EndDate,
            2m,
            10m,
            detailStoreCode: "B2"
        );

        var result = await CreateService().GetCandidatesAsync(
            CreateRequest(),
            new List<string> { "B1" }
        );

        Assert.Empty(result.Data!.Items);
    }

    [Fact]
    public async Task GetCandidatesAsync_空分店范围返回空()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertInvoiceWithDetailAsync("h1", "B1", "SUP", "P1", EndDate, 2m, 10m);

        var service = CreateService();
        var result = await service.GetCandidatesAsync(
            CreateRequest(),
            new List<string>()
        );

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.Total);
    }

    [Fact]
    public async Task GetCandidatesAsync_全分店与空分店缓存键隔离()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertInvoiceWithDetailAsync("h1", "B1", "SUP", "P1", EndDate, 2m, 10m);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache);
        var allStores = await service.GetCandidatesAsync(CreateRequest(), null);
        var noStores = await service.GetCandidatesAsync(CreateRequest(), new List<string>());

        Assert.Single(allStores.Data!.Items);
        Assert.Empty(noStores.Data!.Items);
    }

    [Fact]
    public async Task GetSummaryAsync_软删除进货与明细被排除()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertProductAsync("P2", "ITM2", "BC2", "Product Two");
        await InsertInvoiceWithDetailAsync(
            "h1",
            "B1",
            "SUP",
            "P1",
            EndDate,
            1m,
            1m,
            headerDeleted: true
        );
        await InsertInvoiceWithDetailAsync("h2", "B1", "SUP", "P2", EndDate, 1m, 1m);

        var service = CreateService();
        var result = await service.GetCandidatesAsync(
            CreateRequest(),
            new List<string> { "B1" }
        );

        var codes = result.Data!.Items.Select(row => row.ProductCode);
        Assert.DoesNotContain("P1", codes);
        Assert.Contains("P2", codes);
    }

    [Fact]
    public async Task GetSummaryAsync_期间零值仍返回且动销率为空()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertInvoiceWithDetailAsync(
            "h1",
            "B1",
            "SUP",
            "P1",
            EndDate.AddDays(-120),
            5m,
            10m
        );

        var service = CreateService();
        var result = await service.GetSummaryAsync(
            CreateRequest(),
            new List<string> { "B1" }
        );

        var row = Assert.Single(result.Data!.Items);
        Assert.Equal("P1", row.ProductCode);
        Assert.Equal(0m, row.PurchaseQuantity);
        Assert.Equal(0m, row.NetSalesQuantity);
        Assert.Null(row.SellThroughRate);
    }

    [Fact]
    public async Task GetSummaryAsync_负销量保留且动销率为百分比()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertInvoiceWithDetailAsync("h1", "B1", "SUP", "P1", EndDate, 10m, 100m);
        await InsertSaleAsync(EndDate, "B1", "SUP", "P1", -2, -20m);

        var service = CreateService();
        var result = await service.GetSummaryAsync(
            CreateRequest(),
            new List<string> { "B1" }
        );

        var row = Assert.Single(result.Data!.Items);
        Assert.Equal(10m, row.PurchaseQuantity);
        Assert.Equal(-2m, row.NetSalesQuantity);
        Assert.Equal(-20m, row.NetSalesAmount);
        Assert.Equal(-20m, row.SellThroughRate);
    }

    [Fact]
    public async Task GetBranchesAsync_按当前商品排分店()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertStoreAsync("B2", "Branch Two");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertProductAsync("P2", "ITM2", "BC2", "Product Two");
        await InsertInvoiceWithDetailAsync("h1", "B1", "SUP", "P1", EndDate, 1m, 1m);
        await InsertInvoiceWithDetailAsync("h2", "B1", "SUP", "P2", EndDate, 1m, 1m);
        await InsertSaleAsync(EndDate, "B1", "SUP", "P1", 2, 20m);
        await InsertSaleAsync(EndDate, "B2", "SUP", "P1", 5, 50m);
        await InsertSaleAsync(EndDate, "B1", "SUP", "P2", 100, 1000m);

        var service = CreateService();
        var request = CreateRequest();
        request.CurrentProductCode = "P1";
        var result = await service.GetBranchesAsync(request, new List<string> { "B1", "B2" });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal(2m, result.Data.Single(row => row.BranchCode == "B1").NetSalesQuantity);
        Assert.Equal(5m, result.Data.Single(row => row.BranchCode == "B2").NetSalesQuantity);
    }

    [Fact]
    public async Task GetProductDailyAsync_越界商品返回空()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await InsertInvoiceWithDetailAsync("h1", "B1", "SUP", "P1", EndDate, 1m, 1m);

        var service = CreateService();
        var request = CreateRequest();
        request.CurrentProductCode = "ZZZ";
        var result = await service.GetProductDailyAsync(request, new List<string> { "B1" });

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetInvoiceDetailsAsync_供应商明细优先回退表头()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP-D", "Detail Supplier");
        await InsertSupplierAsync("SUP-H", "Header Supplier");
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One");
        await _db.Insertable(
            new StoreLocalSupplierInvoice
            {
                InvoiceGUID = "h1",
                StoreCode = "B1",
                SupplierCode = "SUP-H",
                InboundDate = EndDate,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(
            new[]
            {
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "d1",
                    InvoiceGUID = "h1",
                    ProductCode = "P1",
                    SupplierCode = "SUP-D",
                    Quantity = 2m,
                    Amount = 10m,
                    IsDeleted = false,
                },
                new StoreLocalSupplierInvoiceDetails
                {
                    DetailGUID = "d2",
                    InvoiceGUID = "h1",
                    ProductCode = "P1",
                    SupplierCode = null,
                    Quantity = 3m,
                    Amount = 15m,
                    IsDeleted = false,
                },
            }
        ).ExecuteCommandAsync();

        var service = CreateService();
        var request = CreateRequest();
        request.CurrentProductCode = "P1";
        var result = await service.GetInvoiceDetailsAsync(request, new List<string> { "B1" });

        Assert.True(result.Success);
        Assert.Equal(
            "SUP-D",
            result.Data!.Items.Single(row => row.DetailGUID == "d1").SupplierCode
        );
        Assert.Equal(
            "SUP-H",
            result.Data.Items.Single(row => row.DetailGUID == "d2").SupplierCode
        );
    }

    [Fact]
    public async Task GetCandidatesAsync_父分类包含子分类()
    {
        await InsertStoreAsync("B1", "Branch One");
        await InsertSupplierAsync("SUP", "Supplier One");
        await _db.Insertable(
            new[]
            {
                new WarehouseCategory
                {
                    CategoryGUID = "root",
                    ParentGUID = null,
                    CategoryName = "Root",
                    IsDeleted = false,
                    IsActive = true,
                },
                new WarehouseCategory
                {
                    CategoryGUID = "child",
                    ParentGUID = "root",
                    CategoryName = "Child",
                    IsDeleted = false,
                    IsActive = true,
                },
            }
        ).ExecuteCommandAsync();
        await InsertProductAsync("P1", "ITM1", "BC1", "Product One", "child");
        await InsertInvoiceWithDetailAsync("h1", "B1", "SUP", "P1", EndDate, 1m, 1m);

        var service = CreateService();
        var request = CreateRequest();
        request.Filter.CategoryGuid = "root";
        var result = await service.GetCandidatesAsync(request, new List<string> { "B1" });

        Assert.True(result.Success);
        Assert.Contains(result.Data!.Items, row => row.ProductCode == "P1");
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // 忽略临时文件清理失败。
            }
        }
    }

    private LocalSupplierProductSalesAnalysisService CreateService(IMemoryCache? cache = null)
    {
        return new LocalSupplierProductSalesAnalysisService(
            CreateSqlSugarContext(_db),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<LocalSupplierProductSalesAnalysisService>.Instance
        );
    }

    private LocalSupplierProductSalesAnalysisRequest CreateRequest()
    {
        return new LocalSupplierProductSalesAnalysisRequest
        {
            Filter = new LocalSupplierProductSalesAnalysisFilterDto
            {
                StartDate = StartDate,
                EndDate = EndDate,
            },
            Selection = new LocalSupplierProductSalesSelectionDto { Mode = "allFiltered" },
        };
    }

    private async Task InsertStoreAsync(string storeCode, string storeName)
    {
        await _db.Insertable(
            new Store
            {
                StoreGUID = $"guid-{storeCode}",
                StoreCode = storeCode,
                StoreName = storeName,
                IsDeleted = false,
                IsActive = true,
            }
        ).ExecuteCommandAsync();
    }

    private async Task InsertSupplierAsync(string code, string name)
    {
        await _db.Insertable(
            new HBLocalSupplier
            {
                Guid = $"guid-{code}",
                LocalSupplierCode = code,
                Name = name,
                Status = 1,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task InsertProductAsync(
        string productCode,
        string itemNumber,
        string barcode,
        string name,
        string? warehouseCategoryGuid = null
    )
    {
        await _db.Insertable(
            new Product
            {
                UUID = $"uuid-{productCode}",
                ProductCode = productCode,
                ItemNumber = itemNumber,
                Barcode = barcode,
                ProductName = name,
                ProductImage = $"{productCode}.jpg",
                WarehouseCategoryGUID = warehouseCategoryGuid,
                IsDeleted = false,
                IsActive = true,
            }
        ).ExecuteCommandAsync();
    }

    private async Task InsertInvoiceWithDetailAsync(
        string invoiceGuid,
        string storeCode,
        string supplierCode,
        string productCode,
        DateTime inboundDate,
        decimal quantity,
        decimal amount,
        bool headerDeleted = false,
        string? detailStoreCode = null
    )
    {
        await _db.Insertable(
            new StoreLocalSupplierInvoice
            {
                InvoiceGUID = invoiceGuid,
                StoreCode = detailStoreCode ?? storeCode,
                SupplierCode = supplierCode,
                InboundDate = inboundDate,
                IsDeleted = headerDeleted,
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(
            new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = $"detail-{invoiceGuid}",
                InvoiceGUID = invoiceGuid,
                StoreCode = storeCode,
                ProductCode = productCode,
                Quantity = quantity,
                Amount = amount,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task InsertSaleAsync(
        DateTime date,
        string branchCode,
        string supplierCode,
        string productCode,
        int quantity,
        decimal amount
    )
    {
        await _db.Insertable(
            new ProductStoreDailySalesStatistic
            {
                Date = date.Date,
                BranchCode = branchCode,
                SupplierCode = supplierCode,
                ProductCode = productCode,
                TotalQuantity = quantity,
                TotalAmount = amount,
            }
        ).ExecuteCommandAsync();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)
            RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
