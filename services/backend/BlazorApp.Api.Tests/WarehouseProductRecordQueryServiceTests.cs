using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseProductRecordQueryServiceTests : IDisposable
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly DateTime _today;

    public WarehouseProductRecordQueryServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(WarehouseProduct),
            typeof(Product),
            typeof(Container),
            typeof(ContainerDetail),
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(Store)
        );
        _today = ResolveBrisbaneDate(FixedUtcNow);
    }

    [Fact]
    public async Task GetSummaryAsync_商品编码大小写与空白不敏感且返回主档字段()
    {
        await SeedWarehouseProduct("P-1", true);
        await SeedProduct("P-1", "ITEM-1", "BAR-1", "商品一", "Product One", "img/1.jpg", true);

        var service = CreateService();
        var result = await service.GetSummaryAsync("  p-1 ");

        Assert.NotNull(result);
        Assert.Equal("P-1", result.ProductCode);
        Assert.Equal("ITEM-1", result.ItemNumber);
        Assert.Equal("BAR-1", result.Barcode);
        Assert.Equal("商品一", result.ProductName);
        Assert.Equal("Product One", result.EnglishName);
        Assert.Equal("img/1.jpg", result.ImageUrl);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task GetSummaryAsync_商品不存在返回null()
    {
        var result = await CreateService().GetSummaryAsync("MISSING");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSummaryAsync_软删除仓库商品不可查询()
    {
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = "P-DELETED",
            IsActive = true,
            IsDeleted = true,
        }).ExecuteCommandAsync();
        await SeedProduct("P-DELETED", "ITEM-D", "BAR-D", "已删除商品", "Deleted", "img/d.jpg", true);

        var result = await CreateService().GetSummaryAsync("P-DELETED");

        Assert.Null(result);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService().QueryContainersAsync(
            "P-DELETED",
            new WarehouseProductRecordContainerQueryRequest()
        ));
    }

    [Fact]
    public async Task QueryContainersAsync_默认排除状态7且显式状态可包含7()
    {
        await SeedWarehouseProduct("P-1", true);
        await SeedContainer("C-1", "柜-1", loadingDate: _today.AddDays(-10), status: 1);
        await SeedContainer("C-7", "柜-7", loadingDate: _today.AddDays(-9), status: 7);
        await SeedContainerDetail("D-1", "C-1", "P-1", 10, 100, 1, 2, 200);
        await SeedContainerDetail("D-7", "C-7", "P-1", 20, 200, 1, 2, 400);

        var service = CreateService();
        var defaultResult = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest());
        Assert.Equal(1, defaultResult.TotalCount);
        Assert.Equal(1, defaultResult.Summary.ContainerCount);
        Assert.Equal(10m, defaultResult.Summary.LoadingPieces);

        var explicitResult = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            Statuses = new() { 7 },
        });
        Assert.Equal(1, explicitResult.TotalCount);
        Assert.Equal("C-7", Assert.Single(explicitResult.Items).ContainerCode);
    }

    [Fact]
    public async Task QueryContainersAsync_排除软删除且关键字匹配编号或编码()
    {
        await SeedWarehouseProduct("P-1", true);
        await SeedContainer("C-KEEP", "ABC123", loadingDate: _today.AddDays(-5), status: 1);
        await SeedContainer("C-DEL", "ABC-DEL", loadingDate: _today.AddDays(-4), status: 1, isDeleted: true);
        await SeedContainerDetail("D-KEEP", "C-KEEP", "P-1", 1, 10, 1, 2, 20);
        await SeedContainerDetail("D-KEEP2", "C-KEEP", "P-1", 2, 20, 1, 2, 40);
        await SeedContainerDetail("D-DEL-DETAIL", "C-KEEP", "P-1", 99, 990, 1, 2, 999, isDeleted: true);
        await SeedContainerDetail("D-DEL", "C-DEL", "P-1", 5, 50, 1, 2, 100);

        var service = CreateService();
        var byNumber = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            ContainerKeyword = "ABC123",
        });
        Assert.Equal(2, byNumber.TotalCount);
        Assert.All(byNumber.Items, item => Assert.Equal("C-KEEP", item.ContainerCode));

        var byCode = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            ContainerKeyword = "C-KEEP",
        });
        Assert.Equal(2, byCode.TotalCount);
    }

    [Fact]
    public async Task QueryContainersAsync_有效到货日优先实际且仅日期筛选排除null()
    {
        await SeedWarehouseProduct("P-1", true);
        var actual = _today.AddDays(-3);
        var estimated = _today.AddDays(-10);
        await SeedContainer("C-ACTUAL", "柜-实际", estimatedArrival: estimated, actualArrival: actual, status: 1);
        await SeedContainer("C-EST", "柜-预计", estimatedArrival: _today.AddDays(-2), status: 1);
        await SeedContainer("C-NONE", "柜-无日期", status: 1);
        await SeedContainerDetail("D-ACTUAL", "C-ACTUAL", "P-1", 1, 10, 1, 2, 20);
        await SeedContainerDetail("D-EST", "C-EST", "P-1", 2, 20, 1, 2, 40);
        await SeedContainerDetail("D-NONE", "C-NONE", "P-1", 3, 30, 1, 2, 60);

        var service = CreateService();
        var all = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest());
        Assert.Equal(3, all.TotalCount);

        var byDate = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            ArrivalStartDate = _today.AddDays(-5),
            ArrivalEndDate = _today,
        });
        Assert.Equal(2, byDate.TotalCount);
        Assert.DoesNotContain(byDate.Items, item => item.ContainerCode == "C-NONE");
        var actualItem = Assert.Single(byDate.Items, item => item.ContainerCode == "C-ACTUAL");
        Assert.Equal(actual, actualItem.EffectiveArrivalDate);
    }

    [Fact]
    public async Task QueryContainersAsync_未来预计货柜默认可见且分页排序合计正确()
    {
        await SeedWarehouseProduct("P-1", true);
        var future = _today.AddDays(5);
        await SeedContainer("C-FUTURE", "FUTURE-1", estimatedArrival: future, status: 1);
        await SeedContainer("C-PAST", "PAST-1", estimatedArrival: _today.AddDays(-5), status: 1);
        await SeedContainerDetail("D-FUTURE", "C-FUTURE", "P-1", 3, 30, 5, 6, 180);
        await SeedContainerDetail("D-PAST", "C-PAST", "P-1", 4, 40, 7, 8, 320);

        var service = CreateService();
        var result = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            PageNumber = 1,
            PageSize = 1,
            SortBy = "effectiveArrivalDate",
            SortDirection = "desc",
        });

        Assert.Equal(2, result.TotalCount);
        var firstItem = Assert.Single(result.Items);
        Assert.Equal("C-FUTURE", firstItem.ContainerCode);
        Assert.Equal(2, result.Summary.ContainerCount);
        Assert.Equal(7m, result.Summary.LoadingPieces);
        Assert.Equal(70m, result.Summary.LoadingQuantity);
        Assert.Equal(500m, result.Summary.TotalAmount);

        var byContainerNumber = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            PageNumber = 1,
            PageSize = 1,
            SortBy = "containerNumber",
            SortDirection = "desc",
        });
        Assert.Equal("PAST-1", Assert.Single(byContainerNumber.Items).ContainerNumber);
    }

    [Fact]
    public async Task QueryContainersAsync_无效分页参数抛参数异常()
    {
        await SeedWarehouseProduct("P-1", true);
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryContainersAsync(
            "P-1",
            new WarehouseProductRecordContainerQueryRequest { PageNumber = 0 }
        ));
        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryContainersAsync(
            "P-1",
            new WarehouseProductRecordContainerQueryRequest { PageSize = 101 }
        ));
        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryContainersAsync(
            "P-1",
            new WarehouseProductRecordContainerQueryRequest
            {
                ArrivalStartDate = _today,
                ArrivalEndDate = _today.AddDays(-1),
            }
        ));
    }

    [Fact]
    public async Task QueryContainersAsync_同货柜多明细使用DetailCode稳定分页()
    {
        await SeedWarehouseProduct("P-1", true);
        await SeedContainer("C-SAME", "SAME", estimatedArrival: _today, status: 1);
        await SeedContainerDetail("D-2", "C-SAME", "P-1", 1, 10, 1, 2, 20);
        await SeedContainerDetail("D-1", "C-SAME", "P-1", 1, 10, 1, 2, 20);

        var service = CreateService();
        var first = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            PageNumber = 1,
            PageSize = 1,
        });
        var second = await service.QueryContainersAsync("P-1", new WarehouseProductRecordContainerQueryRequest
        {
            PageNumber = 2,
            PageSize = 1,
        });

        Assert.Equal("D-1", Assert.Single(first.Items).DetailCode);
        Assert.Equal("D-2", Assert.Single(second.Items).DetailCode);
    }

    [Fact]
    public async Task QueryAllocationsAsync_业务日优先出库且分店代码回退未知()
    {
        await SeedWarehouseProduct("P-1", true);
        await SeedStore("S-1", "S1", "分店一", true);
        var today = _today;

        await SeedOrder("O-1", orderDate: today.AddDays(-5), outboundDate: today.AddDays(-2), storeCode: "S1");
        await SeedOrderDetail("D-1", "O-1", "P-1", detailStore: null, allocQuantity: 2, importPrice: 3);
        await SeedOrderDetail("D-2", "O-1", "P-1", detailStore: " S9 ", allocQuantity: 4, importPrice: 5);
        await SeedOrder("O-EMPTY", orderDate: today.AddDays(-1), outboundDate: null, storeCode: null);
        await SeedOrderDetail("D-EMPTY", "O-EMPTY", "P-1", detailStore: null, allocQuantity: 1, importPrice: 2);

        var service = CreateService();
        var result = await service.QueryAllocationsAsync("P-1", new WarehouseProductRecordAllocationQueryRequest
        {
            StartDate = today.AddDays(-10),
            EndDate = today,
        });

        Assert.Equal(7m, result.Summary.AllocationQuantity);
        Assert.Equal(6m + 20m + 2m, result.Summary.AllocationAmount);
        Assert.Equal(2, result.Summary.OrderCount);

        var s1 = Assert.Single(result.Branches, b => b.StoreCode == "S1");
        Assert.Equal("分店一", s1.StoreName);
        Assert.True(s1.IsActive);
        Assert.Equal(2m, s1.AllocationQuantity);
        Assert.Equal(6m, s1.AllocationAmount);
        Assert.Equal(1, s1.OrderCount);

        var unknown = Assert.Single(result.Branches, b => b.StoreCode == "S9");
        Assert.Equal("未匹配分店（S9）", unknown.StoreName);
        Assert.False(unknown.IsActive);

        var none = Assert.Single(result.Branches, b => b.StoreCode == string.Empty);
        Assert.Equal("未匹配分店（无编码）", none.StoreName);
    }

    [Fact]
    public async Task QueryAllocationsAsync_排除软删除且订单去重()
    {
        await SeedWarehouseProduct("P-1", true);
        await SeedStore("S-1", "S1", "分店一", true);
        var today = _today;

        await SeedOrder("O-KEEP", orderDate: today, outboundDate: null, storeCode: "S1");
        await SeedOrderDetail("D-1", "O-KEEP", "P-1", "S1", 2, 3);
        await SeedOrderDetail("D-2", "O-KEEP", "P-1", "S1", 1, 4);
        await SeedOrder("O-DEL", orderDate: today, outboundDate: null, storeCode: "S1", isDeleted: true);
        await SeedOrderDetail("D-DEL", "O-DEL", "P-1", "S1", 99, 1);
        await SeedOrder("O-DETAIL-DEL", orderDate: today, outboundDate: null, storeCode: "S1");
        await SeedOrderDetail("D-DETAIL-DEL", "O-DETAIL-DEL", "P-1", "S1", 88, 1, isDeleted: true);

        var service = CreateService();
        var result = await service.QueryAllocationsAsync("P-1", new WarehouseProductRecordAllocationQueryRequest
        {
            StartDate = today,
            EndDate = today,
        });

        var branch = Assert.Single(result.Branches);
        Assert.Equal(3m, branch.AllocationQuantity);
        Assert.Equal(2m * 3m + 1m * 4m, branch.AllocationAmount);
        Assert.Equal(1, branch.OrderCount);
        Assert.Equal(1, result.Summary.OrderCount);
    }

    [Fact]
    public async Task QueryAllocationsAsync_366天边界内允许超出拒绝且未来拒绝()
    {
        await SeedWarehouseProduct("P-1", true);
        var service = CreateService();
        var today = _today;

        var ok = await service.QueryAllocationsAsync("P-1", new WarehouseProductRecordAllocationQueryRequest
        {
            StartDate = today.AddDays(-365),
            EndDate = today,
        });
        Assert.NotNull(ok);

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAllocationsAsync(
            "P-1",
            new WarehouseProductRecordAllocationQueryRequest
            {
                StartDate = today.AddDays(-366),
                EndDate = today,
            }
        ));

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAllocationsAsync(
            "P-1",
            new WarehouseProductRecordAllocationQueryRequest
            {
                StartDate = today,
                EndDate = today.AddDays(1),
            }
        ));
    }

    [Fact]
    public async Task QueryAllocationsAsync_开始或结束日期缺失时拒绝()
    {
        await SeedWarehouseProduct("P-1", true);
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAllocationsAsync(
            "P-1",
            new WarehouseProductRecordAllocationQueryRequest()
        ));
        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAllocationsAsync(
            "P-1",
            new WarehouseProductRecordAllocationQueryRequest { StartDate = _today }
        ));
        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAllocationsAsync(
            "P-1",
            new WarehouseProductRecordAllocationQueryRequest { EndDate = _today }
        ));
    }

    [Fact]
    public async Task QueryContainersAsync_商品不存在抛KeyNotFound()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.QueryContainersAsync(
            "MISSING",
            new WarehouseProductRecordContainerQueryRequest()
        ));
    }

    [Fact]
    public async Task QueryAllocationsAsync_商品不存在抛KeyNotFound()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.QueryAllocationsAsync(
            "MISSING",
            new WarehouseProductRecordAllocationQueryRequest { StartDate = _today, EndDate = _today }
        ));
    }

    private WarehouseProductRecordQueryService CreateService() =>
        new(CreateContext(_db), NullLogger<WarehouseProductRecordQueryService>.Instance, new FixedTimeProvider(FixedUtcNow));

    private static SqlSugarContext CreateContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private async Task SeedWarehouseProduct(string productCode, bool isActive) =>
        await _db.Insertable(new WarehouseProduct { ProductCode = productCode, IsActive = isActive }).ExecuteCommandAsync();

    private async Task SeedProduct(string productCode, string itemNumber, string barcode, string name, string englishName, string image, bool isActive) =>
        await _db.Insertable(new Product
        {
            UUID = $"U-{productCode}",
            ProductCode = productCode,
            ItemNumber = itemNumber,
            Barcode = barcode,
            ProductName = name,
            EnglishName = englishName,
            ProductImage = image,
            IsActive = isActive,
        }).ExecuteCommandAsync();

    private async Task SeedContainer(
        string containerCode,
        string containerNumber,
        DateTime? loadingDate = null,
        DateTime? estimatedArrival = null,
        DateTime? actualArrival = null,
        int? status = 1,
        bool isDeleted = false
    ) =>
        await _db.Insertable(new Container
        {
            ContainerCode = containerCode,
            ContainerNumber = containerNumber,
            LoadingDate = loadingDate,
            EstimatedArrivalDate = estimatedArrival,
            ActualArrivalDate = actualArrival,
            Status = status,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();

    private async Task SeedContainerDetail(
        string detailCode,
        string containerCode,
        string productCode,
        decimal loadingPieces,
        decimal loadingQuantity,
        decimal domesticPrice,
        decimal importPrice,
        decimal totalAmount,
        bool isDeleted = false
    ) =>
        await _db.Insertable(new ContainerDetail
        {
            DetailCode = detailCode,
            ContainerCode = containerCode,
            ProductCode = productCode,
            LoadingPieces = loadingPieces,
            LoadingQuantity = loadingQuantity,
            DomesticPrice = domesticPrice,
            ImportPrice = importPrice,
            TotalAmount = totalAmount,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();

    private async Task SeedStore(string storeGuid, string storeCode, string storeName, bool isActive, bool isDeleted = false) =>
        await _db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();

    private async Task SeedOrder(
        string orderGuid,
        DateTime? orderDate,
        DateTime? outboundDate,
        string? storeCode,
        bool isDeleted = false
    ) =>
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            OrderDate = orderDate,
            OutboundDate = outboundDate,
            StoreCode = storeCode,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();

    private async Task SeedOrderDetail(
        string detailGuid,
        string orderGuid,
        string productCode,
        string? detailStore,
        decimal allocQuantity,
        decimal importPrice,
        bool isDeleted = false
    ) =>
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = detailGuid,
            OrderGUID = orderGuid,
            ProductCode = productCode,
            StoreCode = detailStore,
            AllocQuantity = allocQuantity,
            ImportPrice = importPrice,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();

    private static DateTime ResolveBrisbaneDate(DateTime utcNow)
    {
        foreach (var id in new[] { "Australia/Brisbane", "E. Australia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById(id)).Date;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.Local).Date;
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (File.Exists(_dbPath)) SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }
}
