using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.ProductInsights;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>以 SQLite 验证查询真实分组、日期范围和历史回显，避免仅测试口径辅助函数。</summary>
public sealed class StoreProductInsightQueryServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public StoreProductInsightQueryServiceTests()
    {
        _connection = new SqliteConnection($"Data Source={_path}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(Store), typeof(Product), typeof(HBLocalSupplier),
            typeof(StoreLocalSupplierInvoice), typeof(StoreLocalSupplierInvoiceDetails), typeof(StoreRetailPrice),
            typeof(WareHouseOrder), typeof(WareHouseOrderDetails),
            typeof(ProductStoreDailySalesStatistic)
        );
    }

    [Fact]
    public async Task 普通商品_本地进货包含未入库和部分入库单且统计末日销售()
    {
        await SeedCommonAsync("L100");
        await SeedInvoiceAsync("old", new DateTime(2025, 1, 4), 2, 1);
        await SeedInvoiceAsync("draft", new DateTime(2026, 9, 14), 99, 0);
        await SeedInvoiceAsync("current-a", new DateTime(2026, 9, 14), 3, 2);
        await SeedInvoiceDetailAsync("current-a", "null-purchase", null);
        await SeedInvoiceAsync("current-b", new DateTime(2026, 9, 14), 4, 1);
        await SeedSaleAsync(new DateTime(2026, 9, 14, 18, 30, 0), 7, 21m);

        var result = await CreateService().GetAsync("S1", "P1", new DateTime(2026, 9, 14), new DateTime(2026, 9, 14));

        Assert.NotNull(result);
        Assert.Equal("local", result!.SourceType);
        Assert.Equal(106m, result.Purchases.Quantity);
        Assert.Equal(3, result.Purchases.DocumentCount);
        Assert.Equal("draft", result.Purchases.LastRecord!.Id);
        Assert.Equal(7, result.Sales.Quantity);
        Assert.Equal(21m, result.Sales.Amount);
        Assert.Equal(new DateTime(2026, 9, 14, 23, 0, 0), result.SalesStatisticLastUpdatedAt);
        Assert.Equal("2026-09-14", result.Range.StartDate);
        Assert.Equal("2026-09-14", result.Range.EndDate);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"startDate\":\"2026-09-14\"", json);
        Assert.Contains("\"range\":{\"startDate\":\"2026-09-14\",\"endDate\":\"2026-09-14\"}", json);
    }

    [Fact]
    public async Task 仓库商品_较早订货在本期出库只计送货且不计未出库配货()
    {
        await SeedCommonAsync("200");
        await SeedWarehouseOrderAsync("older-outbound", new DateTime(2026, 6, 1), new DateTime(2026, 9, 14, 20, 0, 0), 5, 5, 2);
        await SeedWarehouseDetailAsync("older-outbound", "null-warehouse", null, null);
        await SeedWarehouseOrderAsync("pending", new DateTime(2026, 9, 14), null, 9, 9, 3);

        var result = await CreateService().GetAsync("S1", "P1", new DateTime(2026, 9, 14), new DateTime(2026, 9, 14));

        Assert.NotNull(result);
        Assert.Equal("warehouse", result!.SourceType);
        Assert.Equal(9m, result.Warehouse.OrderedQuantity);
        Assert.Equal(5m, result.Warehouse.DeliveredQuantity);
        Assert.Equal("older-outbound", result.Warehouse.LastDelivery!.Id);
        Assert.Single(result.Warehouse.Deliveries);
        Assert.Equal("older-outbound", result.Warehouse.Deliveries[0].Id);
        Assert.Equal(0, result.Sales.Quantity);
        Assert.Null(result.SalesStatisticLastUpdatedAt);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(null, true)]
    [InlineData(2, false)]
    [InlineData(0, false)]
    public async Task 本地进货_按进货单数量统计且未填入库日期使用订单日期(int? inboundStatus, bool hasInboundDate)
    {
        await SeedCommonAsync("L100");
        var purchaseDate = new DateTime(2026, 9, 14, 23, 30, 0);
        await _db.Insertable(new StoreLocalSupplierInvoice
        {
            InvoiceGUID = "local-invoice", StoreCode = "S1", SupplierCode = "L100", InvoiceNo = "DATS-59890",
            InboundStatus = inboundStatus, InboundDate = hasInboundDate ? purchaseDate : null,
            OrderDate = hasInboundDate ? purchaseDate.AddDays(-1) : purchaseDate, IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedInvoiceDetailAsync("local-invoice", "local-detail", 12m);

        var result = await CreateService().GetAsync("S1", "P1", purchaseDate.Date, purchaseDate.Date);

        Assert.NotNull(result);
        Assert.Equal(12m, result!.Purchases.Quantity);
        Assert.Equal(1, result.Purchases.DocumentCount);
        var record = Assert.Single(result.Purchases.Records);
        Assert.Equal("DATS-59890", record.DocumentNo);
        Assert.Equal(purchaseDate, record.Date);
        Assert.Equal(record.Id, result.Purchases.LastRecord!.Id);
    }

    [Fact]
    public async Task 本地进货_订单日期历史回显保留门店删除和结束日期边界()
    {
        await SeedCommonAsync("L100");
        var endDate = new DateTime(2026, 9, 14);
        foreach (var (id, storeCode, orderDate, invoiceDeleted, detailDeleted) in new[]
        {
            ("history", "S1", (DateTime?)new DateTime(2024, 2, 1), false, false),
            ("future", "S1", (DateTime?)endDate.AddDays(1), false, false),
            ("other-store", "S2", (DateTime?)endDate, false, false),
            ("deleted-invoice", "S1", (DateTime?)endDate, true, false),
            ("deleted-detail", "S1", (DateTime?)endDate, false, true),
            ("undated", "S1", (DateTime?)null, false, false),
        })
        {
            await _db.Insertable(new StoreLocalSupplierInvoice
            {
                InvoiceGUID = id, StoreCode = storeCode, SupplierCode = "L100", InvoiceNo = id,
                InboundStatus = 0, OrderDate = orderDate, IsDeleted = invoiceDeleted,
            }).ExecuteCommandAsync();
            await _db.Insertable(new StoreLocalSupplierInvoiceDetails
            {
                DetailGUID = $"detail-{id}", InvoiceGUID = id, ProductCode = "P1", Quantity = 6m, IsDeleted = detailDeleted,
            }).ExecuteCommandAsync();
        }

        var result = await CreateService().GetAsync("S1", "P1", endDate, endDate);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.Purchases.Quantity);
        Assert.Empty(result.Purchases.Records);
        Assert.NotNull(result.Purchases.LastRecord);
        Assert.Equal("history", result.Purchases.LastRecord!.Id);
        Assert.Equal(new DateTime(2024, 2, 1), result.Purchases.LastRecord.Date);
    }

    [Fact]
    public async Task 普通商品_范围内无进货时回显不设更早下限的最近有效记录()
    {
        await SeedCommonAsync("L100");
        await SeedInvoiceAsync("history-2024", new DateTime(2024, 2, 1), 6, 2);

        var result = await CreateService().GetAsync("S1", "P1", new DateTime(2026, 9, 14), new DateTime(2026, 9, 14));

        Assert.NotNull(result);
        Assert.Equal(0m, result!.Purchases.Quantity);
        Assert.Empty(result.Purchases.Records);
        Assert.Equal("history-2024", result.Purchases.LastRecord!.Id);
        Assert.Equal(new DateTime(2024, 2, 1), result.Purchases.LastRecord.Date);
    }

    [Fact]
    public async Task 本地进货_直连商品优先并仅回退同店未软删StoreProductCode映射()
    {
        await SeedCommonAsync("L100");
        await _db.Insertable(new Store { StoreGUID = "store-2", StoreCode = "S2", StoreName = "Store 2", IsActive = true, IsDeleted = false }).ExecuteCommandAsync();
        await _db.Insertable(new Product { UUID = "product-2", ProductCode = "P2", ProductName = "Other", LocalSupplierCode = "L100", IsDeleted = false }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new StoreRetailPrice { UUID = "price-local", StoreCode = "S1", ProductCode = "P1", IsActive = false, IsDeleted = false },
            new StoreRetailPrice { UUID = "price-other", StoreCode = "S1", ProductCode = "P2", IsActive = false, IsDeleted = false },
            new StoreRetailPrice { UUID = "price-cross", StoreCode = "S2", ProductCode = "P1", IsActive = false, IsDeleted = false },
            new StoreRetailPrice { UUID = "price-deleted", StoreCode = "S1", ProductCode = "P1", IsActive = false, IsDeleted = true },
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreLocalSupplierInvoice { InvoiceGUID = "mapped", StoreCode = "S1", SupplierCode = "L100", InvoiceNo = "mapped", InboundDate = new DateTime(2026, 9, 14), InboundStatus = 2, IsDeleted = false }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new StoreLocalSupplierInvoiceDetails { DetailGUID = "direct", InvoiceGUID = "mapped", ProductCode = "P1", StoreProductCode = "price-other", Quantity = 2, IsDeleted = false },
            new StoreLocalSupplierInvoiceDetails { DetailGUID = "fallback-empty", InvoiceGUID = "mapped", ProductCode = "", StoreProductCode = "price-local", Quantity = 3, IsDeleted = false },
            new StoreLocalSupplierInvoiceDetails { DetailGUID = "cross-store", InvoiceGUID = "mapped", ProductCode = null, StoreProductCode = "price-cross", Quantity = 99, IsDeleted = false },
            // 空商品码只能回退到未软删的同店映射，不能将已删除价格中的旧映射计入。
            new StoreLocalSupplierInvoiceDetails { DetailGUID = "fallback-deleted", InvoiceGUID = "mapped", ProductCode = "", StoreProductCode = "price-deleted", Quantity = 99, IsDeleted = false },
            // 商品码完整时不依赖已删除价格映射，仍以明细商品码为准。
            new StoreLocalSupplierInvoiceDetails { DetailGUID = "direct-deleted", InvoiceGUID = "mapped", ProductCode = "P1", StoreProductCode = "price-deleted", Quantity = 2, IsDeleted = false },
        }).ExecuteCommandAsync();

        var result = await CreateService().GetAsync("S1", "P1", new DateTime(2026, 9, 14), new DateTime(2026, 9, 14));

        Assert.NotNull(result);
        Assert.Equal(7m, result!.Purchases.Quantity);
        Assert.Equal(1, result.Purchases.DocumentCount);
        Assert.Equal("mapped", Assert.Single(result.Purchases.Records).Id);
    }

    private StoreProductInsightQueryService CreateService() => new(Context(_db), new ServiceCollection().BuildServiceProvider());

    private async Task SeedCommonAsync(string supplierCode)
    {
        await _db.Insertable(new Store { StoreGUID = "store-1", StoreCode = "S1", StoreName = "Store 1", IsActive = true, IsDeleted = false }).ExecuteCommandAsync();
        await _db.Insertable(new HBLocalSupplier { Guid = "supplier-1", LocalSupplierCode = supplierCode, Name = "Supplier", Status = 1, IsDeleted = false }).ExecuteCommandAsync();
        await _db.Insertable(new Product { UUID = "product-1", ProductCode = "P1", ProductName = "Product", LocalSupplierCode = supplierCode, IsActive = true, IsDeleted = false }).ExecuteCommandAsync();
    }

    private async Task SeedInvoiceAsync(string id, DateTime inboundDate, decimal quantity, int inboundStatus)
    {
        await _db.Insertable(new StoreLocalSupplierInvoice { InvoiceGUID = id, StoreCode = "S1", SupplierCode = "L100", InvoiceNo = id, InboundDate = inboundDate, InboundStatus = inboundStatus, IsDeleted = false }).ExecuteCommandAsync();
        await _db.Insertable(new StoreLocalSupplierInvoiceDetails { DetailGUID = $"detail-{id}", InvoiceGUID = id, ProductCode = "P1", Quantity = quantity, IsDeleted = false }).ExecuteCommandAsync();
    }

    private Task SeedInvoiceDetailAsync(string invoiceGuid, string detailId, decimal? quantity) => _db.Insertable(new StoreLocalSupplierInvoiceDetails
    {
        DetailGUID = detailId, InvoiceGUID = invoiceGuid, ProductCode = "P1", Quantity = quantity, IsDeleted = false,
    }).ExecuteCommandAsync();

    private async Task SeedWarehouseOrderAsync(string id, DateTime orderDate, DateTime? outboundDate, decimal quantity, decimal allocatedQuantity, int status)
    {
        await _db.Insertable(new WareHouseOrder { OrderGUID = id, StoreCode = "S1", OrderNo = id, OrderDate = orderDate, OutboundDate = outboundDate, FlowStatus = status, IsDeleted = false }).ExecuteCommandAsync();
        await _db.Insertable(new WareHouseOrderDetails { DetailGUID = $"detail-{id}", OrderGUID = id, ProductCode = "P1", Quantity = quantity, AllocQuantity = allocatedQuantity, IsDeleted = false }).ExecuteCommandAsync();
    }

    private Task SeedWarehouseDetailAsync(string orderGuid, string detailId, decimal? quantity, decimal? allocatedQuantity) => _db.Insertable(new WareHouseOrderDetails
    {
        DetailGUID = detailId, OrderGUID = orderGuid, ProductCode = "P1", Quantity = quantity, AllocQuantity = allocatedQuantity, IsDeleted = false,
    }).ExecuteCommandAsync();

    private Task SeedSaleAsync(DateTime date, int quantity, decimal amount) => _db.Insertable(new ProductStoreDailySalesStatistic
    {
        Date = date, BranchCode = "S1", SupplierCode = "L100", ProductCode = "P1", TotalQuantity = quantity, TotalAmount = amount, OrderCount = 1, UpdateTime = new DateTime(2026, 9, 14, 23, 0, 0),
    }).ExecuteCommandAsync();

    private static SqlSugarContext Context(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_path);
    }
}
