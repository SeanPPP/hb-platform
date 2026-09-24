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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 订货前台「分店供应商进货销量分析」只读接口：只认订货前台权限，门店范围限定为本人名下门店；
/// 同时覆盖后台分析接口「销售看板新权限码 或 LocalPurchase.View」的方法内授权。
/// </summary>
public sealed class ReactLocalSupplierInvoiceSalesAnalysisShopEndpointTests
{
    [Fact]
    public async Task ShopAnalysis_没有订货前台权限返回403且不调用服务()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, CreateAuthorizationService().Object, ["S1"]);

        var result = await controller.GetShopPurchaseSalesAnalysis(CreateQuery("S1"));

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShopAnalysis_只有后台LocalPurchaseView权限也返回403()
    {
        // 前台接口不放开后台 LocalPurchase.View，避免后台权限意外获得前台入口。
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.LocalPurchase.View).Object,
            ["S1"]
        );

        var result = await controller.GetShopPurchaseSalesAnalysis(CreateQuery("S1"));

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShopAnalysis_只有销售看板新权限码也返回403()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(
                Permissions.SalesDashboard.LocalSupplierPurchaseSalesView
            ).Object,
            ["S1"]
        );

        Assert.IsType<ForbidResult>(
            await controller.GetShopPurchaseSalesAnalysis(CreateQuery("S1"))
        );
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShopAnalysis_未注入授权服务时一律拒绝()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, authorizationService: null, ["S1"]);

        Assert.IsType<ForbidResult>(
            await controller.GetShopPurchaseSalesAnalysis(CreateQuery("S1"))
        );
        Assert.IsType<ForbidResult>(
            await controller.GetShopPurchaseSalesAnalysisSupplierOptions("S1")
        );
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShopAnalysis_有订货前台权限查询本人门店时调用服务且范围为该门店()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        LocalSupplierPurchaseSalesAnalysisQueryDto? capturedQuery = null;
        IReadOnlyList<string>? capturedScope = null;
        service
            .Setup(item => item.GetPurchaseSalesAnalysisAsync(
                It.IsAny<LocalSupplierPurchaseSalesAnalysisQueryDto>(),
                It.IsAny<IReadOnlyList<string>?>()
            ))
            .Callback((LocalSupplierPurchaseSalesAnalysisQueryDto query, IReadOnlyList<string>? scope) =>
            {
                capturedQuery = query;
                capturedScope = scope;
            })
            .ReturnsAsync(
                ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.OK(
                    new LocalSupplierPurchaseSalesAnalysisResponseDto()
                )
            );
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.OrderFront.View).Object,
            ["S1", "S2"]
        );

        var result = await controller.GetShopPurchaseSalesAnalysis(CreateQuery(" S1 "));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("S1", capturedQuery?.StoreCode);
        Assert.Equal(new[] { "S1" }, capturedScope);
        service.Verify(
            item => item.GetPurchaseSalesAnalysisAsync(
                It.IsAny<LocalSupplierPurchaseSalesAnalysisQueryDto>(),
                It.IsAny<IReadOnlyList<string>?>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task ShopAnalysis_请求非本人门店返回403()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.OrderFront.View).Object,
            ["S1"]
        );

        var result = await controller.GetShopPurchaseSalesAnalysis(CreateQuery("S9"));

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task ShopAnalysis_缺少门店返回400(string? storeCode)
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.OrderFront.View).Object,
            ["S1"]
        );

        var result = await controller.GetShopPurchaseSalesAnalysis(CreateQuery(storeCode));

        Assert.IsType<BadRequestObjectResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShopAnalysis_缺少供应商返回400()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.OrderFront.View).Object,
            ["S1"]
        );

        var result = await controller.GetShopPurchaseSalesAnalysis(
            new LocalSupplierPurchaseSalesAnalysisQueryDto { StoreCode = "S1" }
        );

        Assert.IsType<BadRequestObjectResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("WarehouseStaff", true)]
    [InlineData("仓库员工", true)]
    [InlineData("订货员", false)]
    public async Task ShopAnalysis_OrdersCreate只兼容纯仓库员工(string roleName, bool allowed)
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>();
        service
            .Setup(item => item.GetPurchaseSalesAnalysisAsync(
                It.IsAny<LocalSupplierPurchaseSalesAnalysisQueryDto>(),
                It.IsAny<IReadOnlyList<string>?>()
            ))
            .ReturnsAsync(
                ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.OK(
                    new LocalSupplierPurchaseSalesAnalysisResponseDto()
                )
            );
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.Orders.Create).Object,
            ["S1"],
            roleNames: [roleName]
        );

        var result = await controller.GetShopPurchaseSalesAnalysis(CreateQuery("S1"));

        if (allowed)
        {
            Assert.IsType<OkObjectResult>(result);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
            service.VerifyNoOtherCalls();
        }
    }

    [Fact]
    public async Task ShopSupplierOptions_没有订货前台权限返回403()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, CreateAuthorizationService().Object, ["S1"]);

        var result = await controller.GetShopPurchaseSalesAnalysisSupplierOptions("S1");

        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ShopSupplierOptions_本人门店时按该门店范围调用服务()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetSupplierOptionsAsync(
                It.Is<IReadOnlyList<string>?>(scope =>
                    scope != null && scope.Count == 1 && scope[0] == "S1"
                ),
                "S1"
            ))
            .ReturnsAsync(new List<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto>());
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.OrderFront.View).Object,
            ["S1", "S2"]
        );

        var result = await controller.GetShopPurchaseSalesAnalysisSupplierOptions("S1");

        Assert.IsType<OkObjectResult>(result);
        service.VerifyAll();
    }

    [Fact]
    public async Task ShopSupplierOptions_非本人门店返回403_缺少门店返回400()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.OrderFront.View).Object,
            ["S1"]
        );

        Assert.IsType<ForbidResult>(
            await controller.GetShopPurchaseSalesAnalysisSupplierOptions("S9")
        );
        Assert.IsType<BadRequestObjectResult>(
            await controller.GetShopPurchaseSalesAnalysisSupplierOptions(null)
        );
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BackOfficeAnalysis_仅有销售看板新权限码即可访问后台分析接口()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetPurchaseSalesAnalysisAsync(
                It.Is<LocalSupplierPurchaseSalesAnalysisQueryDto>(query => query.StoreCode == "S1"),
                It.Is<IReadOnlyList<string>?>(scope =>
                    scope != null && scope.Count == 1 && scope[0] == "S1"
                )
            ))
            .ReturnsAsync(
                ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.OK(
                    new LocalSupplierPurchaseSalesAnalysisResponseDto()
                )
            );
        service
            .Setup(item => item.GetStoreOptionsAsync(It.IsAny<IReadOnlyList<string>?>()))
            .ReturnsAsync(new List<LocalSupplierPurchaseSalesAnalysisStoreOptionDto>());
        service
            .Setup(item => item.GetSupplierOptionsAsync(It.IsAny<IReadOnlyList<string>?>(), "S1"))
            .ReturnsAsync(new List<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto>());
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(
                Permissions.SalesDashboard.LocalSupplierPurchaseSalesView
            ).Object,
            ["S1"]
        );

        Assert.IsType<OkObjectResult>(await controller.GetPurchaseSalesAnalysis(CreateQuery("S1")));
        Assert.IsType<OkObjectResult>(await controller.GetPurchaseSalesAnalysisStoreOptions());
        Assert.IsType<OkObjectResult>(
            await controller.GetPurchaseSalesAnalysisSupplierOptions("S1")
        );
        service.VerifyAll();
    }

    [Fact]
    public async Task BackOfficeAnalysis_原LocalPurchaseView权限仍可访问()
    {
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetStoreOptionsAsync(It.IsAny<IReadOnlyList<string>?>()))
            .ReturnsAsync(new List<LocalSupplierPurchaseSalesAnalysisStoreOptionDto>());
        var controller = CreateController(
            service.Object,
            CreateAuthorizationService(Permissions.LocalPurchase.View).Object,
            ["S1"]
        );

        Assert.IsType<OkObjectResult>(await controller.GetPurchaseSalesAnalysisStoreOptions());
        service.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackOfficeAnalysis_两个权限码都没有返回403(bool onlyOrderFrontPermission)
    {
        // 订货前台权限也不能反向打开后台分析接口；未注入授权服务同样拒绝。
        var service = new Mock<ILocalSupplierInvoiceSalesAnalysisService>(MockBehavior.Strict);
        var authorization = onlyOrderFrontPermission
            ? CreateAuthorizationService(Permissions.OrderFront.View, Permissions.LocalPurchase.MobileView)
            : CreateAuthorizationService();
        var controller = CreateController(service.Object, authorization.Object, ["S1"]);
        var withoutAuthorizationService = CreateController(
            service.Object,
            authorizationService: null,
            ["S1"]
        );

        Assert.IsType<ForbidResult>(await controller.GetPurchaseSalesAnalysis(CreateQuery("S1")));
        Assert.IsType<ForbidResult>(await controller.GetPurchaseSalesAnalysisStoreOptions());
        Assert.IsType<ForbidResult>(
            await controller.GetPurchaseSalesAnalysisSupplierOptions("S1")
        );
        Assert.IsType<ForbidResult>(
            await withoutAuthorizationService.GetPurchaseSalesAnalysis(CreateQuery("S1"))
        );
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(ReactLocalSupplierInvoiceSalesAnalysisController.GetShopPurchaseSalesAnalysis))]
    [InlineData(nameof(ReactLocalSupplierInvoiceSalesAnalysisController.GetShopPurchaseSalesAnalysisSupplierOptions))]
    public void ShopEndpoints_不挂后台策略_由方法内校验订货前台权限(string methodName)
    {
        var method = typeof(ReactLocalSupplierInvoiceSalesAnalysisController).GetMethod(methodName)!;

        Assert.DoesNotContain(
            method.GetCustomAttributes<AuthorizeAttribute>(inherit: false),
            attribute => !string.IsNullOrWhiteSpace(attribute.Policy)
        );
        // 类级仍要求登录，匿名请求进不来。
        Assert.Single(
            typeof(ReactLocalSupplierInvoiceSalesAnalysisController)
                .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
        );
    }

    private static LocalSupplierPurchaseSalesAnalysisQueryDto CreateQuery(string? storeCode) =>
        new() { StoreCode = storeCode, SupplierCode = "SUP1" };

    private static ReactLocalSupplierInvoiceSalesAnalysisController CreateController(
        ILocalSupplierInvoiceSalesAnalysisService service,
        IAuthorizationService? authorizationService,
        List<string> stores,
        IReadOnlyList<string>? roleNames = null
    )
    {
        var users = new Mock<IUserService>();
        users
            .Setup(item => item.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<UserDetailDto>.OK(
                    new UserDetailDto
                    {
                        UserGUID = "user-1",
                        Stores = stores
                            .Select(code => new UserStoreDto { StoreCode = code })
                            .ToList(),
                    }
                )
            );

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        claims.AddRange((roleNames ?? []).Select(role => new Claim(ClaimTypes.Role, role)));

        // 前台接口不访问数据库上下文（只有按单据校验门店的后台接口才用），这里传 null 即可。
        return new ReactLocalSupplierInvoiceSalesAnalysisController(
            service,
            users.Object,
            null!,
            NullLogger<ReactLocalSupplierInvoiceSalesAnalysisController>.Instance,
            authorizationService
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
                },
            },
        };
    }

    private static Mock<IAuthorizationService> CreateAuthorizationService(
        params string[] allowedPolicies
    )
    {
        var allowed = new HashSet<string>(allowedPolicies, StringComparer.OrdinalIgnoreCase);
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(item => item.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                It.IsAny<string>()
            ))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
                allowed.Contains(policy)
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed()
            );
        return authorization;
    }
}
