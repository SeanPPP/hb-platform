using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("ProductHqSyncServiceTests")]
public sealed class ProductPushToHqServiceTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;

    public ProductPushToHqServiceTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(ProductSetCode),
            typeof(StoreMultiCodeProduct)
        );
        _hqDb.CodeFirst.InitTables(
            typeof(HqBranch),
            typeof(DIC_商品信息字典表),
            typeof(DIC_商品零售价表),
            typeof(DIC_一品多码表),
            typeof(DIC_分店一品多码表),
            typeof(CBP_DIC_商品库存表)
        );
    }

    [Fact]
    public async Task PushToHqAsync_空商品编码_返回验证错误()
    {
        var response = await CreateService().PushToHqAsync(new List<string>());

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_EMPTY_CODES", response.ErrorCode);
    }

    [Fact]
    public async Task PushToHqAsync_首次推送_创建商品分店价格多码和分店多码且不写库存()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new List<string> { " HB001 " });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(0, response.Data?.FailedCount);
        Assert.Equal(1, response.Data?.TotalCount);
        Assert.Equal(1, response.Data?.ProductsAdded);
        Assert.Equal(2, response.Data?.StoreRetailPricesCreated);
        Assert.Equal(1, response.Data?.ProductSetCodesCreated);
        Assert.Equal(2, response.Data?.StoreMultiCodesCreated);
        Assert.Equal(6, response.Data?.AffectedRowCount);
        Assert.Equal(0, await _hqDb.Queryable<CBP_DIC_商品库存表>().CountAsync());

        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("HB001-ITEM", product.H货号);
        Assert.Equal("测试商品英文", product.H商品名称);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, prices.Count);
        Assert.Contains(prices, row => row.H分店代码 == "S01" && row.H分店零售价 == 4.5m);
        Assert.Contains(prices, row => row.H分店代码 == "S02" && row.H分店商品编码 == "S02HB001");

        var setCode = await _hqDb.Queryable<DIC_一品多码表>()
            .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1");
        Assert.Equal("set-1", setCode.HGUID);
        Assert.Equal("952700000002", setCode.H多条形码);

        var storeMultiCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1")
            .ToListAsync();
        Assert.Equal(2, storeMultiCodes.Count);
        Assert.Contains(storeMultiCodes, row => row.H分店代码 == "S01" && row.H一品多码零售价 == 3.9m);
        Assert.Contains(storeMultiCodes, row => row.H分店代码 == "S02" && row.H一品多码零售价 == 4.0m);
    }

    [Fact]
    public async Task PushToHqAsync_重复推送_更新既有记录且不重复创建()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new List<string> { "HB001" });
        Assert.True(first.Success, first.Message);

        var second = await service.PushToHqAsync(new List<string> { "HB001" });

        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data?.ProductsUpdated);
        Assert.Equal(2, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(1, second.Data?.ProductSetCodesUpdated);
        Assert.Equal(2, second.Data?.StoreMultiCodesUpdated);
        Assert.Equal(6, second.Data?.AffectedRowCount);
        Assert.Equal(1, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(1, await _hqDb.Queryable<DIC_一品多码表>().CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_部分商品不存在_返回失败统计和错误明细()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new List<string> { "HB001", "HB404" });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(1, response.Data?.FailedCount);
        Assert.Equal(2, response.Data?.TotalCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), item => item.Contains("HB404"));
    }

    [Fact]
    public async Task PushToHqAsync_本地重复业务键_首次推送不会创建重复Hq记录()
    {
        await SeedProductGraphAsync();
        await SeedDuplicateProductGraphRowsAsync();

        var response = await CreateService().PushToHqAsync(new List<string> { "HB001" });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.ProductsAdded);
        Assert.Equal(2, response.Data?.StoreRetailPricesCreated);
        Assert.Equal(1, response.Data?.ProductSetCodesCreated);
        Assert.Equal(2, response.Data?.StoreMultiCodesCreated);
        Assert.Equal(1, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(1, await _hqDb.Queryable<DIC_一品多码表>().CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());

        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("测试商品英文新版", product.H商品名称);

        var setCode = await _hqDb.Queryable<DIC_一品多码表>()
            .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1");
        Assert.Equal(4.4m, setCode.H一品多码零售价);

        var storeMultiCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1")
            .ToListAsync();
        Assert.Contains(storeMultiCodes, row => row.H分店代码 == "S01" && row.H一品多码零售价 == 4.2m);
        Assert.Contains(storeMultiCodes, row => row.H分店代码 == "S02" && row.H一品多码零售价 == 4.4m);
    }

    [Fact]
    public async Task PushToHq_控制器使用Pos商品管理权限并委托服务()
    {
        var authorize = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.PushToHq))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Permissions.PosProducts.Manage, authorize?.Policy);

        var service = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        service
            .Setup(item => item.PushToHqAsync(It.Is<List<string>>(codes => codes.SequenceEqual(new[] { "HB001" }))))
            .ReturnsAsync(ApiResponse<PushProductsToHqResult>.OK(new PushProductsToHqResult(), "ok"));
        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            service.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>()
        );

        var response = await controller.PushToHq(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { " HB001 ", "HB001" },
        });

        Assert.IsType<OkObjectResult>(response);
        service.VerifyAll();
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hqConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
    }

    private async Task SeedProductGraphAsync()
    {
        await _hqDb.Insertable(new[]
        {
            new HqBranch { BranchCode = "S01", BranchName = "一店" },
            new HqBranch { BranchCode = "S02", BranchName = "二店" },
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new Product
        {
            UUID = "product-1",
            ProductCode = "HB001",
            LocalSupplierCode = "SUP01",
            ItemNumber = "HB001-ITEM",
            Barcode = "952700000001",
            ProductName = "测试商品中文",
            EnglishName = "测试商品英文",
            ProductType = 2,
            PurchasePrice = 2.5m,
            RetailPrice = 4.5m,
            MiddlePackageQuantity = 6,
            ProductImage = "HB001.jpg",
            IsActive = true,
            IsAutoPricing = true,
            IsSpecialProduct = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = "set-1",
            ProductCode = "HB001",
            SetProductCode = "HB001-M1",
            SetItemNumber = "HB001-M1",
            SetBarcode = "952700000002",
            SetPurchasePrice = 2.0m,
            SetRetailPrice = 4.0m,
            SetQuantity = 1,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-1",
            StoreCode = "S01",
            ProductCode = "HB001",
            MultiCodeProductCode = "HB001-M1",
            StoreMultiCodeProductCode = "S01HB001-M1",
            MultiBarcode = "952700000003",
            PurchasePrice = 1.9m,
            MultiCodeRetailPrice = 3.9m,
            DiscountRate = 0.95m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDuplicateProductGraphRowsAsync()
    {
        var latest = DateTime.UtcNow.AddDays(1);
        await _localDb.Insertable(new Product
        {
            UUID = "product-duplicate-latest",
            ProductCode = "HB001",
            LocalSupplierCode = "SUP01",
            ItemNumber = "HB001-ITEM-LATEST",
            Barcode = "952700000009",
            ProductName = "测试商品中文新版",
            EnglishName = "测试商品英文新版",
            ProductType = 2,
            PurchasePrice = 2.8m,
            RetailPrice = 5.5m,
            MiddlePackageQuantity = 8,
            ProductImage = "HB001-latest.jpg",
            IsActive = true,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsDeleted = false,
            CreatedAt = latest,
            UpdatedAt = latest,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = "set-duplicate-latest",
            ProductCode = "HB001",
            SetProductCode = "HB001-M1",
            SetItemNumber = "HB001-M1-LATEST",
            SetBarcode = "952700000010",
            SetPurchasePrice = 2.2m,
            SetRetailPrice = 4.4m,
            SetQuantity = 1,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = latest,
            UpdatedAt = latest,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-duplicate-latest",
            StoreCode = "S01",
            ProductCode = "HB001",
            MultiCodeProductCode = "HB001-M1",
            StoreMultiCodeProductCode = "S01HB001-M1-LATEST",
            MultiBarcode = "952700000011",
            PurchasePrice = 2.1m,
            MultiCodeRetailPrice = 4.2m,
            DiscountRate = 0.9m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = latest,
            UpdatedAt = latest,
        }).ExecuteCommandAsync();
    }

    private ProductHqSyncService CreateService()
    {
        return new ProductHqSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            Mock.Of<IMapper>(),
            Mock.Of<ILogger<ProductHqSyncService>>()
        );
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static IConfiguration CreateHqConfiguration(string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StoreHzgHQConnection"] = connectionString,
                }
            )
            .Build();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(
        ISqlSugarClient db,
        IConfiguration configuration
    )
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HqSqlSugarContext)
        );
        var dbField = typeof(HqSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        var configurationField = typeof(HqSqlSugarContext).GetField(
            "<Configuration>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        configurationField!.SetValue(context, configuration);
        return context;
    }
}
