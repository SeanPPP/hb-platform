using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

internal sealed class TestLogger<T> : ILogger<T>
    , ITestLoggerSink
{
    private readonly object _syncRoot = new();
    private readonly List<TestLogEntry> _entries = new();

    public IReadOnlyList<TestLogEntry> Entries
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        lock (_syncRoot)
        {
            _entries.Add(new TestLogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}

internal sealed record TestLogEntry(LogLevel LogLevel, string Message, Exception? Exception);

internal sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    public void Dispose() { }
}

internal interface ITestLoggerSink
{
    IReadOnlyList<TestLogEntry> Entries { get; }
}

internal static class StoreOrderCacheWarmerTestFactory
{
    public static IStoreOrderCacheWarmer Create(
        IStoreOrderReactService service,
        IMemoryCache cache,
        out ITestLoggerSink loggerSink
    )
    {
        var warmerType = typeof(IStoreOrderCacheWarmer).Assembly.GetType(
            "BlazorApp.Api.Cache.StoreOrderCacheWarmer",
            throwOnError: true
        )!;
        var loggerType = typeof(TestLogger<>).MakeGenericType(warmerType);
        loggerSink = (ITestLoggerSink)Activator.CreateInstance(loggerType)!;

        return (IStoreOrderCacheWarmer)
            Activator.CreateInstance(warmerType, service, loggerSink, cache)!;
    }
}

public class ReactStoreOrderAuthorizationTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public ReactStoreOrderAuthorizationTests()
    {
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        _sqliteConnection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(typeof(WareHouseOrder));
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    [Fact]
    public void ProductCacheKey_IgnoresStoreCodeForSameProductFilters()
    {
        StoreOrderCacheKeys.ClearActiveKeys();

        var warehouseStoreKey = StoreOrderCacheKeys.Products(
            new StoreOrderFilterDto
            {
                StoreCode = "1006",
                PageNumber = 1,
                PageSize = 18,
                SortBy = "Default",
            }
        );
        var brisbaneStoreKey = StoreOrderCacheKeys.Products(
            new StoreOrderFilterDto
            {
                StoreCode = "1004",
                PageNumber = 1,
                PageSize = 18,
                SortBy = "Default",
            }
        );

        Assert.Equal(warehouseStoreKey, brisbaneStoreKey);
    }

    [Fact]
    public async Task ProductCacheClear_KeepsBothWebAndExpoHomePageKeys()
    {
        StoreOrderCacheKeys.ClearActiveKeys();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var warmer = StoreOrderCacheWarmerTestFactory.Create(service.Object, cache, out _);
        var webHomeKey = StoreOrderCacheKeys.GetHomePageCacheKey(50);
        var expoHomeKey = StoreOrderCacheKeys.GetHomePageCacheKey(18);
        var webWarmUpKey = StoreOrderCacheKeys.GetHomePageWarmUpCacheKey(50);
        var expoWarmUpKey = StoreOrderCacheKeys.GetHomePageWarmUpCacheKey(18);
        var customKey = StoreOrderCacheKeys.Products(
            new StoreOrderFilterDto
            {
                PageNumber = 2,
                PageSize = 18,
                SortBy = "Default",
            }
        );

        cache.Set(webHomeKey, "web");
        cache.Set(expoHomeKey, "expo");
        cache.Set(webWarmUpKey, "web-warm-up");
        cache.Set(expoWarmUpKey, "expo-warm-up");
        cache.Set(customKey, "custom");

        await warmer.ClearCacheAsync();

        Assert.True(cache.TryGetValue(webHomeKey, out _));
        Assert.True(cache.TryGetValue(expoHomeKey, out _));
        Assert.True(cache.TryGetValue(webWarmUpKey, out _));
        Assert.True(cache.TryGetValue(expoWarmUpKey, out _));
        Assert.False(cache.TryGetValue(customKey, out _));
    }

    [Fact]
    public async Task WarmUpHomePageAsync_同时写入轻量预热键和正常首页缓存键()
    {
        StoreOrderCacheKeys.ClearActiveKeys();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetPagedListAsync(It.IsAny<StoreOrderFilterDto>()))
            .Returns<StoreOrderFilterDto>(filter =>
                Task.FromResult(
                    new PagedListReactDto<StoreOrderProductDto>
                    {
                        Items = new List<StoreOrderProductDto>
                        {
                            new() { ProductCode = $"P-{filter.PageSize}" },
                        },
                        Total = filter.PageSize + 1000,
                        PageNumber = 1,
                        PageSize = filter.PageSize,
                    }
                )
            );

        var warmer = StoreOrderCacheWarmerTestFactory.Create(service.Object, cache, out _);

        await warmer.WarmUpHomePageAsync();

        var normalWebKey = StoreOrderCacheKeys.GetHomePageCacheKey(50);
        var normalExpoKey = StoreOrderCacheKeys.GetHomePageCacheKey(18);
        var warmWebKey = StoreOrderCacheKeys.GetHomePageWarmUpCacheKey(50);
        var warmExpoKey = StoreOrderCacheKeys.GetHomePageWarmUpCacheKey(18);

        Assert.True(
            cache.TryGetValue(normalWebKey, out PagedListReactDto<StoreOrderProductDto>? normalWeb)
        );
        Assert.True(
            cache.TryGetValue(normalExpoKey, out PagedListReactDto<StoreOrderProductDto>? normalExpo)
        );
        Assert.True(cache.TryGetValue(warmWebKey, out _));
        Assert.True(cache.TryGetValue(warmExpoKey, out _));
        Assert.Equal(1050, normalWeb!.Total);
        Assert.Equal(1018, normalExpo!.Total);
    }

    [Fact]
    public async Task WarmUpHomePageAsync_已有预热运行时_应跳过并记录警告()
    {
        StoreOrderCacheKeys.ClearActiveKeys();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var firstCallEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;

        service
            .Setup(item => item.GetPagedListAsync(It.IsAny<StoreOrderFilterDto>()))
            .Returns<StoreOrderFilterDto>(async filter =>
            {
                var currentCount = Interlocked.Increment(ref invocationCount);
                if (currentCount == 1)
                {
                    firstCallEntered.TrySetResult();
                    await releaseFirstCall.Task;
                }

                return new PagedListReactDto<StoreOrderProductDto>
                {
                    Items = new List<StoreOrderProductDto>
                    {
                        new() { ProductCode = $"P-{filter.PageSize}" },
                    },
                    Total = 1,
                    PageNumber = 1,
                    PageSize = filter.PageSize,
                };
            });

        var warmer = StoreOrderCacheWarmerTestFactory.Create(service.Object, cache, out var logger);

        var firstWarmUpTask = warmer.WarmUpHomePageAsync();
        await firstCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await warmer.WarmUpHomePageAsync().WaitAsync(TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, Volatile.Read(ref invocationCount));
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Warning
                && entry.Message.Contains("已有首页商品列表缓存预热正在运行", StringComparison.Ordinal)
        );

        releaseFirstCall.TrySetResult();
        await firstWarmUpTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WarmUpHomePageAsync_预热查询已取消时_应记录警告且不向外抛出()
    {
        StoreOrderCacheKeys.ClearActiveKeys();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        service
            .Setup(item => item.GetPagedListAsync(It.IsAny<StoreOrderFilterDto>()))
            .Returns(Task.FromCanceled<PagedListReactDto<StoreOrderProductDto>>(cancellationSource.Token));

        var warmer = StoreOrderCacheWarmerTestFactory.Create(service.Object, cache, out var logger);

        await warmer.WarmUpHomePageAsync();

        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Warning
                && entry.Message.Contains("首页商品列表缓存预热已取消", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Controller_RequiresAuthorization_AndNoLongerAllowsAnonymous()
    {
        var controllerAttributes = typeof(ReactStoreOrderController).GetCustomAttributes(true);
        var controllerAuthorize = Assert.Single(controllerAttributes.OfType<AuthorizeAttribute>());

        Assert.DoesNotContain(controllerAttributes, attribute => attribute is AllowAnonymousAttribute);
        Assert.Null(controllerAuthorize.Policy);

        var usedBranchesMethod = typeof(ReactStoreOrderController).GetMethod(
            nameof(ReactStoreOrderController.GetUsedBranches)
        );
        Assert.NotNull(usedBranchesMethod);
        Assert.DoesNotContain(
            usedBranchesMethod!.GetCustomAttributes(true),
            attribute => attribute is AllowAnonymousAttribute
        );
    }

    [Fact]
    public async Task GetProducts_ForbidsUserWithoutOrderFrontPermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(),
            CreateScopeService()
        );

        var result = await controller.GetProducts(new StoreOrderFilterDto { StoreCode = "S001" });

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetProducts_ForbidsScopedUserWhenStoreCodeIsMissing()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            CreateScopeService(),
            new[] { "Order" }
        );

        var result = await controller.GetProducts(new StoreOrderFilterDto());

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetProducts_AllowsOrderRolePermissions_AndCallsService()
    {
        var filter = new StoreOrderFilterDto { StoreCode = "S001" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetPagedListAsync(filter))
            .ReturnsAsync(new PagedListReactDto<StoreOrderProductDto>());

        var controller = CreateController(
            service,
            CreateAuthorizationService(
                Permissions.OrderFront.View,
                Permissions.Orders.View,
                Permissions.Orders.Create
            ),
            CreateScopeService(),
            new[] { "Order" }
        );

        var result = await controller.GetProducts(filter);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.GetPagedListAsync(filter), Times.Once);
    }

    [Fact]
    public async Task GetProducts_AllowsAssignedStoreWhenManageScopeRejectsOrderUser()
    {
        var filter = new StoreOrderFilterDto { StoreCode = "1024" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetPagedListAsync(filter))
            .ReturnsAsync(new PagedListReactDto<StoreOrderProductDto>());
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto>
                    {
                        new() { StoreCode = "1024", StoreName = "Bankstown" },
                    }
                )
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var result = await controller.GetProducts(filter);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.GetPagedListAsync(filter), Times.Once);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task GetProducts_ForbidsUnassignedStoreWhenManageScopeRejectsOrderUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto>
                    {
                        new() { StoreCode = "1006", StoreName = "HB WARE HOUSE" },
                    }
                )
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var result = await controller.GetProducts(new StoreOrderFilterDto { StoreCode = "1024" });

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task ScanLookupProducts_AllowsAssignedStoreForOrderReadUser()
    {
        var request = new StoreOrderScanLookupRequestDto { Barcode = "930001", StoreCode = "1024" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.ScanLookupProductsAsync(request))
            .ReturnsAsync(
                ApiResponse<StoreOrderScanLookupResultDto>.OK(
                    new StoreOrderScanLookupResultDto { Barcode = "930001" }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = "1024" } }
                )
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var result = await controller.ScanLookupProducts(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.ScanLookupProductsAsync(request), Times.Once);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task GetActiveCartSummary_AllowsAssignedStoreForOrderReadUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetActiveCartSummaryAsync("1024"))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartDto?>.OK(
                    new StoreOrderCartDto
                    {
                        OrderGUID = "cart-summary-1",
                        StoreCode = "1024",
                        TotalQuantity = 3,
                    }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = "1024" } }
                )
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var result = await controller.GetActiveCartSummary("1024");

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.GetActiveCartSummaryAsync("1024"), Times.Once);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task ScanLookupProducts_ForbidsMissingStoreCodeForScopedUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            CreateScopeService(),
            new[] { "Order" }
        );

        var result = await controller.ScanLookupProducts(
            new StoreOrderScanLookupRequestDto { Barcode = "930001" }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddToCart_AllowsAssignedStoreForOrderCreateUser()
    {
        var request = new AddToCartRequestDto
        {
            StoreCode = "1024",
            ProductCode = "P001",
            Quantity = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var cart = new StoreOrderCartDto
        {
            OrderGUID = "cart-1",
            StoreCode = "1024",
            TotalQuantity = 1,
            Items = new List<StoreOrderCartItemDto>
            {
                new()
                {
                    DetailGUID = "D001",
                    ProductCode = "P001",
                    Quantity = 1,
                },
            },
        };
        service
            .Setup(item => item.AddToCartAsync(request))
            .ReturnsAsync(ApiResponse<StoreOrderCartDto?>.OK(cart));
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = "1024" } }
                )
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var result = await controller.AddToCart(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<StoreOrderCartDto>(
            ok.Value!.GetType().GetProperty("data")!.GetValue(ok.Value)
        );
        Assert.Equal("cart-1", data.OrderGUID);
        service.Verify(item => item.AddToCartAsync(request), Times.Once);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task AddToCart_WhenPreorderPending_AllowsSavingNormalOrderDraft()
    {
        var request = new AddToCartRequestDto
        {
            StoreCode = "1024",
            ProductCode = "P001",
            Quantity = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.AddToCartAsync(request))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartDto?>.OK(
                    new StoreOrderCartDto { OrderGUID = "draft-cart", StoreCode = "1024" }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = "1024" } }
                )
            );
        var preorderGateService = new Mock<IPreorderGateService>(MockBehavior.Strict);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "Order" },
            userService: userService,
            preorderGateService: preorderGateService
        );

        var result = await controller.AddToCart(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.AddToCartAsync(request), Times.Once);
        preorderGateService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ScanLookupProducts_ThenAddToCart_AllowsWarehouseManageOrdersWithoutStoreScope()
    {
        var storeCode = "1024";
        var lookupRequest = new StoreOrderScanLookupRequestDto
        {
            Barcode = "930001",
            StoreCode = storeCode,
        };
        var addRequest = new AddToCartRequestDto
        {
            StoreCode = storeCode,
            ProductCode = "P001",
            Quantity = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.ScanLookupProductsAsync(lookupRequest))
            .ReturnsAsync(
                ApiResponse<StoreOrderScanLookupResultDto>.OK(
                    new StoreOrderScanLookupResultDto
                    {
                        Barcode = "930001",
                        Items = new List<StoreOrderProductDto>
                        {
                            new() { ProductCode = addRequest.ProductCode },
                        },
                    }
                )
            );
        service
            .Setup(item => item.AddToCartMutationAsync(addRequest))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartMutationResultDto?>.OK(
                    new StoreOrderCartMutationResultDto
                    {
                        ProductCode = addRequest.ProductCode,
                        Summary = new StoreOrderCartMutationSummaryDto
                        {
                            OrderGUID = "cart-1",
                            StoreCode = storeCode,
                            TotalQuantity = 1,
                            TotalSku = 1,
                        },
                    }
                )
            );
        var authorizationService = CreateAuthorizationService(Permissions.Warehouse.ManageOrders);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync(storeCode)).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = storeCode } }
                )
            );

        var controller = CreateController(
            service,
            authorizationService,
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var lookupResult = await controller.ScanLookupProducts(lookupRequest);
        controller.Request.Path = "/api/react/v1/store-order/cart/scan-add";
        var addResult = await controller.AddToCart(addRequest);

        Assert.IsType<OkObjectResult>(lookupResult);
        Assert.IsType<OkObjectResult>(addResult);
        service.Verify(item => item.ScanLookupProductsAsync(lookupRequest), Times.Once);
        service.Verify(item => item.AddToCartMutationAsync(addRequest), Times.Once);
        // 权限 policy 与门店无关，lookup 已查过 Warehouse.ManageOrders，scan-add 复用缓存。
        authorizationService.Verify(
            item =>
                item.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<string>()
                ),
            Times.Exactly(4)
        );
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(storeCode), Times.Never);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Never);
    }

    [Fact]
    public async Task AddToCart_ReusesPermissionAndAssignedStoreCacheForSameScanFlow()
    {
        var request = new AddToCartRequestDto
        {
            StoreCode = "1024",
            ProductCode = "P001",
            Quantity = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.AddToCartAsync(It.IsAny<AddToCartRequestDto>()))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartDto?>.OK(
                    new StoreOrderCartDto
                    {
                        OrderGUID = "cart-1",
                        StoreCode = request.StoreCode,
                        TotalQuantity = 1,
                    }
                )
            );
        var authorizationService = CreateAuthorizationService(Permissions.Orders.Create);
        var scopeService = CreateScopeService();
        scopeService
            .Setup(item => item.CanAccessStoreCodeAsync(request.StoreCode))
            .ReturnsAsync(false);
        var userService = CreateAssignedStoreUserService(request.StoreCode);

        var controller = CreateController(
            service,
            authorizationService,
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var firstResult = await controller.AddToCart(request);
        var secondResult = await controller.AddToCart(request);

        Assert.IsType<OkObjectResult>(firstResult);
        Assert.IsType<OkObjectResult>(secondResult);
        authorizationService.Verify(
            item =>
                item.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<string>()
                ),
            Times.Exactly(4)
        );
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(request.StoreCode), Times.Once);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
        service.Verify(item => item.AddToCartAsync(It.IsAny<AddToCartRequestDto>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AddToCart_DoesNotReuseAssignedStoreCacheAcrossStores()
    {
        using var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.AddToCartAsync(It.IsAny<AddToCartRequestDto>()))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartDto?>.OK(
                    new StoreOrderCartDto
                    {
                        OrderGUID = "cart-1",
                        TotalQuantity = 1,
                    }
                )
            );
        var authorizationService = CreateAuthorizationService(Permissions.Orders.Create);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("2048")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto>
                    {
                        new() { StoreCode = "1024", IsPrimary = false },
                        new() { StoreCode = "2048", IsPrimary = false },
                    }
                )
            );
        var controller = CreateController(
            service,
            authorizationService,
            scopeService,
            new[] { "Order" },
            userService: userService,
            cache: sharedCache
        );

        // 共享同一个 MemoryCache 时，不同门店仍必须分别判断 assigned-store scope。
        var firstResult = await controller.AddToCart(new AddToCartRequestDto
        {
            StoreCode = "1024",
            ProductCode = "P001",
            Quantity = 1,
        });
        var secondResult = await controller.AddToCart(new AddToCartRequestDto
        {
            StoreCode = "2048",
            ProductCode = "P002",
            Quantity = 1,
        });

        Assert.IsType<OkObjectResult>(firstResult);
        Assert.IsType<OkObjectResult>(secondResult);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("1024"), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("2048"), Times.Once);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Exactly(2));
        service.Verify(item => item.AddToCartAsync(It.IsAny<AddToCartRequestDto>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ScanLookupProducts_ThenAddToCart_AllowsWarehouseStaffMobileScanFlow()
    {
        var storeCode = "1024";
        var lookupRequest = new StoreOrderScanLookupRequestDto
        {
            Barcode = "930001",
            StoreCode = storeCode,
        };
        var addRequest = new AddToCartRequestDto
        {
            StoreCode = storeCode,
            ProductCode = "P001",
            Quantity = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.ScanLookupProductsAsync(lookupRequest))
            .ReturnsAsync(
                ApiResponse<StoreOrderScanLookupResultDto>.OK(
                    new StoreOrderScanLookupResultDto
                    {
                        Barcode = "930001",
                        Items = new List<StoreOrderProductDto>
                        {
                            new() { ProductCode = addRequest.ProductCode },
                        },
                    }
                )
            );
        service
            .Setup(item => item.AddToCartMutationAsync(addRequest))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartMutationResultDto?>.OK(
                    new StoreOrderCartMutationResultDto
                    {
                        ProductCode = addRequest.ProductCode,
                        Summary = new StoreOrderCartMutationSummaryDto
                        {
                            OrderGUID = "cart-1",
                            StoreCode = storeCode,
                            TotalQuantity = 1,
                            TotalSku = 1,
                        },
                    }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync(storeCode)).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage, Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );
        controller.Request.Headers["X-Scan-Trace-Id"] = "scan-add-1";

        var lookupResult = await controller.ScanLookupProducts(lookupRequest);
        controller.Request.Path = "/api/react/v1/store-order/cart/scan-add";
        var addResult = await controller.AddToCart(addRequest);

        Assert.IsType<OkObjectResult>(lookupResult);
        Assert.IsType<OkObjectResult>(addResult);
        service.Verify(item => item.ScanLookupProductsAsync(lookupRequest), Times.Once);
        service.Verify(item => item.AddToCartMutationAsync(addRequest), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(storeCode), Times.Never);
    }

    [Fact]
    public async Task UpdateCartItem_AllowsWarehouseStaffMobileScanFlow()
    {
        var lookupRequest = new StoreOrderScanLookupRequestDto
        {
            Barcode = "930001",
            StoreCode = "1024",
        };
        var request = new AddToCartRequestDto
        {
            StoreCode = "1024",
            ProductCode = "P001",
            Quantity = 3,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.ScanLookupProductsAsync(lookupRequest))
            .ReturnsAsync(
                ApiResponse<StoreOrderScanLookupResultDto>.OK(
                    new StoreOrderScanLookupResultDto
                    {
                        Barcode = "930001",
                        Items = new List<StoreOrderProductDto>
                        {
                            new() { ProductCode = request.ProductCode },
                        },
                    }
                )
            );
        service
            .Setup(item => item.UpdateCartItemMutationAsync(request))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartMutationResultDto?>.OK(
                    new StoreOrderCartMutationResultDto
                    {
                        ProductCode = request.ProductCode,
                        Summary = new StoreOrderCartMutationSummaryDto
                        {
                            OrderGUID = "cart-1",
                            StoreCode = request.StoreCode,
                            TotalQuantity = 3,
                            TotalSku = 1,
                        },
                    }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync(request.StoreCode)).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage, Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );
        controller.Request.Headers["X-Scan-Trace-Id"] = "scan-update-1";

        var lookupResult = await controller.ScanLookupProducts(lookupRequest);
        controller.Request.Path = "/api/react/v1/store-order/cart/scan-update";
        var result = await controller.UpdateCartItem(request);

        Assert.IsType<OkObjectResult>(lookupResult);
        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.ScanLookupProductsAsync(lookupRequest), Times.Once);
        service.Verify(item => item.UpdateCartItemMutationAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(request.StoreCode), Times.Never);
    }

    [Fact]
    public async Task ClearCart_AllowsWarehouseStaffWithOrderCreatePermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.ClearCartAsync("1024"))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartDto?>.OK(
                    new StoreOrderCartDto { OrderGUID = "cart-1", StoreCode = "1024" }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage, Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.ClearCart(new ClearCartRequestDto { StoreCode = "1024" });

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.ClearCartAsync("1024"), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("1024"), Times.Never);
    }

    [Fact]
    public async Task RemoveFromCart_AllowsWarehouseStaffWithOrderCreatePermission()
    {
        var request = new RemoveFromCartRequestDto { StoreCode = "1024", DetailGUID = "detail-1" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service.Setup(item => item.RemoveFromCartAsync(request)).ReturnsAsync(ApiResponse<bool>.OK(true));
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage, Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.RemoveFromCart(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.RemoveFromCartAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("1024"), Times.Never);
    }

    [Fact]
    public async Task SubmitOrder_AllowsWarehouseStaffWithOrderCreatePermission()
    {
        var request = new SubmitStoreOrderRequestDto { StoreCode = "S001" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service.Setup(item => item.SubmitOrderAsync(request)).ReturnsAsync(ApiResponse<bool>.OK(true));
        var scopeService = CreateScopeService();
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.SubmitOrder(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.SubmitOrderAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitOrder_WhenPreorderPending_ReturnsConflictWithoutSubmittingNormalOrder()
    {
        var request = new SubmitStoreOrderRequestDto { StoreCode = "1024" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(true);
        var preorderGateService = new Mock<IPreorderGateService>(MockBehavior.Strict);
        preorderGateService
            .Setup(item => item.CheckAsync("1024"))
            .ReturnsAsync(new PreorderGateResult { IsBlocked = true, PendingCount = 1 });
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "Order" },
            preorderGateService: preorderGateService
        );

        var result = await controller.SubmitOrder(request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PreorderGateResult>>(conflict.Value);
        Assert.Equal("PREORDER_REQUIRED", response.ErrorCode);
        Assert.Equal(1, response.Data?.PendingCount);
        service.VerifyNoOtherCalls();
        preorderGateService.Verify(item => item.CheckAsync("1024"), Times.Once);
    }

    [Fact]
    public async Task CreateOrder_WhenPreorderPending_ReturnsConflictWithoutCreatingNormalOrder()
    {
        var request = new CreateStoreOrderDto { StoreCode = "1024" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(true);
        var preorderGateService = new Mock<IPreorderGateService>(MockBehavior.Strict);
        preorderGateService
            .Setup(item => item.CheckAsync("1024"))
            .ReturnsAsync(new PreorderGateResult { IsBlocked = true, PendingCount = 1 });
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "Order" },
            preorderGateService: preorderGateService
        );

        var result = await controller.CreateOrder(request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PreorderGateResult>>(conflict.Value);
        Assert.Equal("PREORDER_REQUIRED", response.ErrorCode);
        service.VerifyNoOtherCalls();
        preorderGateService.Verify(item => item.CheckAsync("1024"), Times.Once);
    }

    [Fact]
    public async Task CopyOrder_WhenPreorderPending_ReturnsConflictWithoutCopyingNormalOrder()
    {
        var request = new CopyOrderDto
        {
            SourceOrderGUID = "source-order",
            TargetStoreCode = "1024",
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("source-order")).ReturnsAsync(true);
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(true);
        var preorderGateService = new Mock<IPreorderGateService>(MockBehavior.Strict);
        preorderGateService
            .Setup(item => item.CheckAsync("1024"))
            .ReturnsAsync(new PreorderGateResult { IsBlocked = true, PendingCount = 1 });
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "Order" },
            preorderGateService: preorderGateService
        );

        var result = await controller.CopyOrder(request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PreorderGateResult>>(conflict.Value);
        Assert.Equal("PREORDER_REQUIRED", response.ErrorCode);
        service.VerifyNoOtherCalls();
        preorderGateService.Verify(item => item.CheckAsync("1024"), Times.Once);
    }

    [Fact]
    public void PreorderBypassFlag_CannotBeInjectedFromClientJson()
    {
        const string json = """{"storeCode":"1024","targetStoreCode":"1024","sourceOrderGUID":"source","bypassPreorderGate":true}""";

        var submit = JsonSerializer.Deserialize<SubmitStoreOrderRequestDto>(json);
        var create = JsonSerializer.Deserialize<CreateStoreOrderDto>(json);
        var copy = JsonSerializer.Deserialize<CopyOrderDto>(json);

        Assert.False(Assert.IsType<SubmitStoreOrderRequestDto>(submit).BypassPreorderGate);
        Assert.False(Assert.IsType<CreateStoreOrderDto>(create).BypassPreorderGate);
        Assert.False(Assert.IsType<CopyOrderDto>(copy).BypassPreorderGate);
    }

    [Fact]
    public async Task GetActiveCartSummary_AllowsWarehouseStaffWithOrderCreatePermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetActiveCartSummaryAsync("1024"))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartDto?>.OK(
                    new StoreOrderCartDto { OrderGUID = "cart-warehouse-1", StoreCode = "1024" }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.GetActiveCartSummary("1024");

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.GetActiveCartSummaryAsync("1024"), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("1024"), Times.Never);
    }

    [Fact]
    public async Task GetDynamicData_AllowsWarehouseStaffWithOrderCreatePermission()
    {
        var request = new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "1024",
            ProductCodes = new List<string> { "P001" },
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetProductsDynamicDataAsync(request))
            .ReturnsAsync(ApiResponse<List<StoreOrderDynamicDataDto>>.OK(new List<StoreOrderDynamicDataDto>()));
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.GetDynamicData(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.GetProductsDynamicDataAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("1024"), Times.Never);
    }

    [Fact]
    public async Task AddToCart_ForbidsWarehouseStaffLegacyManageOnScanRoute()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            CreateScopeService(),
            new[] { "WarehouseStaff" }
        );
        controller.Request.Path = "/api/react/v1/store-order/cart/scan-add";

        var result = await controller.AddToCart(
            new AddToCartRequestDto { StoreCode = "S001", ProductCode = "P001", Quantity = 1 }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddToCart_ForbidsOrderReadOnlyUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            CreateScopeService(),
            new[] { "Order" }
        );

        var result = await controller.AddToCart(
            new AddToCartRequestDto { StoreCode = "S001", ProductCode = "P001", Quantity = 1 }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddToCart_ForbidsUnassignedStoreForOrderCreateUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = "1006" } }
                )
            );
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "Order" },
            userService: userService
        );

        var result = await controller.AddToCart(
            new AddToCartRequestDto { StoreCode = "1024", ProductCode = "P001", Quantity = 1 }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task GetOrderList_ForbidsWhenRequestedStoreIsOutsideCurrentUserScope()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = "S001" } }
                )
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            scopeService,
            userService: userService
        );

        var result = await controller.GetOrderList(
            new StoreOrderListFilterDto { StoreCode = "S999" }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task GetImportPriceVariance_ForbidsWarehouseStaffLegacyManagePermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            CreateScopeService(),
            new[] { "WarehouseStaff" }
        );

        var result = await controller.GetImportPriceVariance(
            new StoreOrderImportPriceVarianceQueryDto()
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetImportPriceVariance_AllowsWarehouseManageOrdersPermission()
    {
        var request = new StoreOrderImportPriceVarianceQueryDto();
        var response = ApiResponse<StoreOrderImportPriceVarianceResultDto>.OK(
            new StoreOrderImportPriceVarianceResultDto
            {
                Items = new List<StoreOrderImportPriceVarianceItemDto>(),
                Summary = new StoreOrderImportPriceVarianceSummaryDto(),
                SupplierSummaries = new List<StoreOrderImportPriceVarianceSupplierSummaryDto>(),
            }
        );
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service.Setup(item => item.GetImportPriceVarianceAsync(request)).ReturnsAsync(response);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.GetImportPriceVariance(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response.Data, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
    }

    [Fact]
    public async Task GetImportPriceVarianceDetails_ForbidsWarehouseStaffLegacyManagePermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            CreateScopeService(),
            new[] { "WarehouseStaff" }
        );

        var result = await controller.GetImportPriceVarianceDetails(
            new StoreOrderImportPriceVarianceDetailQueryDto { ProductCode = "P1" }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetImportPriceVarianceDetails_AllowsWarehouseManageOrdersPermission()
    {
        var request = new StoreOrderImportPriceVarianceDetailQueryDto { ProductCode = "P1" };
        var response = ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>.OK(
            new StoreOrderImportPriceVarianceDetailResultDto
            {
                Items = new List<StoreOrderImportPriceVarianceDetailItemDto>(),
                Summary = new StoreOrderImportPriceVarianceSummaryDto(),
            }
        );
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service.Setup(item => item.GetImportPriceVarianceDetailsAsync(request)).ReturnsAsync(response);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.GetImportPriceVarianceDetails(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response.Data, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceDomesticPrice_ForbidsOrderReadOnlyUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceDomesticPrice(
            new StoreOrderImportPriceVarianceDomesticPriceUpdateDto
            {
                ProductCode = "P1",
                DomesticPrice = 12.3m,
            }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceDomesticPrice_AllowsWarehouseManageOrdersPermission()
    {
        var request = new StoreOrderImportPriceVarianceDomesticPriceUpdateDto
        {
            ProductCode = "P1",
            DomesticPrice = 12.3m,
        };
        var response = ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>.OK(
            new StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto
            {
                ProductCode = "P1",
                DomesticPrice = 12.3m,
            }
        );
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateImportPriceVarianceDomesticPriceAsync(request))
            .ReturnsAsync(response);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceDomesticPrice(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response.Data, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceDomesticPrice_HidesInternalServiceException()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.UpdateImportPriceVarianceDomesticPriceAsync(
                    It.IsAny<StoreOrderImportPriceVarianceDomesticPriceUpdateDto>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("sensitive sql detail"));
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceDomesticPrice(
            new StoreOrderImportPriceVarianceDomesticPriceUpdateDto
            {
                ProductCode = "P1",
                DomesticPrice = 12.3m,
            }
        );

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var message = error.Value?.GetType().GetProperty("message")?.GetValue(error.Value) as string;
        Assert.Equal("服务器内部错误", message);
        Assert.DoesNotContain("sensitive", message);
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceWarehouseImportPrice_ForbidsOrderReadOnlyUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceWarehouseImportPrice(
            new StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto
            {
                ProductCode = "P1",
                WarehouseImportPrice = 12.3m,
            }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceWarehouseImportPrice_AllowsWarehouseManageOrdersPermission()
    {
        var request = new StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto
        {
            ProductCode = "P1",
            WarehouseImportPrice = 12.3m,
        };
        var response = ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>.OK(
            new StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto
            {
                ProductCode = "P1",
                WarehouseImportPrice = 12.3m,
            }
        );
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateImportPriceVarianceWarehouseImportPriceAsync(request))
            .ReturnsAsync(response);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceWarehouseImportPrice(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response.Data, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceWarehouseImportPrice_HidesInternalServiceException()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.UpdateImportPriceVarianceWarehouseImportPriceAsync(
                    It.IsAny<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("sensitive sql detail"));
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceWarehouseImportPrice(
            new StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto
            {
                ProductCode = "P1",
                WarehouseImportPrice = 12.3m,
            }
        );

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var message = error.Value?.GetType().GetProperty("message")?.GetValue(error.Value) as string;
        Assert.Equal("服务器内部错误", message);
        Assert.DoesNotContain("sensitive", message);
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceWarehouseImportPriceBatch_ForbidsOrderReadOnlyUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceWarehouseImportPriceBatch(
            new StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto
            {
                ProductCodes = new List<string> { "P1" },
                WarehouseImportPrice = 12.3m,
            }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceWarehouseImportPriceBatch_AllowsWarehouseManageOrdersPermission()
    {
        var request = new StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto
        {
            ProductCodes = new List<string> { "P1", "P2" },
            WarehouseImportPrice = 12.3m,
        };
        var response = ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>.OK(
            new StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto
            {
                UpdatedCount = 2,
                ProductCodes = new List<string> { "P1", "P2" },
                WarehouseImportPrice = 12.3m,
            }
        );
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateImportPriceVarianceWarehouseImportPriceBatchAsync(request))
            .ReturnsAsync(response);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceWarehouseImportPriceBatch(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response.Data, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
    }

    [Fact]
    public async Task UpdateImportPriceVarianceWarehouseImportPriceBatch_HidesInternalServiceException()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.UpdateImportPriceVarianceWarehouseImportPriceBatchAsync(
                    It.IsAny<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto>()
                )
            )
            .ThrowsAsync(new InvalidOperationException("sensitive sql detail"));
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.UpdateImportPriceVarianceWarehouseImportPriceBatch(
            new StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto
            {
                ProductCodes = new List<string> { "P1" },
                WarehouseImportPrice = 12.3m,
            }
        );

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var message = error.Value?.GetType().GetProperty("message")?.GetValue(error.Value) as string;
        Assert.Equal("服务器内部错误", message);
        Assert.DoesNotContain("sensitive", message);
        service.VerifyAll();
    }

    [Fact]
    public async Task GetOrderList_AllowsAssignedStoreForOrderViewUser()
    {
        var expected = new PagedListReactDto<StoreOrderListItemDto>
        {
            Items = new List<StoreOrderListItemDto>(),
            Total = 0,
            PageNumber = 1,
            PageSize = 20,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.GetOrderListAsync(
                    It.Is<StoreOrderListFilterDto>(filter => filter.StoreCode == "1024")
                )
            )
            .ReturnsAsync(expected);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto> { new() { StoreCode = "1024", IsPrimary = false } }
                )
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            scopeService,
            userService: userService
        );

        var result = await controller.GetOrderList(
            new StoreOrderListFilterDto { StoreCode = "1024" }
        );

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task GetOrderList_AllowsLegacyWarehouseManagePermissionForAllStores()
    {
        var expected = new PagedListReactDto<StoreOrderListItemDto>
        {
            Items = new List<StoreOrderListItemDto>(),
            Total = 0,
            PageNumber = 1,
            PageSize = 20,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.GetOrderListAsync(
                    It.Is<StoreOrderListFilterDto>(filter =>
                        string.IsNullOrWhiteSpace(filter.StoreCode)
                    )
                )
            )
            .ReturnsAsync(expected);

        var scopeService = CreateScopeService();
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.GetOrderList(new StoreOrderListFilterDto());

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrder_ForbidsOrderFrontOnlyUser()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            CreateScopeService()
        );

        var result = await controller.CreateOrder(new CreateStoreOrderDto { StoreCode = "S001" });

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateOrder_AllowsWarehouseStaffWithOrderCreatePermissionWithoutStoreScope()
    {
        var request = new CreateStoreOrderDto { StoreCode = "S999" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.CreateOrderAsync(request))
            .ReturnsAsync(ApiResponse<string>.OK("created-order"));
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Create),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.CreateOrder(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.CreateOrderAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
    }

    [Fact]
    public async Task CreateOrder_ForbidsWarehouseStaffLegacyManageWithoutOrderCreatePermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            CreateScopeService(),
            new[] { "WarehouseStaff" }
        );

        var result = await controller.CreateOrder(new CreateStoreOrderDto { StoreCode = "S001" });

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddOrderLine_AllowsWarehouseStaffWithOrderEditPermissionWithoutOrderScope()
    {
        var request = new AddOrderLineDto
        {
            OrderGUID = "order-outside",
            ProductCode = "P001",
            Quantity = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.AddOrderLineAsync(request))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-outside")).ReturnsAsync(false);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.AddOrderLine(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.AddOrderLineAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessOrderAsync("order-outside"), Times.Never);
    }

    [Fact]
    public async Task AddOrderLine_WhenPreorderPending_AllowsEditingNormalOrderDraft()
    {
        const string orderGuid = "order-preorder-blocked";
        var request = new AddOrderLineDto
        {
            OrderGUID = orderGuid,
            ProductCode = "P001",
            Quantity = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service.Setup(item => item.AddOrderLineAsync(request)).ReturnsAsync(ApiResponse<bool>.OK(true));
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync(orderGuid)).ReturnsAsync(true);
        var preorderGateService = new Mock<IPreorderGateService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService,
            new[] { "Order" },
            preorderGateService: preorderGateService
        );

        var result = await controller.AddOrderLine(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.AddOrderLineAsync(request), Times.Once);
        preorderGateService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PasteReplaceOrderLines_WhenPreorderPending_AllowsEditingNormalOrderDraft()
    {
        var request = new PasteReplaceOrderLinesDto
        {
            OrderGUID = "order-preorder-draft",
            Items = new List<ProductQuantityDto>
            {
                new() { ProductCode = "P001", Quantity = 2 },
            },
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.PasteReplaceOrderLinesAsync(request))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        var scopeService = CreateScopeService();
        scopeService
            .Setup(item => item.CanAccessOrderAsync("order-preorder-draft"))
            .ReturnsAsync(true);
        var preorderGateService = new Mock<IPreorderGateService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService,
            new[] { "Order" },
            preorderGateService: preorderGateService
        );

        var result = await controller.PasteReplaceOrderLines(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.PasteReplaceOrderLinesAsync(request), Times.Once);
        preorderGateService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WarehouseStaffLegacyManage_ForbidsStoreOrderWriteActions()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        Assert.IsType<ForbidResult>(
            await controller.CreateOrder(new CreateStoreOrderDto { StoreCode = "S001" })
        );
        Assert.IsType<ForbidResult>(
            await controller.CopyOrder(
                new CopyOrderDto
                {
                    SourceOrderGUID = "order-1",
                    TargetStoreCode = "S001",
                }
            )
        );
        Assert.IsType<ForbidResult>(await controller.DeleteOrder("order-1"));
        Assert.IsType<ForbidResult>(
            await controller.UpdateOrderStatus(
                new UpdateOrderStatusDto { OrderGUID = "order-1", NewStatus = 2 }
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.SubmitOrder(new SubmitStoreOrderRequestDto { StoreCode = "S001" })
        );
        Assert.IsType<ForbidResult>(
            await controller.AddOrderLine(
                new AddOrderLineDto { OrderGUID = "order-1", ProductCode = "P001", Quantity = 1 }
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.UpdateOrderOutboundDate(
                new UpdateOrderOutboundDateDto
                {
                    OrderGuid = "order-1",
                    OutboundDate = new DateTime(2026, 6, 7),
                    CompleteOrder = true,
                }
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.AddToCart(
                new AddToCartRequestDto { StoreCode = "S001", ProductCode = "P001", Quantity = 1 }
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.UpdateCartItem(
                new AddToCartRequestDto { StoreCode = "S001", ProductCode = "P001", Quantity = 2 }
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.RemoveFromCart(
                new RemoveFromCartRequestDto { StoreCode = "S001", DetailGUID = "detail-1" }
            )
        );
        Assert.IsType<ForbidResult>(
            await controller.ClearCart(new ClearCartRequestDto { StoreCode = "S001" })
        );
        Assert.IsType<ForbidResult>(
            await controller.SyncMissingOrders(new SyncMissingOrdersRequestDto())
        );

        service.VerifyNoOtherCalls();
        scopeService.Verify(item => item.CanAccessOrderAsync(It.IsAny<string>()), Times.Never);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetOrderList_AllowsWarehouseManageOrdersPermissionWhenStoreScopeRejects()
    {
        var expected = new PagedListReactDto<StoreOrderListItemDto>
        {
            Items = new List<StoreOrderListItemDto>(),
            Total = 0,
            PageNumber = 1,
            PageSize = 20,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.GetOrderListAsync(
                    It.Is<StoreOrderListFilterDto>(filter => filter.StoreCode == "S999")
                )
            )
            .ReturnsAsync(expected);

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.GetOrderList(
            new StoreOrderListFilterDto { StoreCode = "S999" }
        );

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value?.GetType().GetProperty("data")?.GetValue(ok.Value));
        service.VerifyAll();
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
    }

    [Fact]
    public async Task CreateOrder_AllowsWarehouseManageOrdersPermissionWhenStoreScopeRejects()
    {
        var request = new CreateStoreOrderDto { StoreCode = "S999" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.CreateOrderAsync(request))
            .ReturnsAsync(ApiResponse<string>.OK("created-order"));

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.CreateOrder(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.CreateOrderAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
    }

    [Fact]
    public async Task CopyOrder_AllowsWarehouseManageOrdersPermissionWhenOrderAndStoreScopeReject()
    {
        var request = new CopyOrderDto
        {
            SourceOrderGUID = "order-outside",
            TargetStoreCode = "S999",
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.CopyOrderAsync(request))
            .ReturnsAsync(
                ApiResponse<CopyOrderResultDto>.OK(
                    new CopyOrderResultDto { OrderGUID = "copy-order", OrderNo = "SO-1" }
                )
            );

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-outside")).ReturnsAsync(false);
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.CopyOrder(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.CopyOrderAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessOrderAsync("order-outside"), Times.Never);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
    }

    [Fact]
    public async Task UpdateOrderStatus_AllowsWarehouseManageOrdersPermissionWhenOrderScopeRejects()
    {
        var request = new UpdateOrderStatusDto
        {
            OrderGUID = "order-outside",
            NewStatus = 2,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateOrderStatusAsync("order-outside", 2, true))
            .ReturnsAsync(ApiResponse<bool>.OK(true));

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-outside")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.UpdateOrderStatus(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.UpdateOrderStatusAsync("order-outside", 2, true), Times.Once);
        scopeService.Verify(item => item.CanAccessOrderAsync("order-outside"), Times.Never);
    }

    [Fact]
    public async Task UpdateOrderStatus_普通分店不携带仓库绕过标记并返回正式提交冲突()
    {
        var request = new UpdateOrderStatusDto
        {
            OrderGUID = "order-scoped-draft",
            NewStatus = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateOrderStatusAsync("order-scoped-draft", 1, false))
            .ReturnsAsync(new ApiResponse<bool>
            {
                Success = false,
                ErrorCode = "PREORDER_SUBMIT_ENDPOINT_REQUIRED",
                Message = "草稿订单必须通过正式提交接口提交",
            });

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-scoped-draft")).ReturnsAsync(true);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.UpdateOrderStatus(request);

        Assert.IsType<ConflictObjectResult>(result);
        service.Verify(
            item => item.UpdateOrderStatusAsync("order-scoped-draft", 1, false),
            Times.Once
        );
    }

    [Fact]
    public async Task BatchUpdateOrderStatus_仓库管理权限携带绕过标记()
    {
        var request = new BatchUpdateOrderStatusDto
        {
            OrderGUIDs = new List<string> { "order-batch-a", "order-batch-b" },
            NewStatus = 1,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.BatchUpdateOrderStatusAsync(request.OrderGUIDs, 1, true))
            .ReturnsAsync(ApiResponse<int>.OK(2));

        var scopeService = CreateScopeService();
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.BatchUpdateOrderStatus(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(
            item => item.BatchUpdateOrderStatusAsync(request.OrderGUIDs, 1, true),
            Times.Once
        );
        scopeService.Verify(
            item => item.CanAccessOrderAsync(It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task SyncMissingOrders_ForbidsWithoutWarehouseManageOrdersPermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Edit),
            CreateScopeService()
        );

        var result = await controller.SyncMissingOrders(
            new SyncMissingOrdersRequestDto { StoreCode = "S001" }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncMissingOrders_AllowsWarehouseManageOrdersOutsideRequestedStoreScope()
    {
        var request = new SyncMissingOrdersRequestDto
        {
            StoreCodes = new List<string> { "S001", "S999" },
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.SyncMissingOrdersFromHqAsync(request))
            .ReturnsAsync(new SyncMissingOrdersResultDto { Success = true });

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.SyncMissingOrders(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.SyncMissingOrdersFromHqAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
    }

    [Fact]
    public async Task SyncMissingOrders_AllowsWarehouseManageOrdersLegacyStoreCodeWhenStoreCodesAreBlank()
    {
        var request = new SyncMissingOrdersRequestDto
        {
            StoreCode = "S999",
            StoreCodes = new List<string> { " " },
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.SyncMissingOrdersFromHqAsync(request))
            .ReturnsAsync(new SyncMissingOrdersResultDto { Success = true });

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.SyncMissingOrders(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.SyncMissingOrdersFromHqAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
    }

    [Fact]
    public async Task SyncMissingOrders_AllowsLegacyStoreCode_AndPassesWholeRequestToService()
    {
        var request = new SyncMissingOrdersRequestDto { StoreCode = "S001" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.SyncMissingOrdersFromHqAsync(request))
            .ReturnsAsync(new SyncMissingOrdersResultDto { Success = true });

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService()
        );

        var result = await controller.SyncMissingOrders(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.SyncMissingOrdersFromHqAsync(request), Times.Once);
    }

    [Fact]
    public async Task SyncMissingOrders_AllowsLegacyWarehouseManagePermission()
    {
        var request = new SyncMissingOrdersRequestDto();
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.SyncMissingOrdersFromHqAsync(request))
            .ReturnsAsync(new SyncMissingOrdersResultDto { Success = true });

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            CreateScopeService()
        );

        var result = await controller.SyncMissingOrders(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.SyncMissingOrdersFromHqAsync(request), Times.Once);
    }

    [Fact]
    public async Task SyncMissingOrders_WhenWarehouseManageOrdersAndStoreNotSelected_PassesOriginalRequest()
    {
        var request = new SyncMissingOrdersRequestDto();
        SyncMissingOrdersRequestDto? capturedRequest = null;
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>()))
            .Callback<SyncMissingOrdersRequestDto?>(request => capturedRequest = request)
            .ReturnsAsync(new SyncMissingOrdersResultDto { Success = true });

        var scopeService = CreateScopeService();
        scopeService
            .Setup(item => item.GetScopeAsync())
            .ReturnsAsync(
                new CurrentUserManageableStoreScope
                {
                    IsAllowed = true,
                    IsAuthenticated = true,
                    IsAdmin = false,
                    StoreCodes = new List<string> { "S001", "S002" },
                }
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.SyncMissingOrders(request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Same(request, capturedRequest);
        Assert.Null(capturedRequest!.StoreCodes);
        scopeService.Verify(item => item.GetScopeAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateSyncMissingOrdersJob_ForbidsWithoutWarehouseSyncPermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            CreateScopeService(),
            jobService: jobService
        );

        var result = await controller.CreateSyncMissingOrdersJob(
            new SyncMissingOrdersRequestDto { StoreCode = "S001" }
        );

        Assert.IsType<ForbidResult>(result);
        jobService.VerifyNoOtherCalls();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateSyncMissingOrdersJob_AllowsLegacyWarehouseManagePermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(item =>
                item.StartJobAsync(
                    "user-1",
                    It.Is<SyncMissingOrdersRequestDto>(request => request.StoreCode == "S001"),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new StoreOrderSyncJobDto { JobId = "job-1", Status = "Running" });

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            CreateScopeService(),
            jobService: jobService
        );

        var result = await controller.CreateSyncMissingOrdersJob(
            new SyncMissingOrdersRequestDto { StoreCode = "S001" }
        );

        Assert.IsType<OkObjectResult>(result);
        jobService.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateSyncMissingOrdersJob_AllowsWarehouseManageOrdersOutsideRequestedStoreScope()
    {
        var request = new SyncMissingOrdersRequestDto
        {
            StoreCodes = new List<string> { "S001", "S999" },
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(item =>
                item.StartJobAsync("user-1", request, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new StoreOrderSyncJobDto { JobId = "job-1", Status = "Running" });

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" },
            jobService
        );

        var result = await controller.CreateSyncMissingOrdersJob(request);

        Assert.IsType<OkObjectResult>(result);
        jobService.Verify(
            item => item.StartJobAsync("user-1", request, It.IsAny<CancellationToken>()),
            Times.Once
        );
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateSyncMissingOrdersJob_WhenWarehouseManageOrdersAndStoreNotSelected_PassesOriginalRequest()
    {
        var request = new SyncMissingOrdersRequestDto();
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        SyncMissingOrdersRequestDto? capturedRequest = null;
        string? capturedUserId = null;

        jobService
            .Setup(
                item =>
                    item.StartJobAsync(
                        It.IsAny<string>(),
                        It.IsAny<SyncMissingOrdersRequestDto?>(),
                        It.IsAny<CancellationToken>()
                    )
            )
            .Callback<string, SyncMissingOrdersRequestDto?, CancellationToken>(
                (userId, request, _) =>
                {
                    capturedUserId = userId;
                    capturedRequest = request;
                }
            )
            .ReturnsAsync(
                new StoreOrderSyncJobDto
                {
                    JobId = "job-1",
                    Status = StoreOrderSyncJobStatusConstants.Running,
                    StoreCodes = new List<string> { "S001", "S002" },
                }
            );

        var scopeService = CreateScopeService();
        scopeService
            .Setup(item => item.GetScopeAsync())
            .ReturnsAsync(
                new CurrentUserManageableStoreScope
                {
                    IsAllowed = true,
                    IsAuthenticated = true,
                    IsAdmin = false,
                    StoreCodes = new List<string> { "S001", "S002" },
                }
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" },
            jobService
        );

        var result = await controller.CreateSyncMissingOrdersJob(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("user-1", capturedUserId);
        Assert.Same(request, capturedRequest);
        Assert.Null(capturedRequest!.StoreCodes);
        scopeService.Verify(item => item.GetScopeAsync(), Times.Never);
        jobService.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSyncMissingOrdersJob_ForbidsWithoutWarehouseSyncPermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            CreateScopeService(),
            jobService: jobService
        );

        var result = await controller.GetSyncMissingOrdersJob("job-1");

        Assert.IsType<ForbidResult>(result);
        jobService.VerifyNoOtherCalls();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSyncMissingOrdersJob_AllowsLegacyWarehouseManagePermission()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(item => item.GetJobAsync("job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreOrderSyncJobDto { JobId = "job-1", Status = "Running" });

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            CreateScopeService(),
            jobService: jobService
        );

        var result = await controller.GetSyncMissingOrdersJob("job-1");

        Assert.IsType<OkObjectResult>(result);
        jobService.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSyncMissingOrdersJob_AllowsWarehouseManageOrdersWhenJobContainsStoreOutsideCurrentUserScope()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        jobService
            .Setup(item => item.GetJobAsync("job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new StoreOrderSyncJobDto
                {
                    JobId = "job-1",
                    Status = StoreOrderSyncJobStatusConstants.Running,
                    StoreCodes = new List<string> { "S001", "S999" },
                }
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" },
            jobService
        );

        var result = await controller.GetSyncMissingOrdersJob("job-1");

        Assert.IsType<OkObjectResult>(result);
        jobService.VerifyAll();
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSyncMissingOrdersJob_ReturnsNotFoundWhenJobDoesNotExist()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(item => item.GetJobAsync("job-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreOrderSyncJobDto?)null);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            CreateScopeService(),
            jobService: jobService
        );

        var result = await controller.GetSyncMissingOrdersJob("job-missing");

        Assert.IsType<NotFoundObjectResult>(result);
        jobService.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStoreOrderHqSyncJob_ForbidsWithoutPermissionBeforeReadingJob()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            CreateScopeService(),
            jobService: jobService
        );

        var result = await controller.GetStoreOrderHqSyncJob("job-secret");

        Assert.IsType<ForbidResult>(result);
        jobService.VerifyNoOtherCalls();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateStoreOrderHqIncrementalSyncJob_AllowsWarehouseManageOrdersOutsideRequestedStoreScope()
    {
        var request = new StoreOrderHqSyncRequestDto
        {
            StoreCodes = new List<string> { "S001", "S999" },
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        jobService
            .Setup(item =>
                item.StartHqSyncJobAsync(
                    "user-1",
                    StoreOrderHqSyncMode.Incremental,
                    request,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new StoreOrderSyncJobDto { JobId = "hq-job-1", Status = "Running" });

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" },
            jobService
        );

        var result = await controller.CreateStoreOrderHqIncrementalSyncJob(request);

        Assert.IsType<OkObjectResult>(result);
        jobService.VerifyAll();
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStoreOrderHqSyncJob_AllowsWarehouseManageOrdersWhenIncrementalJobContainsStoreOutsideScope()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        jobService
            .Setup(item => item.GetJobAsync("hq-job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new StoreOrderSyncJobDto
                {
                    JobId = "hq-job-1",
                    Status = StoreOrderSyncJobStatusConstants.Running,
                    Mode = StoreOrderHqSyncMode.Incremental.ToString(),
                    StoreCodes = new List<string> { "S001", "S999" },
                }
            );

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" },
            jobService
        );

        var result = await controller.GetStoreOrderHqSyncJob("hq-job-1");

        Assert.IsType<OkObjectResult>(result);
        jobService.VerifyAll();
        scopeService.Verify(item => item.CanAccessStoreCodeAsync("S999"), Times.Never);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrderDetail_AllowsAuthorizedScopedUser_AndCallsService()
    {
        var service = new Mock<IStoreOrderReactService>();
        service
            .Setup(item =>
                item.GetOrderDetailAsync(
                    "order-1",
                    It.Is<StoreOrderDetailQueryDto>(query =>
                        query.PageNumber == 1
                        && query.PageSize == StoreOrderDetailQueryDto.DefaultPageSize
                    )
                )
            )
            .ReturnsAsync(
                new ApiResponse<StoreOrderDetailDto?>
                {
                    Success = true,
                    Data = new StoreOrderDetailDto
                    {
                        OrderGUID = "order-1",
                        StoreCode = "S001",
                    },
                }
            );

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-1")).ReturnsAsync(true);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            scopeService
        );

        var result = await controller.GetOrderDetail("order-1", new StoreOrderDetailQueryDto());

        var okResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.True(okResult.StatusCode is null or Microsoft.AspNetCore.Http.StatusCodes.Status200OK);
        service.Verify(
            item =>
                item.GetOrderDetailAsync(
                    "order-1",
                    It.Is<StoreOrderDetailQueryDto>(query =>
                        query.PageNumber == 1
                        && query.PageSize == StoreOrderDetailQueryDto.DefaultPageSize
                    )
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetOrderDetailFull_AllowsAuthorizedScopedUser_AndCallsFullService()
    {
        var service = new Mock<IStoreOrderReactService>();
        service
            .Setup(item => item.GetOrderDetailFullAsync("order-1"))
            .ReturnsAsync(
                new ApiResponse<StoreOrderCartDto?>
                {
                    Success = true,
                    Data = new StoreOrderCartDto { OrderGUID = "order-1", StoreCode = "S001" },
                }
            );

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-1")).ReturnsAsync(true);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            scopeService
        );

        var result = await controller.GetOrderDetailFull("order-1");

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.GetOrderDetailFullAsync("order-1"), Times.Once);
    }

    [Fact]
    public async Task GetOrderDetail_AllowsAssignedStoreOrder_WhenManageableOrderScopeFails()
    {
        await SeedOrderAsync("order-assigned", "1024");
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.GetOrderDetailAsync(
                    "order-assigned",
                    It.Is<StoreOrderDetailQueryDto>(query =>
                        query.PageNumber == 1
                        && query.PageSize == StoreOrderDetailQueryDto.DefaultPageSize
                    )
                )
            )
            .ReturnsAsync(
                new ApiResponse<StoreOrderDetailDto?>
                {
                    Success = true,
                    Data = new StoreOrderDetailDto
                    {
                        OrderGUID = "order-assigned",
                        StoreCode = "1024",
                    },
                }
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-assigned")).ReturnsAsync(false);
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = CreateAssignedStoreUserService("1024");

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            scopeService,
            userService: userService,
            dbContext: CreateSqlSugarContext(_db)
        );

        var result = await controller.GetOrderDetail(
            "order-assigned",
            new StoreOrderDetailQueryDto()
        );

        var okResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.True(okResult.StatusCode is null or Microsoft.AspNetCore.Http.StatusCodes.Status200OK);
        service.VerifyAll();
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Fact]
    public async Task GetOrderDetailFull_AllowsAssignedStoreOrder_WhenManageableOrderScopeFails()
    {
        await SeedOrderAsync("order-assigned-full", "1024");
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetOrderDetailFullAsync("order-assigned-full"))
            .ReturnsAsync(
                new ApiResponse<StoreOrderCartDto?>
                {
                    Success = true,
                    Data = new StoreOrderCartDto
                    {
                        OrderGUID = "order-assigned-full",
                        StoreCode = "1024",
                    },
                }
            );
        var scopeService = CreateScopeService();
        scopeService
            .Setup(item => item.CanAccessOrderAsync("order-assigned-full"))
            .ReturnsAsync(false);
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("1024")).ReturnsAsync(false);
        var userService = CreateAssignedStoreUserService("1024");

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            scopeService,
            userService: userService,
            dbContext: CreateSqlSugarContext(_db)
        );

        var result = await controller.GetOrderDetailFull("order-assigned-full");

        Assert.IsType<OkObjectResult>(result);
        service.VerifyAll();
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
    }

    [Theory]
    [InlineData("order-outside", "S999")]
    [InlineData("order-missing", null)]
    public async Task GetOrderDetail_ForbidsWhenAssignedStoreScopeDoesNotMatch(
        string orderGuid,
        string? storeCode
    )
    {
        if (storeCode != null)
        {
            await SeedOrderAsync(orderGuid, storeCode);
        }
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync(orderGuid)).ReturnsAsync(false);
        if (storeCode != null)
        {
            scopeService.Setup(item => item.CanAccessStoreCodeAsync(storeCode)).ReturnsAsync(false);
        }
        var userService = CreateAssignedStoreUserService("S001");

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.OrderFront.View),
            scopeService,
            userService: userService,
            dbContext: CreateSqlSugarContext(_db)
        );

        var result = await controller.GetOrderDetail(orderGuid, new StoreOrderDetailQueryDto());

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshOrderLineImportPrices_AllowsWarehouseManagerAndChecksOrderScope()
    {
        var request = new RefreshStoreOrderImportPricesDto
        {
            OrderGUID = "order-refresh",
            DetailGUIDs = new List<string> { "detail-1" },
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.RefreshOrderLineImportPricesAsync(request))
            .ReturnsAsync(
                ApiResponse<RefreshStoreOrderImportPricesResultDto>.OK(
                    new RefreshStoreOrderImportPricesResultDto { UpdatedCount = 1 }
                )
            );
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-refresh")).ReturnsAsync(true);
        var controller = CreateController(
            service,
            CreateAuthorizationService(),
            scopeService,
            new[] { "WarehouseManager" }
        );

        var result = await controller.RefreshOrderLineImportPrices(request);

        Assert.IsType<OkObjectResult>(result);
        scopeService.Verify(item => item.CanAccessOrderAsync("order-refresh"), Times.Once);
        service.Verify(item => item.RefreshOrderLineImportPricesAsync(request), Times.Once);
    }

    [Fact]
    public async Task RefreshOrderLineImportPrices_ForbidsWarehouseStaffBeforeCallingService()
    {
        var request = new RefreshStoreOrderImportPricesDto { OrderGUID = "order-refresh" };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.Manage),
            scopeService,
            new[] { "WarehouseStaff" }
        );

        var result = await controller.RefreshOrderLineImportPrices(request);

        Assert.IsType<ForbidResult>(result);
        scopeService.Verify(item => item.CanAccessOrderAsync(It.IsAny<string>()), Times.Never);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateStoreContact_RequiresEditPermissionAndScopesBeforeCallingService()
    {
        var request = new UpdateStoreOrderStoreContactDto
        {
            OrderGUID = "order-1",
            StoreCode = "S001",
            Address = "updated address",
            ContactEmail = "updated@example.com",
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateStoreContactAsync(request))
            .ReturnsAsync(
                ApiResponse<StoreOrderStoreContactDto>.OK(
                    new StoreOrderStoreContactDto
                    {
                        OrderGUID = "order-1",
                        StoreCode = "S001",
                        Address = "updated address",
                        ContactEmail = "updated@example.com",
                    }
                )
            );

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-1")).ReturnsAsync(true);
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S001")).ReturnsAsync(true);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService
        );

        var result = await controller.UpdateStoreContact(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.UpdateStoreContactAsync(request), Times.Once);
    }

    [Fact]
    public async Task UpdateOrderOutboundDate_ForbidsWhenOrderScopeRejectsBeforeCallingService()
    {
        var request = new UpdateOrderOutboundDateDto
        {
            OrderGuid = "order-outside",
            OutboundDate = new DateTime(2026, 6, 7),
            CompleteOrder = true,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-outside")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService
        );

        var result = await controller.UpdateOrderOutboundDate(request);

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateOrderOutboundDate_AllowsWarehouseManageOrdersPermissionWhenOrderScopeRejects()
    {
        var request = new UpdateOrderOutboundDateDto
        {
            OrderGuid = "order-outside",
            OutboundDate = new DateTime(2026, 6, 7),
            CompleteOrder = true,
        };
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.UpdateOrderOutboundDateAsync(request))
            .ReturnsAsync(ApiResponse<bool>.OK(true));

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-outside")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.UpdateOrderOutboundDate(request);

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.UpdateOrderOutboundDateAsync(request), Times.Once);
        scopeService.Verify(item => item.CanAccessOrderAsync("order-outside"), Times.Never);
    }

    [Fact]
    public async Task SendInvoiceEmail_UsesEditPermissionAndOrderScope()
    {
        var request = new SendStoreOrderInvoiceEmailDto
        {
            OrderGUID = "order-1",
            ToEmail = "customer@example.com",
        };
        var invoiceEmailJobService = new Mock<IStoreOrderInvoiceEmailJobService>(
            MockBehavior.Strict
        );
        invoiceEmailJobService
            .Setup(item => item.StartJobAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreOrderInvoiceEmailJobDto
            {
                JobId = "job-1",
                Status = StoreOrderInvoiceEmailJobStatusConstants.Queued,
                Message = "发票邮件发送任务已提交",
                OrderGUID = "order-1",
                ToEmail = "customer@example.com",
            });

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-1")).ReturnsAsync(true);

        var controller = CreateController(
            new Mock<IStoreOrderReactService>(MockBehavior.Strict),
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService,
            invoiceEmailJobService: invoiceEmailJobService
        );

        var result = await controller.SendInvoiceEmail(request);

        Assert.IsType<OkObjectResult>(result);
        invoiceEmailJobService.Verify(
            item => item.StartJobAsync(request, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GetInvoiceEmailJob_UsesEditPermissionAndOrderScope()
    {
        var invoiceEmailJobService = new Mock<IStoreOrderInvoiceEmailJobService>(
            MockBehavior.Strict
        );
        invoiceEmailJobService
            .Setup(item => item.GetJobAsync("job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreOrderInvoiceEmailJobDto
            {
                JobId = "job-1",
                Status = StoreOrderInvoiceEmailJobStatusConstants.Succeeded,
                Message = "发票邮件发送成功",
                OrderGUID = "order-1",
                ToEmail = "customer@example.com",
            });

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-1")).ReturnsAsync(true);

        var controller = CreateController(
            new Mock<IStoreOrderReactService>(MockBehavior.Strict),
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService,
            invoiceEmailJobService: invoiceEmailJobService
        );

        var result = await controller.GetInvoiceEmailJob("job-1");

        Assert.IsType<OkObjectResult>(result);
        invoiceEmailJobService.Verify(
            item => item.GetJobAsync("job-1", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task TranslateInvoiceEmailText_UsesEditPermissionAndOrderScope()
    {
        var request = new StoreOrderInvoiceEmailTextTranslationRequestDto
        {
            OrderGUID = "order-1",
            TargetLanguage = "en",
            Subject = "自定义主题",
            Body = "自定义正文",
        };
        var translationService = new Mock<IStoreOrderInvoiceEmailTextTranslationService>(
            MockBehavior.Strict
        );
        translationService
            .Setup(item => item.TranslateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                ApiResponse<StoreOrderInvoiceEmailTextTranslationResultDto>.OK(
                    new StoreOrderInvoiceEmailTextTranslationResultDto
                    {
                        Subject = "Custom subject",
                        Body = "Custom body",
                    }
                )
            );

        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessOrderAsync("order-1")).ReturnsAsync(true);

        var controller = CreateController(
            new Mock<IStoreOrderReactService>(MockBehavior.Strict),
            CreateAuthorizationService(Permissions.Orders.Edit),
            scopeService,
            invoiceEmailTextTranslationService: translationService
        );

        var result = await controller.TranslateInvoiceEmailText(request);

        Assert.IsType<OkObjectResult>(result);
        translationService.Verify(
            item => item.TranslateAsync(request, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    private static ReactStoreOrderController CreateController(
        Mock<IStoreOrderReactService> service,
        Mock<IAuthorizationService> authorizationService,
        Mock<ICurrentUserManageableStoreScopeService> scopeService,
        IReadOnlyCollection<string>? roleNames = null,
        Mock<IStoreOrderSyncJobService>? jobService = null,
        Mock<IStoreOrderInvoiceEmailJobService>? invoiceEmailJobService = null,
        Mock<IStoreOrderPasteReplaceJobService>? pasteReplaceJobService = null,
        Mock<IStoreOrderInvoiceEmailTextTranslationService>? invoiceEmailTextTranslationService = null,
        Mock<IUserService>? userService = null,
        SqlSugarContext? dbContext = null,
        IMemoryCache? cache = null,
        Mock<IPreorderGateService>? preorderGateService = null
    )
    {
        var effectivePreorderGateService =
            preorderGateService ?? new Mock<IPreorderGateService>(MockBehavior.Strict);
        if (preorderGateService == null)
        {
            effectivePreorderGateService
                .Setup(item => item.CheckAsync(It.IsAny<string>()))
                .ReturnsAsync(new PreorderGateResult());
        }

        var controller = new ReactStoreOrderController(
            service.Object,
            Mock.Of<ILogger<ReactStoreOrderController>>(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            dbContext ?? CreateUninitializedSqlSugarContext(),
            (userService ?? new Mock<IUserService>()).Object,
            Mock.Of<BlazorApp.Api.Interfaces.IStoreService>(),
            authorizationService.Object,
            scopeService.Object,
            (jobService ?? new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict)).Object,
            (
                invoiceEmailJobService
                ?? new Mock<IStoreOrderInvoiceEmailJobService>(MockBehavior.Strict)
            ).Object,
            (
                pasteReplaceJobService
                ?? new Mock<IStoreOrderPasteReplaceJobService>(MockBehavior.Strict)
            ).Object,
            (
                invoiceEmailTextTranslationService
                ?? new Mock<IStoreOrderInvoiceEmailTextTranslationService>(MockBehavior.Strict)
            ).Object,
            effectivePreorderGateService.Object
        );

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "user-1"),
                            new Claim(ClaimTypes.Name, "tester"),
                        },
                        "TestAuth"
                    )
                ),
            },
        };

        var identity = (ClaimsIdentity)controller.HttpContext.User.Identity!;
        foreach (var roleName in roleNames ?? Array.Empty<string>())
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
        }

        return controller;
    }

    private async Task SeedOrderAsync(string orderGuid, string storeCode)
    {
        await _db.Insertable(
            new WareHouseOrder
            {
                OrderGUID = orderGuid,
                StoreCode = storeCode,
                OrderNo = orderGuid,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private static Mock<IUserService> CreateAssignedStoreUserService(string storeCode)
    {
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto>
                    {
                        new() { StoreCode = storeCode, IsPrimary = false },
                    }
                )
            );
        return userService;
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    private static SqlSugarContext CreateUninitializedSqlSugarContext()
    {
        return (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
    }

    private static Mock<IAuthorizationService> CreateAuthorizationService(
        params string[] allowedPolicies
    )
    {
        var policies = new HashSet<string>(allowedPolicies, StringComparer.OrdinalIgnoreCase);
        var service = new Mock<IAuthorizationService>();

        service
            .Setup(item =>
                item.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync(
                (
                    ClaimsPrincipal _,
                    object? _,
                    string policy
                ) => policies.Contains(policy)
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed()
            );

        return service;
    }

    private static Mock<ICurrentUserManageableStoreScopeService> CreateScopeService()
    {
        var service = new Mock<ICurrentUserManageableStoreScopeService>();

        service
            .Setup(item => item.GetAccessibleStoreCodesAsync())
            .ReturnsAsync(new List<string> { "S001" });
        service.Setup(item => item.CanAccessStoreCodeAsync("S001")).ReturnsAsync(true);
        service.Setup(item => item.CanAccessOrderAsync("order-1")).ReturnsAsync(true);

        return service;
    }
}

public class StoreOrderSyncJobServiceTests
{
    [Fact]
    public void StoreOrderHqSyncConflictStrategy_Json合同使用字符串枚举()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<StoreOrderHqSyncRequestDto>(
            """
            {
              "conflictStrategy": "HqWins"
            }
            """,
            options
        );

        Assert.NotNull(request);
        Assert.Equal(StoreOrderHqSyncConflictStrategy.HqWins, request!.ConflictStrategy);

        var json = JsonSerializer.Serialize(
            new StoreOrderSyncJobDto
            {
                JobId = "job-json-contract",
                Status = StoreOrderSyncJobStatusConstants.Succeeded,
                Mode = StoreOrderHqSyncMode.Incremental.ToString(),
                ConflictStrategy = StoreOrderHqSyncConflictStrategy.LatestWins,
                Result = new SyncMissingOrdersResultDto
                {
                    Success = true,
                    ConflictStrategy = StoreOrderHqSyncConflictStrategy.HqWins,
                },
            },
            options
        );

        Assert.Contains("\"conflictStrategy\":\"LatestWins\"", json);
        Assert.Contains("\"conflictStrategy\":\"HqWins\"", json);
    }

    [Fact]
    public async Task StartHqSyncJobAsync_未传策略时归一为LatestWins并回显到任务快照()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<SyncMissingOrdersResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        StoreOrderHqSyncRequestDto? capturedRequest = null;
        var syncService = new Mock<IStoreOrderHqSyncService>(MockBehavior.Strict);
        syncService
            .Setup(item =>
                item.SyncAsync(
                    StoreOrderHqSyncMode.Incremental,
                    It.IsAny<StoreOrderHqSyncRequestDto?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<StoreOrderHqSyncMode, StoreOrderHqSyncRequestDto?, string?, CancellationToken>(
                (_, request, _, _) =>
                {
                    capturedRequest = request;
                    invoked.TrySetResult();
                }
            )
            .Returns(() => completion.Task);

        var jobService = CreateHqJobService(syncService.Object);
        var job = await jobService.StartHqSyncJobAsync(
            "user-1",
            StoreOrderHqSyncMode.Incremental,
            new StoreOrderHqSyncRequestDto
            {
                StoreCodes = new List<string> { "S002", "S001", "S001" },
            }
        );

        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(StoreOrderHqSyncConflictStrategy.LatestWins, job.ConflictStrategy);
        Assert.Equal(new List<string> { "S001", "S002" }, job.StoreCodes);
        Assert.NotNull(capturedRequest);
        Assert.Equal(
            StoreOrderHqSyncConflictStrategy.LatestWins,
            capturedRequest!.ConflictStrategy
        );

        completion.SetResult(
            new SyncMissingOrdersResultDto
            {
                Success = true,
                Message = "ok",
                ConflictStrategy = StoreOrderHqSyncConflictStrategy.LatestWins,
            }
        );

        var completedJob = await WaitForJobStatusAsync(
            jobService,
            job.JobId,
            StoreOrderSyncJobStatusConstants.Succeeded
        );
        Assert.Equal(StoreOrderHqSyncConflictStrategy.LatestWins, completedJob.ConflictStrategy);
        Assert.Equal(
            StoreOrderHqSyncConflictStrategy.LatestWins,
            completedJob.Result?.ConflictStrategy
        );
    }

    [Fact]
    public async Task StartHqSyncJobAsync_不同冲突策略运行中时不复用同一个任务()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<SyncMissingOrdersResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var syncService = new Mock<IStoreOrderHqSyncService>(MockBehavior.Strict);
        syncService
            .Setup(item =>
                item.SyncAsync(
                    StoreOrderHqSyncMode.Incremental,
                    It.IsAny<StoreOrderHqSyncRequestDto?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() =>
            {
                invoked.TrySetResult();
                return completion.Task;
            });

        var jobService = CreateHqJobService(syncService.Object);
        var firstJob = await jobService.StartHqSyncJobAsync(
            "user-1",
            StoreOrderHqSyncMode.Incremental,
            new StoreOrderHqSyncRequestDto
            {
                StoreCodes = new List<string> { "S001" },
                ConflictStrategy = StoreOrderHqSyncConflictStrategy.HqWins,
            }
        );
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            jobService.StartHqSyncJobAsync(
                "user-1",
                StoreOrderHqSyncMode.Incremental,
                new StoreOrderHqSyncRequestDto
                {
                    StoreCodes = new List<string> { "S001" },
                    ConflictStrategy = StoreOrderHqSyncConflictStrategy.LatestWins,
                }
            )
        );

        Assert.Equal("已有分店订货同步任务正在运行，请稍后再试", exception.Message);
        Assert.False(string.IsNullOrWhiteSpace(firstJob.JobId));

        completion.SetResult(
            new SyncMissingOrdersResultDto
            {
                Success = true,
                Message = "ok",
                ConflictStrategy = StoreOrderHqSyncConflictStrategy.HqWins,
            }
        );
    }

    [Fact]
    public async Task StartJobAsync_相同用户与门店集合在运行中时_返回同一个任务()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<SyncMissingOrdersResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var syncService = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        syncService
            .Setup(item =>
                item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>())
            )
            .Returns<SyncMissingOrdersRequestDto?>(_ =>
            {
                invoked.TrySetResult();
                return completion.Task;
            });

        var jobService = CreateJobService(syncService.Object);
        var firstRequest = new SyncMissingOrdersRequestDto
        {
            StoreCodes = new List<string> { "S002", "S001", "S001" },
        };
        var secondRequest = new SyncMissingOrdersRequestDto
        {
            StoreCodes = new List<string> { "S001", "S002" },
        };

        var firstJob = await jobService.StartJobAsync("user-1", firstRequest);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondJob = await jobService.StartJobAsync("user-1", secondRequest);

        Assert.Equal(firstJob.JobId, secondJob.JobId);
        Assert.True(secondJob.IsDuplicateRequest);
        syncService.Verify(
            item => item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>()),
            Times.Once
        );

        completion.SetResult(new SyncMissingOrdersResultDto { Success = true, Message = "ok" });
        var completedJob = await WaitForJobStatusAsync(
            jobService,
            firstJob.JobId,
            StoreOrderSyncJobStatusConstants.Succeeded
        );
        Assert.Equal("ok", completedJob.Result?.Message);
    }

    [Fact]
    public async Task StartJobAsync_任务完成后在保留期内可查询_过期后被清理()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        var syncService = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        syncService
            .Setup(item =>
                item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>())
            )
            .ReturnsAsync(new SyncMissingOrdersResultDto { Success = true, Message = "done" });

        var jobService = CreateJobService(
            syncService.Object,
            timeProvider: timeProvider,
            completedRetention: TimeSpan.FromMinutes(45)
        );

        var createdJob = await jobService.StartJobAsync(
            "user-1",
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );
        var completedJob = await WaitForJobStatusAsync(
            jobService,
            createdJob.JobId,
            StoreOrderSyncJobStatusConstants.Succeeded
        );

        Assert.NotNull(completedJob.CompletedAt);
        Assert.NotNull(completedJob.ExpiresAt);
        Assert.Equal("done", completedJob.Result?.Message);

        timeProvider.Advance(TimeSpan.FromMinutes(44));
        Assert.NotNull(await jobService.GetJobAsync(createdJob.JobId));

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await jobService.GetJobAsync(createdJob.JobId));
    }

    [Fact]
    public async Task StartJobAsync_同一请求在旧任务完成后_应该创建新任务()
    {
        var syncService = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        syncService
            .Setup(item =>
                item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>())
            )
            .ReturnsAsync(new SyncMissingOrdersResultDto { Success = true, Message = "done" });

        var jobService = CreateJobService(syncService.Object);
        var request = new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } };

        var firstJob = await jobService.StartJobAsync("user-1", request);
        await WaitForJobStatusAsync(
            jobService,
            firstJob.JobId,
            StoreOrderSyncJobStatusConstants.Succeeded
        );
        var secondJob = await jobService.StartJobAsync("user-1", request);
        await WaitForJobStatusAsync(
            jobService,
            secondJob.JobId,
            StoreOrderSyncJobStatusConstants.Succeeded
        );

        Assert.NotEqual(firstJob.JobId, secondJob.JobId);
        Assert.False(secondJob.IsDuplicateRequest);
        syncService.Verify(
            item => item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>()),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task StartJobAsync_后台同步抛异常时_任务状态为失败()
    {
        var syncService = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        syncService
            .Setup(item =>
                item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>())
            )
            .ThrowsAsync(new InvalidOperationException("boom"));

        var jobService = CreateJobService(syncService.Object);
        var createdJob = await jobService.StartJobAsync(
            "user-1",
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        var failedJob = await WaitForJobStatusAsync(
            jobService,
            createdJob.JobId,
            StoreOrderSyncJobStatusConstants.Failed
        );

        Assert.Equal("boom", failedJob.Result?.Message);
        Assert.False(failedJob.Result?.Success);
    }

    [Fact]
    public async Task StartJobAsync_不同写任务运行中时阻止并发启动()
    {
        var invokedCount = 0;
        var firstInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<SyncMissingOrdersResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var syncService = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        syncService
            .Setup(item =>
                item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>())
            )
            .Returns(() =>
            {
                Interlocked.Increment(ref invokedCount);
                firstInvoked.TrySetResult();

                return completion.Task;
            });

        var jobService = CreateJobService(syncService.Object);
        var request = new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } };

        var firstJob = await jobService.StartJobAsync("user-1", request);
        await firstInvoked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            jobService.StartJobAsync("user-2", request)
        );

        Assert.Equal("已有分店订货同步任务正在运行，请稍后再试", exception.Message);
        Assert.False(string.IsNullOrWhiteSpace(firstJob.JobId));
        syncService.Verify(
            item => item.SyncMissingOrdersFromHqAsync(It.IsAny<SyncMissingOrdersRequestDto?>()),
            Times.Once
        );

        completion.SetResult(new SyncMissingOrdersResultDto { Success = true, Message = "ok" });
    }

    private static StoreOrderSyncJobService CreateJobService(
        IStoreOrderReactService syncService,
        TimeProvider? timeProvider = null,
        TimeSpan? completedRetention = null
    )
    {
        var serviceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
        serviceProvider
            .Setup(item => item.GetService(typeof(IStoreOrderReactService)))
            .Returns(syncService);

        var scope = new Mock<IServiceScope>(MockBehavior.Strict);
        scope.SetupGet(item => item.ServiceProvider).Returns(serviceProvider.Object);
        scope.Setup(item => item.Dispose());

        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopeFactory.Setup(item => item.CreateScope()).Returns(scope.Object);

        return new StoreOrderSyncJobService(
            scopeFactory.Object,
            Mock.Of<ILogger<StoreOrderSyncJobService>>(),
            timeProvider ?? TimeProvider.System,
            completedRetention ?? TimeSpan.FromMinutes(45)
        );
    }

    private static StoreOrderSyncJobService CreateHqJobService(
        IStoreOrderHqSyncService syncService,
        TimeProvider? timeProvider = null,
        TimeSpan? completedRetention = null
    )
    {
        var serviceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
        serviceProvider
            .Setup(item => item.GetService(typeof(IStoreOrderHqSyncService)))
            .Returns(syncService);

        var scope = new Mock<IServiceScope>(MockBehavior.Strict);
        scope.SetupGet(item => item.ServiceProvider).Returns(serviceProvider.Object);
        scope.Setup(item => item.Dispose());

        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopeFactory.Setup(item => item.CreateScope()).Returns(scope.Object);

        return new StoreOrderSyncJobService(
            scopeFactory.Object,
            Mock.Of<ILogger<StoreOrderSyncJobService>>(),
            timeProvider ?? TimeProvider.System,
            completedRetention ?? TimeSpan.FromMinutes(45)
        );
    }

    private static async Task<StoreOrderSyncJobDto> WaitForJobStatusAsync(
        IStoreOrderSyncJobService jobService,
        string jobId,
        string expectedStatus
    )
    {
        for (var index = 0; index < 50; index++)
        {
            var job = await jobService.GetJobAsync(jobId);
            if (job?.Status == expectedStatus)
            {
                return job;
            }

            await Task.Delay(20);
        }

        throw new Xunit.Sdk.XunitException($"任务 {jobId} 未在预期时间内进入 {expectedStatus} 状态");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan offset)
        {
            _utcNow = _utcNow.Add(offset);
        }
    }

}
