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
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreProductMaintenanceWarehousePriceSyncTests : IDisposable
{
    private const string StorePriceUuid = "store-price-1";
    private const string ProductCode = "product-1";
    private const string StoreCode = "store-1";

    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;
    private readonly MemoryCache _cache;
    private readonly Mock<IAutoPricingService> _autoPricingService = new();

    public StoreProductMaintenanceWarehousePriceSyncTests()
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
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(StoreRetailPrice),
            typeof(ProductSetCode),
            typeof(StoreMultiCodeProduct),
            typeof(Store)
        );

        _autoPricingService
            .Setup(service => service.FindStrategyForPriceAsync(
                It.IsAny<decimal>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()
            ))
            .ReturnsAsync((PricingStrategy?)null);
        _autoPricingService
            .Setup(service => service.CalculateRate(It.IsAny<decimal>(), It.IsAny<PricingStrategy?>()))
            .Returns(0m);
        _autoPricingService
            .Setup(service => service.GetAllActiveStrategiesAsync())
            .ReturnsAsync(new List<PricingStrategy>());
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    [Fact]
    public void Contract_定义仓库价格对账请求与响应字段()
    {
        var requestType = typeof(SyncStoreProductWarehousePriceRequestDto);
        var responseType = typeof(SyncStoreProductWarehousePriceResultDto);

        Assert.NotNull(requestType.GetProperty("ConfirmRetailPrice"));
        Assert.NotNull(requestType.GetProperty("ExpectedWarehousePurchasePrice"));
        Assert.NotNull(requestType.GetProperty("ExpectedWarehouseRetailPrice"));
        Assert.NotNull(requestType.GetProperty("ExpectedStorePurchasePrice"));
        Assert.NotNull(requestType.GetProperty("ExpectedStoreRetailPrice"));
        Assert.NotNull(requestType.GetProperty("ExpectedDiscountRate"));
        Assert.NotNull(responseType.GetProperty("Status"));
        Assert.NotNull(responseType.GetProperty("PurchaseUpdated"));
        Assert.NotNull(responseType.GetProperty("RetailUpdated"));
        Assert.NotNull(responseType.GetProperty("RetailConfirmationRequired"));
        Assert.NotNull(responseType.GetProperty("StorePrice"));
        Assert.NotNull(responseType.GetProperty("WarehousePurchasePrice"));
        Assert.NotNull(responseType.GetProperty("WarehouseRetailPrice"));
        Assert.NotNull(responseType.GetProperty("PreviousStorePurchasePrice"));
        Assert.NotNull(responseType.GetProperty("PreviousStoreRetailPrice"));
        Assert.NotNull(responseType.GetProperty("DiscountRate"));
        Assert.NotNull(responseType.GetProperty("PreviousDiscountedRetailPrice"));
        Assert.NotNull(responseType.GetProperty("NewDiscountedRetailPrice"));
    }

    [Fact]
    public void Controller_暴露当前分店仓库价格对账路由()
    {
        var method = typeof(ReactStoreProductMaintenanceController).GetMethod(
            "SyncWarehousePrice",
            BindingFlags.Instance | BindingFlags.Public
        );
        var route = method?.GetCustomAttribute<HttpPostAttribute>();
        var serviceMethod = typeof(IStoreProductMaintenanceReactService).GetMethod(
            "SyncWarehousePriceAsync"
        );

        Assert.NotNull(method);
        Assert.Equal("store-prices/{uuid}/sync-warehouse", route?.Template);
        Assert.NotNull(serviceMethod);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_非供应商200不写入()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("225", 5m, 10m, 0.9m, updatedAt, 6m, 12m);

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.Equal("not_applicable", result.Data!.Status);
        Assert.False(result.Data.PurchaseUpdated);
        Assert.False(result.Data.RetailUpdated);
        var entity = await GetStorePriceAsync();
        Assert.Equal(5m, entity.PurchasePrice);
        Assert.Equal(10m, entity.StoreRetailPriceValue);
        Assert.Equal(updatedAt, entity.UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncWarehousePriceAsync_仓库缺失或两个价格均无效时不覆盖(bool seedInvalidSource)
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("200", 5m, 10m, 0.9m, updatedAt);
        if (seedInvalidSource)
        {
            await InsertWarehousePriceAsync(0m, -1m);
        }

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.Equal("missing_source", result.Data!.Status);
        var entity = await GetStorePriceAsync();
        Assert.Equal(5m, entity.PurchasePrice);
        Assert.Equal(10m, entity.StoreRetailPriceValue);
        Assert.Equal(updatedAt, entity.UpdatedAt);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_仓库价格舍入为零时不覆盖分店价格()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("200", 5m, 10m, 0.2m, updatedAt, 0.004m, 10m);

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.Equal("synced", result.Data!.Status);
        Assert.False(result.Data.PurchaseUpdated);
        var entity = await GetStorePriceAsync();
        Assert.Equal(5m, entity.PurchasePrice);
        Assert.Equal(updatedAt, entity.UpdatedAt);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_自动同步进货价且不触发自动定价并要求确认零售价()
    {
        await SeedAsync(" 200 ", 5m, 10m, 0.9m, DateTime.UtcNow.AddDays(-2), 6.5m, 12m);
        var original = await GetStorePriceAsync();
        original.IsAutoPricing = true;
        original.IsSpecialProduct = true;
        original.IsActive = false;
        await _db.Updateable(original).ExecuteCommandAsync();

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.Equal("confirmation_required", result.Data!.Status);
        Assert.True(result.Data.PurchaseUpdated);
        Assert.False(result.Data.RetailUpdated);
        Assert.True(result.Data.RetailConfirmationRequired);
        Assert.Equal(5m, result.Data.PreviousStorePurchasePrice);
        Assert.Equal(10m, result.Data.PreviousStoreRetailPrice);
        var entity = await GetStorePriceAsync();
        Assert.Equal(6.5m, entity.PurchasePrice);
        Assert.Equal(10m, entity.StoreRetailPriceValue);
        Assert.Equal(0.9m, entity.DiscountRate);
        Assert.True(entity.IsAutoPricing);
        Assert.True(entity.IsSpecialProduct);
        Assert.False(entity.IsActive);
        _autoPricingService.Verify(
            service => service.CalculateRetailPrice(It.IsAny<decimal>(), It.IsAny<PricingStrategy?>()),
            Times.Never
        );
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_主记录只写价格与更新审计列()
    {
        await SeedAsync("200", 5m, 10m, 0.25m, DateTime.UtcNow.AddDays(-2), 6.5m, 12m);
        var original = await GetStorePriceAsync();
        original.IsAutoPricing = true;
        original.IsSpecialProduct = true;
        original.IsActive = false;
        await _db.Updateable(original).ExecuteCommandAsync();
        string? storePriceUpdateSql = null;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("StoreRetailPrice", StringComparison.OrdinalIgnoreCase)
            )
            {
                storePriceUpdateSql = sql;
            }
        };

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.NotNull(storePriceUpdateSql);
        var setClause = storePriceUpdateSql![..storePriceUpdateSql.IndexOf(
            "WHERE",
            StringComparison.OrdinalIgnoreCase
        )];
        Assert.Contains("PurchasePrice", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UpdatedAt", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UpdatedBy", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DiscountRate", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsAutoPricing", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsSpecialProduct", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsActive", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedAt", setClause, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedBy", setClause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_权威更新锁固定按商品仓库分店价顺序获取()
    {
        await SeedAsync("200", 6.5m, 12m, 0.2m, DateTime.UtcNow.AddDays(-2), 6.5m, 12m);
        var authorityReadOrder = new List<string>();
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (sql.Contains("StoreRetailPrice", StringComparison.OrdinalIgnoreCase))
            {
                authorityReadOrder.Add("StoreRetailPrice");
            }
            else if (sql.Contains("WarehouseProduct", StringComparison.OrdinalIgnoreCase))
            {
                authorityReadOrder.Add("WarehouseProduct");
            }
            else if (sql.Contains("Product", StringComparison.OrdinalIgnoreCase))
            {
                authorityReadOrder.Add("Product");
            }
        };

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.Equal(
            new[] { "StoreRetailPrice", "Product", "WarehouseProduct", "StoreRetailPrice" },
            authorityReadOrder.Take(4)
        );
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_锁定重读变成无权分店时不返回价格数据()
    {
        await SeedAsync("200", 6.5m, 12m, 0.2m, DateTime.UtcNow.AddDays(-2), 6.5m, 12m);
        var storeChangedAfterLocatorRead = false;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                storeChangedAfterLocatorRead
                || !sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || !(
                    sql.Contains("FROM `Product`", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("FROM \"Product\"", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("FROM [Product]", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return;
            }

            storeChangedAfterLocatorRead = true;
            _db.Ado.ExecuteCommand(
                "UPDATE StoreRetailPrice SET StoreCode = @storeCode WHERE UUID = @uuid",
                new { storeCode = "other-store", uuid = StorePriceUuid }
            );
        };

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(storeChangedAfterLocatorRead);
        Assert.False(result.Success);
        Assert.Equal("当前账号或设备无权修改该分店商品", result.Message);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_条件更新未命中时返回价格冲突()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("200", 5m, 10m, 0.25m, updatedAt, 6.5m, 12m);
        await _db.Ado.ExecuteCommandAsync(
            """
            CREATE TRIGGER ignore_store_price_update
            BEFORE UPDATE ON StoreRetailPrice
            BEGIN
                SELECT RAISE(IGNORE);
            END;
            """
        );

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.False(result.Success);
        Assert.Equal("PRICE_VERSION_CONFLICT", result.ErrorCode);
        Assert.Equal(5m, result.Data!.StorePrice!.PurchasePrice);
        Assert.Equal(updatedAt, (await GetStorePriceAsync()).UpdatedAt);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_确认零售价保留折扣并同步派生记录()
    {
        await SeedAsync("200", 5m, 10m, 0.2m, DateTime.UtcNow.AddDays(-2), 6.5m, 12m);
        await SeedProjectedSetCodeAsync();
        var service = CreateService();

        var preview = await service.SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );
        Assert.Equal("confirmation_required", preview.Data!.Status);

        var result = await service.SyncWarehousePriceAsync(
            StorePriceUuid,
            CreateConfirmRequest(6.5m, 12m, 6.5m, 10m, 0.2m),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.Equal("synced", result.Data!.Status);
        Assert.False(result.Data.PurchaseUpdated);
        Assert.True(result.Data.RetailUpdated);
        Assert.False(result.Data.RetailConfirmationRequired);
        Assert.Equal(0.2m, result.Data.DiscountRate);
        Assert.Equal(8m, result.Data.PreviousDiscountedRetailPrice);
        Assert.Equal(9.6m, result.Data.NewDiscountedRetailPrice);
        var entity = await GetStorePriceAsync();
        Assert.Equal(6.5m, entity.PurchasePrice);
        Assert.Equal(12m, entity.StoreRetailPriceValue);
        Assert.Equal(0.2m, entity.DiscountRate);
        var projection = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.UUID == "projection-1")
            .FirstAsync();
        Assert.Equal(6.5m, projection.PurchasePrice);
        Assert.Equal(12m, projection.MultiCodeRetailPrice);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_既有派生记录只同步价格并保留状态与创建审计()
    {
        await SeedAsync("200", 5m, 10m, 0.2m, DateTime.UtcNow.AddDays(-2), 6.5m, 12m);
        await SeedProjectedSetCodeAsync();
        var createdAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var projection = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.UUID == "projection-1")
            .FirstAsync();
        projection.DiscountRate = 0.25m;
        projection.IsAutoPricing = true;
        projection.IsSpecialProduct = true;
        projection.IsActive = false;
        projection.CreatedAt = createdAt;
        projection.CreatedBy = "original-creator";
        await _db.Updateable(projection).ExecuteCommandAsync();
        var service = CreateService();

        var preview = await service.SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );
        Assert.Equal("confirmation_required", preview.Data!.Status);
        var result = await service.SyncWarehousePriceAsync(
            StorePriceUuid,
            CreateConfirmRequest(6.5m, 12m, 6.5m, 10m, 0.2m),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        var latest = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.UUID == "projection-1")
            .FirstAsync();
        Assert.Equal(6.5m, latest.PurchasePrice);
        Assert.Equal(12m, latest.MultiCodeRetailPrice);
        Assert.Equal(0.25m, latest.DiscountRate);
        Assert.True(latest.IsAutoPricing);
        Assert.True(latest.IsSpecialProduct);
        Assert.False(latest.IsActive);
        Assert.Equal(createdAt, latest.CreatedAt);
        Assert.Equal("original-creator", latest.CreatedBy);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_价格已一致时幂等且不刷新UpdatedAt()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("200", 6.5m, 12m, 0.8m, updatedAt, 6.504m, 12.004m);

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.True(result.Success);
        Assert.Equal("synced", result.Data!.Status);
        Assert.False(result.Data.PurchaseUpdated);
        Assert.False(result.Data.RetailUpdated);
        Assert.False(result.Data.RetailConfirmationRequired);
        Assert.Equal(updatedAt, (await GetStorePriceAsync()).UpdatedAt);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_确认时分店快照变化返回冲突且零写入()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("200", 5m, 10m, 0.8m, updatedAt, 6.5m, 12m);

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            CreateConfirmRequest(6.5m, 12m, 5m, 9m, 0.8m),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.False(result.Success);
        Assert.Equal("PRICE_VERSION_CONFLICT", result.ErrorCode);
        Assert.Equal(10m, result.Data!.StorePrice!.RetailPrice);
        var entity = await GetStorePriceAsync();
        Assert.Equal(5m, entity.PurchasePrice);
        Assert.Equal(10m, entity.StoreRetailPriceValue);
        Assert.Equal(updatedAt, entity.UpdatedAt);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_确认时仓库快照变化返回冲突且零写入()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("200", 5m, 10m, 0.8m, updatedAt, 6.5m, 12m);

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            CreateConfirmRequest(6.5m, 11m, 5m, 10m, 0.8m),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.False(result.Success);
        Assert.Equal("PRICE_VERSION_CONFLICT", result.ErrorCode);
        Assert.Equal(12m, result.Data!.WarehouseRetailPrice);
        Assert.Equal(updatedAt, (await GetStorePriceAsync()).UpdatedAt);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_拒绝无权分店且不写入()
    {
        var updatedAt = DateTime.UtcNow.AddDays(-2);
        await SeedAsync("200", 5m, 10m, 0.8m, updatedAt, 6.5m, 12m);

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto(),
            "tester",
            new List<string> { "other-store" }
        );

        Assert.False(result.Success);
        Assert.Contains("无权", result.Message);
        Assert.Equal(updatedAt, (await GetStorePriceAsync()).UpdatedAt);
    }

    [Fact]
    public async Task SyncWarehousePriceAsync_派生记录更新失败时回滚主价格()
    {
        await SeedAsync("200", 5m, 10m, 0.8m, DateTime.UtcNow.AddDays(-2), 5m, 12m);
        await SeedProjectedSetCodeAsync();
        await _db.Ado.ExecuteCommandAsync(
            """
            CREATE TRIGGER fail_projection_update
            BEFORE UPDATE ON StoreMultiCodeProduct
            BEGIN
                SELECT RAISE(ABORT, 'projection failure');
            END;
            """
        );

        var result = await CreateService().SyncWarehousePriceAsync(
            StorePriceUuid,
            CreateConfirmRequest(5m, 12m, 5m, 10m, 0.8m),
            "tester",
            new List<string> { StoreCode }
        );

        Assert.False(result.Success);
        Assert.Equal("仓库价格对账失败，请稍后重试", result.Message);
        Assert.DoesNotContain("projection failure", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10m, (await GetStorePriceAsync()).StoreRetailPriceValue);
        var projection = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.UUID == "projection-1")
            .FirstAsync();
        Assert.Equal(10m, projection.MultiCodeRetailPrice);
    }

    [Fact]
    public async Task Controller_快照冲突返回409并携带最新数据()
    {
        var latest = new SyncStoreProductWarehousePriceResultDto
        {
            Status = "confirmation_required",
            WarehouseRetailPrice = 12m,
        };
        var conflict = new ApiResponse<SyncStoreProductWarehousePriceResultDto>
        {
            Success = false,
            ErrorCode = "PRICE_VERSION_CONFLICT",
            Message = "价格已变化",
            Data = latest,
        };
        var service = new Mock<IStoreProductMaintenanceReactService>();
        service
            .Setup(value => value.SyncWarehousePriceAsync(
                StorePriceUuid,
                It.IsAny<SyncStoreProductWarehousePriceRequestDto>(),
                It.IsAny<string>(),
                null
            ))
            .ReturnsAsync(conflict);
        var controller = new ReactStoreProductMaintenanceController(
            service.Object,
            Mock.Of<IDeviceRegistrationService>(),
            Mock.Of<IMapper>(),
            CreateSqlSugarContext(_db),
            NullLogger<ReactStoreProductMaintenanceController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.Name, "admin"),
                            new Claim(ClaimTypes.Role, "Admin"),
                        },
                        "test"
                    )),
                },
            },
        };

        var response = await controller.SyncWarehousePrice(
            StorePriceUuid,
            new SyncStoreProductWarehousePriceRequestDto { ConfirmRetailPrice = true }
        );

        var objectResult = Assert.IsType<ConflictObjectResult>(response);
        var body = Assert.IsType<ApiResponse<SyncStoreProductWarehousePriceResultDto>>(
            objectResult.Value
        );
        Assert.Equal("PRICE_VERSION_CONFLICT", body.ErrorCode);
        Assert.Same(latest, body.Data);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private StoreProductMaintenanceReactService CreateService()
    {
        return new StoreProductMaintenanceReactService(
            CreateSqlSugarContext(_db),
            NullLogger<StoreProductMaintenanceReactService>.Instance,
            _autoPricingService.Object,
            _cache
        );
    }

    private async Task SeedAsync(
        string localSupplierCode,
        decimal? storePurchasePrice,
        decimal? storeRetailPrice,
        decimal? discountRate,
        DateTime updatedAt,
        decimal? warehousePurchasePrice = null,
        decimal? warehouseRetailPrice = null
    )
    {
        await _db.Insertable(new Store
        {
            StoreGUID = "store-guid-1",
            StoreCode = StoreCode,
            StoreName = "测试分店",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new Product
        {
            UUID = "product-uuid-1",
            ProductCode = ProductCode,
            ProductName = "测试商品",
            LocalSupplierCode = localSupplierCode,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = StorePriceUuid,
            StoreCode = StoreCode,
            ProductCode = ProductCode,
            StoreProductCode = $"{StoreCode}-{ProductCode}",
            SupplierCode = localSupplierCode,
            PurchasePrice = storePurchasePrice,
            StoreRetailPriceValue = storeRetailPrice,
            DiscountRate = discountRate,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = updatedAt.AddDays(-1),
            UpdatedAt = updatedAt,
            UpdatedBy = "seed",
        }).ExecuteCommandAsync();

        if (warehousePurchasePrice.HasValue || warehouseRetailPrice.HasValue)
        {
            await InsertWarehousePriceAsync(warehousePurchasePrice, warehouseRetailPrice);
        }
    }

    private Task<int> InsertWarehousePriceAsync(
        decimal? warehousePurchasePrice,
        decimal? warehouseRetailPrice
    )
    {
        return _db.Insertable(new WarehouseProduct
        {
            ProductCode = ProductCode,
            ImportPrice = warehousePurchasePrice,
            OEMPrice = warehouseRetailPrice,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProjectedSetCodeAsync()
    {
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = "set-1",
            ProductCode = ProductCode,
            SetProductCode = "set-product-1",
            SetItemNumber = "set-item-1",
            SetBarcode = "set-barcode-1",
            SetType = 2,
            SetQuantity = 1,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = "projection-1",
            StoreCode = StoreCode,
            ProductCode = ProductCode,
            MultiCodeProductCode = "set-product-1",
            StoreMultiCodeProductCode = $"{StoreCode}set-product-1",
            MultiBarcode = "set-barcode-1",
            PurchasePrice = 5m,
            MultiCodeRetailPrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private Task<StoreRetailPrice> GetStorePriceAsync()
    {
        return _db.Queryable<StoreRetailPrice>()
            .Where(x => x.UUID == StorePriceUuid)
            .FirstAsync();
    }

    private static SyncStoreProductWarehousePriceRequestDto CreateConfirmRequest(
        decimal? warehousePurchasePrice,
        decimal? warehouseRetailPrice,
        decimal? storePurchasePrice,
        decimal? storeRetailPrice,
        decimal? discountRate
    ) => new()
    {
        ConfirmRetailPrice = true,
        ExpectedWarehousePurchasePrice = warehousePurchasePrice,
        ExpectedWarehouseRetailPrice = warehouseRetailPrice,
        ExpectedStorePurchasePrice = storePurchasePrice,
        ExpectedStoreRetailPrice = storeRetailPrice,
        ExpectedDiscountRate = discountRate,
    };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
