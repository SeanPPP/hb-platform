using System.Reflection;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class MobileReportStoreOptionsTests
{
    [Fact]
    public void 移动和Web门店接口保持独立权限()
    {
        Assert.Equal("Reports.ProductMovement.View", typeof(MobileReportStoreOptionsController)
            .GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("SalesDashboard.ProductMovement.View", typeof(ProductMovementReportController)
            .GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Theory]
    [InlineData("WarehouseManager")]
    [InlineData("Admin")]
    public async Task 实时全店角色使用现有POS筛选查询(string role)
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new() { RoleNames = new() { role } }));
        service.Setup(x => x.GetStoreOptionsAsync(null)).ReturnsAsync(new List<ProductMovementReportStoreOptionDto>());
        Assert.IsType<OkObjectResult>(await controller.GetStoreOptions());
        service.VerifyAll();
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 旧JWT管理员角色不能越过实时关联分店()
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new()));
        users.Setup(x => x.GetUserStoresAsync("sean"))
            .ReturnsAsync(ApiResponse<List<UserStoreDto>>.OK(new()
            {
                new() { StoreCode = " S1 " }, new() { StoreCode = "s1" }, new() { StoreCode = "S2" },
            }));
        service.Setup(x => x.GetStoreOptionsAsync(It.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "S1", "S2" }))))
            .ReturnsAsync(new List<ProductMovementReportStoreOptionDto>());
        Assert.IsType<OkObjectResult>(await controller.GetStoreOptions());
        service.VerifyAll();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 快照或关联分店读取失败不查询全店(bool snapshotFails)
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(snapshotFails ? ApiResponse<UserPermissionSnapshotDto>.Error("failed")
                : ApiResponse<UserPermissionSnapshotDto>.OK(new()));
        if (!snapshotFails)
            users.Setup(x => x.GetUserStoresAsync("sean"))
                .ReturnsAsync(ApiResponse<List<UserStoreDto>>.Error("failed"));
        Assert.IsType<ForbidResult>(await controller.GetStoreOptions());
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 没有关联分店返回空列表且不查询全店()
    {
        var (controller, service, users, roles) = Create();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("sean"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new()));
        users.Setup(x => x.GetUserStoresAsync("sean"))
            .ReturnsAsync(ApiResponse<List<UserStoreDto>>.OK(new()));
        var result = Assert.IsType<OkObjectResult>(await controller.GetStoreOptions());
        Assert.Empty(Assert.IsType<ApiResponse<List<ProductMovementReportStoreOptionDto>>>(result.Value).Data!);
        service.VerifyNoOtherCalls();
    }

    private static (MobileReportStoreOptionsController, Mock<IProductMovementReportService>, Mock<IUserService>, Mock<IRoleService>) Create()
    {
        var service = new Mock<IProductMovementReportService>(MockBehavior.Strict);
        var users = new Mock<IUserService>(MockBehavior.Strict);
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        var controller = new MobileReportStoreOptionsController(service.Object, users.Object, roles.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "sean"), new Claim(ClaimTypes.Role, "Admin"),
                    }, "test")),
                },
            },
        };
        return (controller, service, users, roles);
    }
}
