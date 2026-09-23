using System.Reflection;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
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

public sealed class StaticReportAccessControllerTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("超级管理员")]
    [InlineData("WarehouseManager")]
    [InlineData("仓库管理员")]
    public async Task 全店角色放行并禁止缓存(string role)
    {
        var roles = SnapshotReturning(role);

        var controller = LoggedInController(roles.Object, "u1");
        var result = await controller.KfcUncleBills();

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("StoreManager")]
    [InlineData("StoreStaff")]
    [InlineData("User")]
    public async Task 只能看部分门店的角色被拒绝(string role)
    {
        var roles = SnapshotReturning(role);

        var result = await LoggedInController(roles.Object, "u1").KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task JWT里残留的管理员声明不能越过实时角色快照()
    {
        var roles = SnapshotReturning("StoreStaff");

        var result = await LoggedInController(roles.Object, "u1", staleRole: "Admin").KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task 读不到实时角色时拒绝()
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        roles.Setup(service => service.GetUserPermissionSnapshotAsync("u1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.Error("unavailable"));

        var result = await LoggedInController(roles.Object, "u1").KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task 角色服务异常时按拒绝处理而不是500()
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        roles.Setup(service => service.GetUserPermissionSnapshotAsync("u1"))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await LoggedInController(roles.Object, "u1").KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public async Task 缺少用户标识时拒绝()
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        var controller = CreateController(roles.Object);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));

        var result = await controller.KfcUncleBills();

        AssertForbidden(result);
    }

    [Fact]
    public void 端点要求本地商品分析查看权限_未登录由框架返回401()
    {
        var method = typeof(StaticReportAccessController).GetMethod(nameof(StaticReportAccessController.KfcUncleBills))!;
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Permissions.SalesDashboard.LocalProductAnalysisView, authorize!.Policy);
        Assert.Null(typeof(StaticReportAccessController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    private static void AssertForbidden(IActionResult result)
    {
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    private static Mock<IRoleService> SnapshotReturning(string role)
    {
        var roles = new Mock<IRoleService>(MockBehavior.Strict);
        roles.Setup(service => service.GetUserPermissionSnapshotAsync("u1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new() { RoleNames = new() { role } }));
        return roles;
    }

    private static StaticReportAccessController LoggedInController(IRoleService roles, string userGuid, string? staleRole = null)
    {
        var controller = CreateController(roles);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userGuid) };
        if (staleRole != null) claims.Add(new Claim(ClaimTypes.Role, staleRole));
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return controller;
    }

    private static StaticReportAccessController CreateController(IRoleService roles) =>
        new(roles, NullLogger<StaticReportAccessController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
}
