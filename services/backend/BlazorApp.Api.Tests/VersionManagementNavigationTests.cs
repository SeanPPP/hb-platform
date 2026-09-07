using System.Security.Claims;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using Xunit;

namespace BlazorApp.Api.Tests;

public class VersionManagementNavigationTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("管理员")]
    [InlineData("SuperAdmin")]
    [InlineData("超级管理员")]
    public void 管理员不需要额外权限即可看到两个版本管理入口(string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));
        var menu = new NavigationService().BuildAppMenu(user);
        Assert.Contains(menu, item => item.RouteName == "app-downloads");
        Assert.Contains(menu, item => item.RouteName == "wpf-versions");
    }

    [Theory]
    [InlineData("User")]
    [InlineData("StoreManager")]
    [InlineData("WarehouseManager")]
    public void 非管理员即使持有下载管理权限也不能看到移动入口(string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.Role, role),
            new Claim("permission", Permissions.System.ViewAppDownloads),
            new Claim("permission", Permissions.System.ManageAppDownloads),
        }, "test"));
        var menu = new NavigationService().BuildAppMenu(user);
        Assert.DoesNotContain(menu, item => item.RouteName is "app-downloads" or "wpf-versions");
        Assert.Contains(menu, item => item.RouteName == "settings");
    }

    [Theory]
    [InlineData("PDA")]
    [InlineData("Warehouse")]
    public void 纯设备菜单不包含版本管理(string deviceType)
    {
        Assert.DoesNotContain(new NavigationService().BuildDeviceAppMenu(deviceType), item => item.RouteName is "app-downloads" or "wpf-versions");
    }
}
