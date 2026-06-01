using System.Reflection;
using System.Security.Claims;
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
            CreateAuthorizationService(Permissions.Orders.View),
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
        service
            .Setup(item => item.AddToCartAsync(request))
            .ReturnsAsync(new ApiResponse<bool> { Success = true, Data = true });
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

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.AddToCartAsync(request), Times.Once);
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
    public async Task GetOrderDetail_AllowsAuthorizedScopedUser_AndCallsService()
    {
        var service = new Mock<IStoreOrderReactService>();
        service
            .Setup(item => item.GetOrderDetailAsync("order-1"))
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

        var result = await controller.GetOrderDetail("order-1");

        Assert.IsType<OkObjectResult>(result);
        service.Verify(item => item.GetOrderDetailAsync("order-1"), Times.Once);
    }

    private static ReactStoreOrderController CreateController(
        Mock<IStoreOrderReactService> service,
        Mock<IAuthorizationService> authorizationService,
        Mock<ICurrentUserManageableStoreScopeService> scopeService,
        IReadOnlyCollection<string>? roles = null,
        Mock<IUserService>? userService = null
    )
    {
        var controller = new ReactStoreOrderController(
            service.Object,
            Mock.Of<ILogger<ReactStoreOrderController>>(),
            new MemoryCache(new MemoryCacheOptions()),
            userService?.Object ?? Mock.Of<BlazorApp.Api.Interfaces.IUserService>(),
            Mock.Of<BlazorApp.Api.Interfaces.IStoreService>(),
            authorizationService.Object,
            scopeService.Object
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
                        }.Concat((roles ?? Array.Empty<string>()).Select(role =>
                            new Claim(ClaimTypes.Role, role)
                        )),
                        "TestAuth"
                    )
                ),
            },
        };

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
