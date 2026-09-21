using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
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
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreProductMaintenanceEnsureStorePriceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Mock<IAutoPricingService> _pricing = new();
    private readonly Mock<IProductMaintenanceHqProjectionWriter> _hq = new();

    public StoreProductMaintenanceEnsureStorePriceTests()
    {
        _connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(Product), typeof(Store), typeof(HBLocalSupplier), typeof(StoreRetailPrice),
            typeof(ProductSetCode), typeof(StoreMultiCodeProduct), typeof(StoreClearancePrice),
            typeof(WarehouseProduct)
        );
        _pricing.Setup(x => x.FindStrategyForPriceAsync(
                It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<string?>()
            ))
            .ReturnsAsync((PricingStrategy?)null);
        _pricing.Setup(x => x.CalculateRetailPrice(
                It.IsAny<decimal>(), It.IsAny<PricingStrategy?>()
            ))
            .Returns<decimal, PricingStrategy?>((cost, _) => cost * 2m);
        _hq.Setup(x => x.EnqueueAsync(
                It.IsAny<ISqlSugarClient>(),
                It.IsAny<ProductMaintenanceHqMutationRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new ProductHqSyncOperationStatusDto
            {
                OperationId = "op-1",
                Status = ProductHqSyncOutboxStatuses.Pending,
                Retryable = true,
            });
    }

    [Fact]
    public async Task Missing_row_is_created_and_second_call_is_idempotent()
    {
        await Seed("P-1", "S-1", 3m, 9m);
        var service = CreateService();
        Assert.True((await service.EnsureStorePriceAsync("P-1", "S-1", "tester", new() { "S-1" })).Success);
        Assert.True((await service.EnsureStorePriceAsync("P-1", "S-1", "tester", new() { "S-1" })).Success);

        var rows = await _db.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-1" && x.StoreCode == "S-1" && !x.IsDeleted)
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("S-1P-1", row.StoreProductCode);
        Assert.Equal(3m, row.PurchasePrice);
        Assert.Equal(9m, row.StoreRetailPriceValue);
        Assert.Equal(1m, row.DiscountRate);
        _hq.Verify(x => x.EnqueueAsync(
            It.IsAny<ISqlSugarClient>(),
            It.IsAny<ProductMaintenanceHqMutationRequest>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Existing_row_is_not_overwritten_and_deleted_history_is_kept()
    {
        await Seed("P-2", "S-1", 3m, 9m);
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "old", StoreCode = "S-1", ProductCode = "P-2", StoreProductCode = "legacy",
            PurchasePrice = 7m, StoreRetailPriceValue = 8m, DiscountRate = .5m, IsActive = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "deleted", StoreCode = "S-1", ProductCode = "P-2",
            IsDeleted = true, PurchasePrice = 99m,
        }).ExecuteCommandAsync();
        Assert.True((await CreateService().EnsureStorePriceAsync("P-2", "S-1", "tester", new() { "S-1" })).Success);
        var rows = await _db.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-2" && x.StoreCode == "S-1")
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("legacy", rows.Single(x => !x.IsDeleted).StoreProductCode);
        Assert.Equal(7m, rows.Single(x => !x.IsDeleted).PurchasePrice);
        Assert.True(rows.Single(x => x.UUID == "deleted").IsDeleted);
        VerifyNoHqEnqueue();
    }

    [Fact]
    public async Task Empty_store_unauthorized_scope_and_missing_product_do_not_write()
    {
        await Seed("P-3", "S-1", null, null);
        var service = CreateService();
        Assert.False((await service.EnsureStorePriceAsync("P-3", null, "tester", new() { "S-1" })).Success);
        Assert.False((await service.EnsureStorePriceAsync("P-3", "S-2", "tester", new() { "S-1" })).Success);
        Assert.False((await service.EnsureStorePriceAsync("P-3", "missing", "tester", new() { "missing" })).Success);
        Assert.False((await service.EnsureStorePriceAsync("missing", "S-1", "tester", new() { "S-1" })).Success);
        Assert.Empty(await _db.Queryable<StoreRetailPrice>().ToListAsync());
        VerifyNoHqEnqueue();
    }

    [Fact]
    public async Task Auto_pricing_uses_calculator_only_for_positive_cost_and_null_stays_null()
    {
        await Seed("P-4", "S-1", 4m, 99m, true);
        Assert.True((await CreateService().EnsureStorePriceAsync("P-4", "S-1", "tester", new() { "S-1" })).Success);
        Assert.Equal(8m,
            (await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-4"))
            .StoreRetailPriceValue);
        await Seed("P-5", "S-1", null, null, true);
        Assert.True((await CreateService().EnsureStorePriceAsync("P-5", "S-1", "tester", new() { "S-1" })).Success);
        Assert.Null(
            (await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-5"))
            .StoreRetailPriceValue);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cost_below_auto_pricing_minimum_keeps_product_retail_price(bool autoPricing)
    {
        await Seed("P-low", "S-1", .05m, 7m, autoPricing);
        _pricing.Setup(x => x.CalculateRetailPrice(
                It.Is<decimal>(cost => cost < .1m),
                It.IsAny<PricingStrategy?>()
            ))
            .Throws(new InvalidOperationException("低于自动定价下限"));

        var result = await CreateService().EnsureStorePriceAsync("P-low", "S-1", "tester", new() { "S-1" });

        Assert.True(result.Success);
        var row = await _db.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-low");
        Assert.Equal(7m, row.StoreRetailPriceValue);
        Assert.Equal(autoPricing, row.IsAutoPricing);
        _pricing.Verify(x => x.CalculateRetailPrice(
            It.Is<decimal>(cost => cost < .1m),
            It.IsAny<PricingStrategy?>()
        ), Times.Never);

        var evaluation = await CreateService().EvaluateAutoPricingAsync(
            new EvaluateStoreProductAutoPricingDto
            {
                ProductCode = "P-low", StoreCode = "S-1", ForceAutoPricing = true,
            },
            new() { "S-1" }
        );
        Assert.True(evaluation.Success);
        Assert.NotNull(evaluation.Data);
        Assert.False(evaluation.Data.HasValidPurchasePrice);
        Assert.False(evaluation.Data.ShouldUpdate);
        _pricing.Verify(x => x.CalculateRetailPrice(
            It.Is<decimal>(cost => cost < .1m),
            It.IsAny<PricingStrategy?>()
        ), Times.Never);
    }

    [Fact]
    public async Task Hq_enqueue_failure_rolls_back_insert()
    {
        await Seed("P-rollback", "S-1", 3m, 9m);
        _hq.Setup(x => x.EnqueueAsync(
                It.IsAny<ISqlSugarClient>(),
                It.IsAny<ProductMaintenanceHqMutationRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var result = await CreateService().EnsureStorePriceAsync(
            "P-rollback", "S-1", "tester", new() { "S-1" }
        );

        Assert.False(result.Success);
        Assert.Empty(await _db.Queryable<StoreRetailPrice>().ToListAsync());
        _hq.Verify(x => x.EnqueueAsync(
            It.IsAny<ISqlSugarClient>(),
            It.IsAny<ProductMaintenanceHqMutationRequest>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task Duplicate_non_deleted_rows_are_rejected_without_mutation()
    {
        await Seed("P-duplicate", "S-1", 3m, 9m);
        await _db.Insertable(new[]
        {
            new StoreRetailPrice
            {
                UUID = "first", StoreCode = "S-1", ProductCode = "P-duplicate", PurchasePrice = 5m,
            },
            new StoreRetailPrice
            {
                UUID = "second", StoreCode = "S-1", ProductCode = "P-duplicate", PurchasePrice = 6m,
            },
        }).ExecuteCommandAsync();

        var result = await CreateService().EnsureStorePriceAsync(
            "P-duplicate", "S-1", "tester", new() { "S-1" }
        );

        Assert.False(result.Success);
        var rows = await _db.Queryable<StoreRetailPrice>().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(5m, rows.Single(x => x.UUID == "first").PurchasePrice);
        Assert.Equal(6m, rows.Single(x => x.UUID == "second").PurchasePrice);
        VerifyNoHqEnqueue();
    }

    [Fact]
    public async Task Soft_deleted_only_row_keeps_history_and_creates_new_active_row()
    {
        await Seed("P-deleted-price", "S-1", 3m, 9m);
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "deleted", StoreCode = "S-1", ProductCode = "P-deleted-price",
            PurchasePrice = 99m, IsDeleted = true,
        }).ExecuteCommandAsync();

        var result = await CreateService().EnsureStorePriceAsync(
            "P-deleted-price", "S-1", "tester", new() { "S-1" }
        );

        Assert.True(result.Success);
        var rows = await _db.Queryable<StoreRetailPrice>().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows.Single(x => x.UUID == "deleted").IsDeleted);
        Assert.Equal(99m, rows.Single(x => x.UUID == "deleted").PurchasePrice);
        Assert.Equal(3m, rows.Single(x => !x.IsDeleted).PurchasePrice);
        _hq.Verify(x => x.EnqueueAsync(
            It.IsAny<ISqlSugarClient>(),
            It.IsAny<ProductMaintenanceHqMutationRequest>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Theory]
    [InlineData("inactive-store")]
    [InlineData("deleted-store")]
    public async Task Inactive_or_deleted_store_cannot_receive_a_new_price(string storeCode)
    {
        await Seed("P-store", "S-1", 3m, 9m);
        await _db.Insertable(new Store
        {
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = storeCode != "inactive-store",
            IsDeleted = storeCode == "deleted-store",
        }).ExecuteCommandAsync();

        var result = await CreateService().EnsureStorePriceAsync(
            "P-store", storeCode, "tester", new() { storeCode }
        );

        Assert.False(result.Success);
        Assert.Empty(await _db.Queryable<StoreRetailPrice>().ToListAsync());
        VerifyNoHqEnqueue();
    }

    [Fact]
    public async Task Deleted_product_cannot_receive_a_new_price()
    {
        await Seed("P-deleted", "S-1", 3m, 9m);
        await _db.Updateable<Product>()
            .SetColumns(x => x.IsDeleted == true)
            .Where(x => x.ProductCode == "P-deleted")
            .ExecuteCommandAsync();

        var result = await CreateService().EnsureStorePriceAsync(
            "P-deleted", "S-1", "tester", new() { "S-1" }
        );

        Assert.False(result.Success);
        Assert.Empty(await _db.Queryable<StoreRetailPrice>().ToListAsync());
        VerifyNoHqEnqueue();
    }

    [Fact]
    public async Task Only_requested_store_uses_product_defaults_and_child_rows_remain_unchanged()
    {
        await Seed("P-scope", "S-1", 3m, 9m);
        await _db.Insertable(new Store
        {
            StoreCode = "S-2", StoreName = "S-2", IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "other-store", StoreCode = "S-2", ProductCode = "P-scope",
            PurchasePrice = 30m, StoreRetailPriceValue = 90m,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = "child", StoreCode = "S-1", ProductCode = "P-scope",
            MultiCodeProductCode = "P-scope-child", PurchasePrice = 21m,
            MultiCodeRetailPrice = 44m, IsActive = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = "set-child", ProductCode = "P-scope", SetProductCode = "P-scope-child",
            SetItemNumber = "set-child", SetPurchasePrice = 11m, SetRetailPrice = 22m,
        }).ExecuteCommandAsync();
        ProductMaintenanceHqMutationRequest? enqueued = null;
        _hq.Setup(x => x.EnqueueAsync(
                It.IsAny<ISqlSugarClient>(),
                It.IsAny<ProductMaintenanceHqMutationRequest>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<ISqlSugarClient, ProductMaintenanceHqMutationRequest, CancellationToken>(
                (_, request, _) => enqueued = request
            )
            .ReturnsAsync(new ProductHqSyncOperationStatusDto
            {
                OperationId = "op-1", Status = ProductHqSyncOutboxStatuses.Pending, Retryable = true,
            });

        var result = await CreateService().EnsureStorePriceAsync(
            "P-scope", "S-1", "tester", new() { "S-1", "S-2" }
        );

        Assert.True(result.Success);
        var rows = await _db.Queryable<StoreRetailPrice>().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(3m, rows.Single(x => x.StoreCode == "S-1").PurchasePrice);
        Assert.Equal(9m, rows.Single(x => x.StoreCode == "S-1").StoreRetailPriceValue);
        Assert.Equal(30m, rows.Single(x => x.StoreCode == "S-2").PurchasePrice);
        Assert.Equal(90m, rows.Single(x => x.StoreCode == "S-2").StoreRetailPriceValue);

        var child = Assert.Single(await _db.Queryable<StoreMultiCodeProduct>().ToListAsync());
        Assert.Equal(21m, child.PurchasePrice);
        Assert.Equal(44m, child.MultiCodeRetailPrice);
        Assert.False(child.IsActive);
        var setChild = Assert.Single(await _db.Queryable<ProductSetCode>().ToListAsync());
        Assert.Equal(11m, setChild.SetPurchasePrice);
        Assert.Equal(22m, setChild.SetRetailPrice);

        Assert.NotNull(enqueued);
        Assert.Equal(ProductMaintenanceHqOperationKinds.StorePriceUpdated, enqueued.OperationKind);
        Assert.Equal("P-scope", enqueued.ProductCode);
        Assert.Equal(new[] { "S-1" }, enqueued.TargetStoreCodes);
        Assert.Equal(new[] { "S-1", "S-2" }, enqueued.AuthorizedStoreCodes);
        Assert.Equal(new[]
        {
            ProductMaintenanceHqFieldMasks.StorePurchasePrice,
            ProductMaintenanceHqFieldMasks.StoreRetailPrice,
            ProductMaintenanceHqFieldMasks.StoreDiscountRate,
            ProductMaintenanceHqFieldMasks.StoreAutoPricing,
            ProductMaintenanceHqFieldMasks.StoreSpecialProduct,
            ProductMaintenanceHqFieldMasks.StoreActive,
        }, enqueued.FieldMask);
        Assert.DoesNotContain(ProductMaintenanceHqFieldMasks.StoreMultiCodes, enqueued.FieldMask);
        Assert.DoesNotContain(ProductMaintenanceHqFieldMasks.ProductSetCodes, enqueued.FieldMask);
    }

    [Fact]
    public async Task Controller_denies_edit_permission_before_service_call()
    {
        var service = new Mock<IStoreProductMaintenanceReactService>(MockBehavior.Strict);
        var auth = new Mock<IAuthorizationService>(MockBehavior.Strict);
        auth.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), null, Permissions.StoreProducts.Edit)).ReturnsAsync(AuthorizationResult.Failed());
        var controller = new ReactStoreProductMaintenanceController(service.Object, Mock.Of<IDeviceRegistrationService>(), Mock.Of<IMapper>(), Context(_db), NullLogger<ReactStoreProductMaintenanceController>.Instance, auth.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "manager"), new Claim(ClaimTypes.Role, "Manager") }, "test")) } } };
        Assert.IsType<ForbidResult>(await controller.EnsureStorePrice("P-1", "S-1"));
        service.VerifyNoOtherCalls();
    }

    private StoreProductMaintenanceReactService CreateService() => new(
        Context(_db),
        NullLogger<StoreProductMaintenanceReactService>.Instance,
        _pricing.Object,
        _cache,
        Mock.Of<IWarehouseProductChangeHistoryService>(),
        Mock.Of<ICurrentUserService>(),
        _hq.Object
    );

    private static SqlSugarContext Context(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private async Task Seed(string product, string store, decimal? purchase, decimal? retail, bool auto = false)
    {
        await _db.Insertable(new Product
        {
            ProductCode = product,
            ProductName = product,
            LocalSupplierCode = "SUP",
            PurchasePrice = purchase,
            RetailPrice = retail,
            IsAutoPricing = auto,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        if (!await _db.Queryable<Store>().AnyAsync(x => x.StoreCode == store))
        {
            await _db.Insertable(new Store
            {
                StoreCode = store, StoreName = store, IsActive = true, IsDeleted = false,
            }).ExecuteCommandAsync();
        }
    }

    private void VerifyNoHqEnqueue() => _hq.Verify(x => x.EnqueueAsync(
        It.IsAny<ISqlSugarClient>(),
        It.IsAny<ProductMaintenanceHqMutationRequest>(),
        It.IsAny<CancellationToken>()
    ), Times.Never);

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }
}
