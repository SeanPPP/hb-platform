using System.Reflection;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
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
        Mock<ICurrentUserManageableStoreScopeService> scopeService
    )
    {
        var controller = new ReactStoreOrderController(
            service.Object,
            Mock.Of<ILogger<ReactStoreOrderController>>(),
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<BlazorApp.Api.Interfaces.IUserService>(),
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
                        },
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
