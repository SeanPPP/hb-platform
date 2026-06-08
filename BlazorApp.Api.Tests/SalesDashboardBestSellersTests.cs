using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDashboardBestSellersTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public SalesDashboardBestSellersTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _localConnection.Open();
        _posmConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(typeof(Product), typeof(WarehouseProduct), typeof(Store));
        _posmDb.CodeFirst.InitTables(typeof(SalesOrder), typeof(SalesOrderDetail), typeof(POSM_设备注册信息表));
    }

    [Fact]
    public async Task GetBestSellersAsync_返回仓库状态条码起订量和参与统计分店数量()
    {
        await SeedProductAsync("P-BEST-1", "ITEM-1", "BAR-1", "热销一", productIsActive: true, warehouseIsActive: false, minOrderQuantity: 6);
        await SeedProductAsync("P-BEST-2", "ITEM-2", "BAR-2", "热销二", productIsActive: false, warehouseIsActive: true, minOrderQuantity: 3);
        await SeedStoreAsync("S1", "Store 1");
        await SeedStoreAsync("S2", "Store 2");
        await SeedStoreAsync("S3", "Store 3");

        await SeedSaleAsync("O-1", "D-1", "P-BEST-1", "S1", new DateTime(2026, 6, 1), 5, 10m);
        await SeedSaleAsync("O-2", "D-2", "P-BEST-1", "S2", new DateTime(2026, 6, 2), 7, 14m);
        await SeedSaleAsync("O-3", "D-3", "P-BEST-1", "S1", new DateTime(2026, 6, 3), 2, 4m);
        await SeedSaleAsync("O-4", "D-4", "P-BEST-2", "S3", new DateTime(2026, 6, 4), 20, 40m);

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            new List<string> { "S1", "S2" },
            pageIndex: 1,
            pageSize: 10
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("P-BEST-1", product.ProductCode);
        Assert.Equal("BAR-1", product.Barcode);
        Assert.False(product.IsActive);
        Assert.Equal(6, product.MinOrderQuantity);
        Assert.Equal(14, product.Quantity);
        Assert.Equal(28m, product.SalesAmount);
        Assert.Equal(2, product.BranchSalesCount);
        Assert.Collection(
            product.BranchSales,
            row =>
            {
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal("Store 1", row.BranchName);
                Assert.Equal(7, row.Quantity);
            },
            row =>
            {
                Assert.Equal("S2", row.BranchCode);
                Assert.Equal("Store 2", row.BranchName);
                Assert.Equal(7, row.Quantity);
            }
        );
    }

    [Fact]
    public async Task GetBestSellersAsync_空权限分店列表直接返回空结果()
    {
        await SeedProductAsync("P-NO-STORE", "ITEM-NO-STORE", "BAR-NO-STORE", "无权限商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedSaleAsync("O-NO-STORE", "D-NO-STORE", "P-NO-STORE", "S1", new DateTime(2026, 6, 1), 9, 18m);

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            new List<string>(),
            pageIndex: 1,
            pageSize: 50
        );

        Assert.Empty(result.Products);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task GetBestSellersAsync_忽略软删除仓库库存状态()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "P-SOFT-uuid",
            ProductCode = "P-SOFT",
            ItemNumber = "ITEM-SOFT",
            Barcode = "BAR-SOFT",
            ProductName = "软删除库存商品",
            LocalSupplierCode = "200",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = "P-SOFT",
            IsActive = false,
            MinOrderQuantity = 12,
            IsDeleted = true,
        }).ExecuteCommandAsync();
        await SeedSaleAsync("O-SOFT", "D-SOFT", "P-SOFT", "S1", new DateTime(2026, 6, 1), 4, 8m);

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Null(product.IsActive);
        Assert.Null(product.MinOrderQuantity);
    }

    [Fact]
    public async Task GetBestSellersAsync_仓库商品匹配不受本地供应商码限制()
    {
        await SeedProductAsync(
            "HB022-119",
            "HB022-119",
            "9525810220084",
            "TOY",
            productIsActive: true,
            warehouseIsActive: false,
            minOrderQuantity: 4,
            localSupplierCode: null
        );
        await SeedSaleAsync("O-HB022", "D-HB022", "HB022-119", "S1", new DateTime(2026, 6, 1), 9, 18m);

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("9525810220084", product.Barcode);
        Assert.False(product.IsActive);
        Assert.Equal(4, product.MinOrderQuantity);
    }

    [Fact]
    public async Task GetBestSellersAsync_HBweb条码缺失时回退POSM销售明细条码和名称()
    {
        await SeedProductAsync(
            "P-POSM-BAR",
            "ITEM-POSM-BAR",
            barcode: null,
            name: "",
            productIsActive: true,
            warehouseIsActive: true,
            minOrderQuantity: 1
        );
        await SeedSaleAsync(
            "O-POSM-BAR",
            "D-POSM-BAR",
            "P-POSM-BAR",
            "S1",
            new DateTime(2026, 6, 1),
            3,
            6m,
            barcode: "POSM-BAR-001",
            productName: "POSM 商品名"
        );

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal("POSM-BAR-001", product.Barcode);
        Assert.Equal("POSM 商品名", product.ProductName);
    }

    [Fact]
    public async Task GetBestSellersAsync_订单分店为空时用设备注册分店统计StoresSold()
    {
        await SeedProductAsync("P-DEVICE-STORE", "ITEM-DEVICE-STORE", "BAR-DEVICE", "设备分店商品", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedStoreAsync("S1", "Store 1");
        await SeedStoreAsync("S2", "Store 2");
        await SeedDeviceAsync("POS-1", "S1");
        await SeedDeviceAsync("POS-2", "S2");
        await SeedSaleAsync("O-DEVICE-1", "D-DEVICE-1", "P-DEVICE-STORE", null, new DateTime(2026, 6, 1), 5, 10m, deviceCode: "POS-1");
        await SeedSaleAsync("O-DEVICE-2", "D-DEVICE-2", "P-DEVICE-STORE", null, new DateTime(2026, 6, 2), 7, 14m, deviceCode: "POS-2");

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 1,
            pageSize: 50
        );

        var product = Assert.Single(result.Products);
        Assert.Equal(2, product.BranchSalesCount);
        Assert.Collection(
            product.BranchSales,
            row =>
            {
                Assert.Equal("S2", row.BranchCode);
                Assert.Equal("Store 2", row.BranchName);
                Assert.Equal(7, row.Quantity);
            },
            row =>
            {
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal("Store 1", row.BranchName);
                Assert.Equal(5, row.Quantity);
            }
        );
    }

    [Fact]
    public async Task GetBestSellersAsync_数据库分页返回正确总数排名和当前页分店销量()
    {
        await SeedProductAsync("P-RANK-1", "ITEM-RANK-1", "BAR-RANK-1", "排名一", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedProductAsync("P-RANK-2", "ITEM-RANK-2", "BAR-RANK-2", "排名二", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedProductAsync("P-RANK-3", "ITEM-RANK-3", "BAR-RANK-3", "排名三", productIsActive: true, warehouseIsActive: true, minOrderQuantity: 1);
        await SeedStoreAsync("S1", "Store 1");
        await SeedStoreAsync("S2", "Store 2");

        await SeedSaleAsync("O-RANK-1", "D-RANK-1", "P-RANK-1", "S1", new DateTime(2026, 6, 1), 30, 60m);
        await SeedSaleAsync("O-RANK-2", "D-RANK-2", "P-RANK-2", "S1", new DateTime(2026, 6, 2), 12, 24m);
        await SeedSaleAsync("O-RANK-3", "D-RANK-3", "P-RANK-2", "S2", new DateTime(2026, 6, 3), 8, 16m);
        await SeedSaleAsync("O-RANK-4", "D-RANK-4", "P-RANK-3", "S1", new DateTime(2026, 6, 4), 5, 10m);

        var result = await CreateService().GetBestSellersAsync(
            new DateRangeDto { StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 8) },
            null,
            pageIndex: 2,
            pageSize: 1
        );

        var product = Assert.Single(result.Products);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, product.Rank);
        Assert.Equal("P-RANK-2", product.ProductCode);
        Assert.Equal(20, product.Quantity);
        Assert.Equal(2, product.BranchSalesCount);
        Assert.Equal(new[] { "S1", "S2" }, product.BranchSales.Select(x => x.BranchCode).ToArray());
    }

    [Fact]
    public async Task GetBestSellers_普通用户传入请求分店时只把权限交集传给服务()
    {
        List<string>? capturedBranchCodes = null;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(x => x.GetBestSellersAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>(),
                It.IsAny<int>(),
                It.IsAny<int>()
            ))
            .Callback<DateRangeDto, List<string>?, int, int>((_, branchCodes, _, _) => capturedBranchCodes = branchCodes)
            .ReturnsAsync(new BestSellerResponseDto());

        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = new List<UserStoreDto>
                {
                    new() { StoreCode = "S1" },
                    new() { StoreCode = "S3" },
                },
            }));

        var controller = CreateController(serviceMock.Object, userServiceMock.Object);

        var response = await controller.GetBestSellers(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 8),
            new List<string> { "S1", "S2" },
            pageIndex: 1,
            pageSize: 50
        );

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(new[] { "S1" }, capturedBranchCodes);
    }

    [Fact]
    public async Task GetBestSellers_普通用户请求分店无权限交集时返回空结果且不调用服务()
    {
        var serviceMock = new Mock<ISalesDashboardReactService>();
        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = new List<UserStoreDto>
                {
                    new() { StoreCode = "S3" },
                },
            }));

        var controller = CreateController(serviceMock.Object, userServiceMock.Object);

        var response = await controller.GetBestSellers(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 8),
            new List<string> { "S1", "S2" },
            pageIndex: 2,
            pageSize: 50
        );

        serviceMock.Verify(
            x => x.GetBestSellersAsync(It.IsAny<DateRangeDto>(), It.IsAny<List<string>?>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never
        );
        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Contains("Total = 0", ok.Value?.ToString());
        Assert.Contains("PageIndex = 2", ok.Value?.ToString());
    }

    [Fact]
    public async Task GetBestSellers_普通用户没有关联分店时返回空结果且不调用服务()
    {
        var serviceMock = new Mock<ISalesDashboardReactService>();
        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = new List<UserStoreDto>(),
            }));

        var controller = CreateController(serviceMock.Object, userServiceMock.Object);

        var response = await controller.GetBestSellers(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 8),
            branchCodes: null,
            pageIndex: 1,
            pageSize: 50
        );

        serviceMock.Verify(
            x => x.GetBestSellersAsync(It.IsAny<DateRangeDto>(), It.IsAny<List<string>?>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never
        );
        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Contains("Total = 0", ok.Value?.ToString());
    }

    private async Task SeedProductAsync(
        string productCode,
        string itemNumber,
        string? barcode,
        string name,
        bool productIsActive,
        bool warehouseIsActive,
        int minOrderQuantity,
        string? localSupplierCode = "200"
    )
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"{productCode}-uuid",
            ProductCode = productCode,
            ItemNumber = itemNumber,
            Barcode = barcode,
            ProductName = name,
            ProductImage = $"{productCode}.jpg",
            LocalSupplierCode = localSupplierCode,
            IsActive = productIsActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            IsActive = warehouseIsActive,
            MinOrderQuantity = minOrderQuantity,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeCode, string storeName)
    {
        await _localDb.Insertable(new Store
        {
            StoreGUID = $"{storeCode}-guid",
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDeviceAsync(string deviceCode, string branchCode)
    {
        await _posmDb.Insertable(new POSM_设备注册信息表
        {
            系统设备编号 = deviceCode,
            设备硬件识别码 = $"{deviceCode}-hardware",
            分店代码 = branchCode,
            设备类型 = "POS",
            设备系统 = "Windows",
            设备状态 = 1,
            设备授权码 = $"{deviceCode}-auth",
        }).ExecuteCommandAsync();
    }

    private async Task SeedSaleAsync(
        string orderGuid,
        string detailGuid,
        string productCode,
        string? branchCode,
        DateTime orderTime,
        int quantity,
        decimal actualAmount,
        string? barcode = null,
        string? productName = null,
        string? deviceCode = null
    )
    {
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = orderGuid,
            BranchCode = branchCode,
            DeviceCode = deviceCode,
            OrderTime = orderTime,
            Status = 1,
        }).ExecuteCommandAsync();

        await _posmDb.Insertable(new SalesOrderDetail
        {
            OrderDetailGuid = detailGuid,
            OrderGuid = orderGuid,
            ProductCode = productCode,
            SupplierCode = "200",
            Barcode = barcode,
            ProductName = productName,
            Quantity = quantity,
            ActualAmount = actualAmount,
        }).ExecuteCommandAsync();
    }

    private SalesDashboardReactService CreateService()
    {
        return new SalesDashboardReactService(
            CreateSqlSugarContext(_localDb),
            CreatePosmSqlSugarContext(_posmDb),
            Mock.Of<IMapper>(),
            NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions())
        );
    }

    private static SalesDashboardController CreateController(
        ISalesDashboardReactService service,
        IUserService userService
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
            },
            "TestAuth"
        ));

        var controller = new SalesDashboardController(
            service,
            NullLogger<SalesDashboardController>.Instance,
            userService,
            Mock.Of<ISalesDashboardCacheWarmer>()
        );
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _localConnection.Dispose();
        _posmConnection.Dispose();
        if (File.Exists(_localDbPath)) File.Delete(_localDbPath);
        if (File.Exists(_posmDbPath)) File.Delete(_posmDbPath);
    }
}
