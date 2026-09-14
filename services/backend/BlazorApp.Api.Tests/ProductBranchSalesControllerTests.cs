using System.Reflection;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Features.ProductInsights;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductBranchSalesControllerTests
{
    [Fact]
    public void 路由使用商品销量分析权限()
    {
        var authorize = typeof(ProductBranchSalesController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Permissions.Reports.ProductMovementView, authorize!.Policy);
    }

    [Fact]
    public async Task 实时全店角色查询完整POS范围()
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new()
            {
                RoleNames = new() { "WarehouseManager" },
            }));
        service.Setup(x => x.GetAsync("P1", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("all-pos"));

        var result = Assert.IsType<OkObjectResult>(await controller.Get(" P1 ", "2026-09-01", "2026-09-02", default));

        Assert.Equal("all-pos", Assert.IsType<ApiResponse<ProductInsightBranchSalesDto>>(result.Value).Data!.Scope);
        service.VerifyAll();
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 旧JWT管理员角色不能绕过实时门店范围()
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new()));
        users.Setup(x => x.GetUserStoresAsync("sean"))
            .ReturnsAsync(ApiResponse<List<UserStoreDto>>.OK(new()
            {
                new() { StoreCode = " s1 " }, new() { StoreCode = "S1" }, new() { StoreCode = "s2" },
            }));
        service.Setup(x => x.GetAsync(
                "P1", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2),
                It.Is<IReadOnlyCollection<string>>(stores => stores.OrderBy(code => code).SequenceEqual(new[] { "S1", "S2" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("authorized-pos"));

        var result = Assert.IsType<OkObjectResult>(await controller.Get("P1", "2026-09-01", "2026-09-02", default));

        Assert.Equal("authorized-pos", Assert.IsType<ApiResponse<ProductInsightBranchSalesDto>>(result.Value).Data!.Scope);
        service.VerifyAll();
    }

    [Fact]
    public async Task 授权快照失败时拒绝且绝不退化为全POS范围()
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.Error("failed"));

        Assert.IsType<ForbidResult>(await controller.Get("P1", "2026-09-01", "2026-09-02", default));

        service.VerifyNoOtherCalls();
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 日期格式错误返回四百且不把错误伪装为零销量()
    {
        var (controller, service, users, roles) = Create();

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Get("P1", "2026/09/01", "2026-09-02", default));

        var response = Assert.IsType<ApiResponse<ProductInsightBranchSalesDto>>(result.Value);
        Assert.Equal("INVALID_DATE_RANGE", response.Code);
        service.VerifyNoOtherCalls();
        users.VerifyNoOtherCalls();
        roles.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("2026-09-01", null)]
    [InlineData(null, "2026-09-02")]
    public async Task 只提供一个日期返回四百且不查询服务(string? startDate, string? endDate)
    {
        var (controller, service, users, roles) = Create();

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Get("P1", startDate, endDate, default));

        var response = Assert.IsType<ApiResponse<ProductInsightBranchSalesDto>>(result.Value);
        Assert.Equal("INVALID_DATE_RANGE", response.Code);
        service.VerifyNoOtherCalls();
        users.VerifyNoOtherCalls();
        roles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 两个日期都省略时使用近九十天默认范围()
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new() { RoleNames = new() { "Admin" } }));
        service.Setup(x => x.GetAsync(
                "P1",
                new DateOnly(2026, 6, 17),
                new DateOnly(2026, 9, 14),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("all-pos"));

        Assert.IsType<OkObjectResult>(await controller.Get("P1", null, null, default));

        service.VerifyAll();
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 最大结束日期返回四百而非日期溢出五百()
    {
        var (controller, service, users, roles) = Create();

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Get("P1", "9999-12-30", "9999-12-31", default));

        var response = Assert.IsType<ApiResponse<ProductInsightBranchSalesDto>>(result.Value);
        Assert.Equal("INVALID_DATE_RANGE", response.Code);
        service.VerifyNoOtherCalls();
        users.VerifyNoOtherCalls();
        roles.VerifyNoOtherCalls();
    }

    private static ProductInsightBranchSalesDto Response(string scope) => new()
    {
        ProductCode = "P1",
        Scope = scope,
        Range = new ProductInsightBranchSalesRangeDto { StartDate = "2026-09-01", EndDate = "2026-09-02" },
    };

    private static (ProductBranchSalesController Controller, Mock<IProductBranchSalesService> Service, Mock<IUserService> Users, Mock<IRoleService> Roles) Create()
    {
        var service = new Mock<IProductBranchSalesService>(MockBehavior.Strict);
        var users = new Mock<IUserService>(MockBehavior.Strict);
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        var controller = new ProductBranchSalesController(
            service.Object,
            users.Object,
            roles.Object,
            NullLogger<ProductBranchSalesController>.Instance,
            new FixedTimeProvider()
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "sean"),
                        // 模拟过期 JWT；实时快照才是全店范围权威。
                        new Claim(ClaimTypes.Role, "Admin"),
                    }, "test")),
                },
            },
        };
        return (controller, service, users, roles);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    }
}
