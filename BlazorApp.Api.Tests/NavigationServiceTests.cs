using System.Security.Claims;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace BlazorApp.Api.Tests;

public class NavigationServiceTests
{
    private readonly NavigationService _service = new();

    [Fact]
    public void BuildAppMenu_HidesEmployeeProfileWithoutEmployeeProfilePermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Orders.View));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "employee-profile");
    }

    [Fact]
    public void BuildAppMenu_ShowsEmployeeProfileWithEmployeeProfileViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.EmployeeProfiles.View));

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "employee-profile");
    }

    [Fact]
    public void BuildAppMenu_HidesUsersForStoreManagerWithoutUsersViewPermission()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "StoreManager"));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "users");
    }

    [Fact]
    public void BuildAppMenu_ShowsUsersForStoreManagerWithUsersViewPermission()
    {
        var user = CreateUser(
            new Claim(ClaimTypes.Role, "StoreManager"),
            new Claim("permission", Permissions.Users.View)
        );

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "users");
    }

    [Fact]
    public void BuildAppMenu_HidesUsersForNonStoreManagerWithUsersViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Users.View));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "users");
    }

    [Fact]
    public void EmployeeProfileSelfRead_RequiresEmployeeProfileViewPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(nameof(EmployeeProfilesController.GetSelf));

        Assert.Equal(Permissions.EmployeeProfiles.View, authorizeAttribute.Policy);
    }

    [Fact]
    public void EmployeeProfileSelfSave_RequiresEmployeeProfileEditPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(nameof(EmployeeProfilesController.UpsertSelf));

        Assert.Equal(Permissions.EmployeeProfiles.Edit, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersGrid_RequiresUsersViewPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.Grid)
        );

        Assert.Equal(Permissions.Users.View, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersCreate_RequiresUsersCreatePermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.Create)
        );

        Assert.Equal(Permissions.Users.Create, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersUpdate_RequiresUsersEditPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.Update)
        );

        Assert.Equal(Permissions.Users.Edit, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersPassword_RequiresUsersResetPasswordPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.UpdatePassword)
        );

        Assert.Equal(Permissions.Users.ResetPassword, authorizeAttribute.Policy);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static AuthorizeAttribute GetMethodAuthorizeAttribute(string methodName)
    {
        return GetMethodAuthorizeAttribute(typeof(EmployeeProfilesController), methodName);
    }

    private static AuthorizeAttribute GetMethodAuthorizeAttribute(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .OfType<AuthorizeAttribute>()
            .Single(item => !string.IsNullOrWhiteSpace(item.Policy));
    }
}
