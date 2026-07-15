using System.Reflection;
using BlazorApp.Api.Controllers;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class UserStorePosTerminalPermissionContractTests
{
    [Fact]
    public void SharedContract_包含实体与精确响应字段()
    {
        var sharedAssembly = typeof(Permissions).Assembly;
        var entity = sharedAssembly.GetType("BlazorApp.Shared.Models.SysUserStorePosPermission");
        var response = sharedAssembly.GetType(
            "BlazorApp.Shared.DTOs.UserStorePosTerminalPermissionsResponse"
        );

        Assert.NotNull(entity);
        Assert.NotNull(response);
        Assert.Equal(
            new[]
            {
                "AssignablePermissions",
                "EffectivePermissionCodes",
                "GrantedPermissionCodes",
                "InheritedPermissionCodes",
                "Mode",
                "OverriddenPermissionCodes",
            },
            response!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(item => item.Name)
                .OrderBy(item => item)
                .ToArray()
        );
    }

    [Fact]
    public void Resolver_门店覆盖优先于继承且空覆盖恢复继承()
    {
        var resolver = Type.GetType(
            "BlazorApp.Api.Services.UserStorePosTerminalPermissionResolver, BlazorApp.Api"
        );
        Assert.NotNull(resolver);
        var resolve = resolver!.GetMethod("ResolveEffectivePermissionCodes");
        Assert.NotNull(resolve);

        var inherited = new[]
        {
            "Permissions.PosTerminal.Sales.LineManualDiscount",
            "Permissions.PosTerminal.Sales.OrderManualDiscount",
        };
        var overrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["Permissions.PosTerminal.Sales.LineManualDiscount"] = false,
            ["Permissions.PosTerminal.Sales.LineQuickDiscount10Percent"] = true,
        };

        var effective = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(
            resolve!.Invoke(null, new object?[] { inherited, overrides, false })
        );
        Assert.DoesNotContain("Permissions.PosTerminal.Sales.LineManualDiscount", effective);
        Assert.Contains("Permissions.PosTerminal.Sales.LineQuickDiscount10Percent", effective);
        Assert.Contains("Permissions.PosTerminal.Sales.OrderManualDiscount", effective);

        var restored = Assert.IsAssignableFrom<IReadOnlyCollection<string>>(
            resolve.Invoke(
                null,
                new object?[]
                {
                    inherited,
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                    false,
                }
            )
        );
        Assert.Equal(inherited.OrderBy(item => item), restored.OrderBy(item => item));
    }

    [Fact]
    public void Controller_固定路由方法和管理权限策略()
    {
        var type = typeof(UserStorePosTerminalPermissionsController);
        Assert.Equal(
            "api/Users/guid/{userGuid}/stores/{storeGuid}/pos-terminal-permissions",
            type.GetCustomAttribute<RouteAttribute>()!.Template
        );
        Assert.Equal(
            Permissions.Users.ManagePosTerminalPermissions,
            type.GetCustomAttribute<AuthorizeAttribute>()!.Policy
        );
        Assert.NotNull(type.GetMethod("Get")!.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(type.GetMethod("Put")!.GetCustomAttribute<HttpPutAttribute>());
        Assert.NotNull(type.GetMethod("Delete")!.GetCustomAttribute<HttpDeleteAttribute>());
    }
}
