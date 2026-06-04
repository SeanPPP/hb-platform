using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ReactStoreOrderAuthorizationTests
{
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
        var warmer = new StoreOrderCacheWarmer(
            service.Object,
            Mock.Of<ILogger<StoreOrderCacheWarmer>>(),
            cache
        );
        var webHomeKey = StoreOrderCacheKeys.GetHomePageCacheKey(50);
        var expoHomeKey = StoreOrderCacheKeys.GetHomePageCacheKey(18);
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
        cache.Set(customKey, "custom");

        await warmer.ClearCacheAsync();

        Assert.True(cache.TryGetValue(webHomeKey, out _));
        Assert.True(cache.TryGetValue(expoHomeKey, out _));
        Assert.False(cache.TryGetValue(customKey, out _));
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
    public async Task ScanLookupProducts_ThenAddToCart_ReusesShortTtlAuthorizationAndStoreScopeCache()
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
                    new StoreOrderScanLookupResultDto { Barcode = "930001" }
                )
            );
        service
            .Setup(item => item.AddToCartAsync(addRequest))
            .ReturnsAsync(
                ApiResponse<StoreOrderCartDto?>.OK(
                    new StoreOrderCartDto
                    {
                        OrderGUID = "cart-1",
                        StoreCode = storeCode,
                        TotalQuantity = 1,
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
        var addResult = await controller.AddToCart(addRequest);

        Assert.IsType<OkObjectResult>(lookupResult);
        Assert.IsType<OkObjectResult>(addResult);
        service.Verify(item => item.ScanLookupProductsAsync(lookupRequest), Times.Once);
        service.Verify(item => item.AddToCartAsync(addRequest), Times.Once);
        authorizationService.Verify(
            item =>
                item.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<string>()
                ),
            Times.Exactly(4)
        );
        scopeService.Verify(item => item.CanAccessStoreCodeAsync(storeCode), Times.Once);
        userService.Verify(item => item.GetUserStoresAsync("user-1"), Times.Once);
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

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Orders.View),
            scopeService
        );

        var result = await controller.GetOrderList(
            new StoreOrderListFilterDto { StoreCode = "S999" }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
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
    public async Task SyncMissingOrders_ForbidsWhenAnyRequestedStoreIsOutsideCurrentUserScope()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.SyncMissingOrders(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001", "S999" } }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncMissingOrders_ForbidsLegacyStoreCodeWhenStoreCodesAreBlank()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" }
        );

        var result = await controller.SyncMissingOrders(
            new SyncMissingOrdersRequestDto
            {
                StoreCode = "S999",
                StoreCodes = new List<string> { " " },
            }
        );

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
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
    public async Task SyncMissingOrders_WhenStoreNotSelectedForScopedUser_PassesAccessibleStores()
    {
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

        var result = await controller.SyncMissingOrders(new SyncMissingOrdersRequestDto());

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(new List<string> { "S001", "S002" }, capturedRequest!.StoreCodes);
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
    public async Task CreateSyncMissingOrdersJob_ForbidsWhenRequestedStoreIsOutsideCurrentUserScope()
    {
        var service = new Mock<IStoreOrderReactService>(MockBehavior.Strict);
        var jobService = new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict);
        var scopeService = CreateScopeService();
        scopeService.Setup(item => item.CanAccessStoreCodeAsync("S999")).ReturnsAsync(false);

        var controller = CreateController(
            service,
            CreateAuthorizationService(Permissions.Warehouse.ManageOrders),
            scopeService,
            new[] { "StoreManager" },
            jobService
        );

        var result = await controller.CreateSyncMissingOrdersJob(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001", "S999" } }
        );

        Assert.IsType<ForbidResult>(result);
        jobService.VerifyNoOtherCalls();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateSyncMissingOrdersJob_WhenStoreNotSelectedForScopedUser_PassesScopedStoresToJobService()
    {
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

        var result = await controller.CreateSyncMissingOrdersJob(new SyncMissingOrdersRequestDto());

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("user-1", capturedUserId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(new List<string> { "S001", "S002" }, capturedRequest!.StoreCodes);
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
    public async Task GetSyncMissingOrdersJob_ForbidsWhenJobContainsStoreOutsideCurrentUserScope()
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

        Assert.IsType<ForbidResult>(result);
        jobService.VerifyAll();
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
    public async Task GetOrderDetail_AllowsAuthorizedScopedUser_AndCallsService()
    {
        var service = new Mock<IStoreOrderReactService>();
        service
            .Setup(item =>
                item.GetOrderDetailAsync(
                    "order-1",
                    It.Is<StoreOrderDetailQueryDto>(query =>
                        query.PageNumber == 1 && query.PageSize == 50
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

        Assert.IsType<OkObjectResult>(result);
        service.Verify(
            item =>
                item.GetOrderDetailAsync(
                    "order-1",
                    It.Is<StoreOrderDetailQueryDto>(query =>
                        query.PageNumber == 1 && query.PageSize == 50
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

    private static ReactStoreOrderController CreateController(
        Mock<IStoreOrderReactService> service,
        Mock<IAuthorizationService> authorizationService,
        Mock<ICurrentUserManageableStoreScopeService> scopeService,
        IReadOnlyCollection<string>? roleNames = null,
        Mock<IStoreOrderSyncJobService>? jobService = null,
        Mock<IStoreOrderInvoiceEmailJobService>? invoiceEmailJobService = null,
        Mock<IUserService>? userService = null
    )
    {
        var controller = new ReactStoreOrderController(
            service.Object,
            Mock.Of<ILogger<ReactStoreOrderController>>(),
            new MemoryCache(new MemoryCacheOptions()),
            (userService ?? new Mock<IUserService>()).Object,
            Mock.Of<BlazorApp.Api.Interfaces.IStoreService>(),
            authorizationService.Object,
            scopeService.Object,
            (jobService ?? new Mock<IStoreOrderSyncJobService>(MockBehavior.Strict)).Object,
            (
                invoiceEmailJobService
                ?? new Mock<IStoreOrderInvoiceEmailJobService>(MockBehavior.Strict)
            ).Object
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
