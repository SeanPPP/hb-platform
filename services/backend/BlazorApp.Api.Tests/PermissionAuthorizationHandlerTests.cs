using System.Security.Claims;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Authorization;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class PermissionAuthorizationHandlerTests
{
    [Theory]
    [InlineData(Permissions.LocalPurchase.View)]
    [InlineData("LocalInvocie.View")]
    public async Task LocalPurchaseViewPolicy_DoesNotTrustCanonicalOrLegacyPermissionClaims(
        string permissionClaim
    )
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", Permissions.LocalPurchase.View))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", "LocalInvocie.View"))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.LocalPurchase.View);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(new Claim("permission", permissionClaim)),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task LocalPurchaseViewPolicy_AllowsLegacyDatabasePermissionAlias()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", Permissions.LocalPurchase.View))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", "LocalInvocie.View"))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.LocalPurchase.View);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", Permissions.LocalPurchase.View),
            Times.Once
        );
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", "LocalInvocie.View"),
            Times.Once
        );
    }

    [Fact]
    public async Task ViewAppDownloadsPolicy_AllowsManageAppDownloadsDatabasePermissionAlias()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", Permissions.System.ViewAppDownloads))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", Permissions.System.ManageAppDownloads))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.System.ViewAppDownloads);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", Permissions.System.ViewAppDownloads),
            Times.Once
        );
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", Permissions.System.ManageAppDownloads),
            Times.Once
        );
    }

    [Fact]
    public async Task LocalPurchaseViewPolicy_DeniesWithoutCanonicalOrLegacyPermission()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", It.IsAny<string>()))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.LocalPurchase.View);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", Permissions.LocalPurchase.View),
            Times.Once
        );
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", "LocalInvocie.View"),
            Times.Once
        );
    }

    [Fact]
    public async Task LocalPurchaseViewPolicy_RequeriesDatabaseInsteadOfCachingPermissionResult()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .SetupSequence(service =>
                service.UserHasPermissionAsync("user-1", Permissions.LocalPurchase.View)
            )
            .ReturnsAsync(ApiResponse<bool>.OK(true))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", "LocalInvocie.View"))
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.LocalPurchase.View);

        var firstContext = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(),
            resource: null
        );
        await handler.HandleAsync(firstContext);

        var secondContext = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(),
            resource: null
        );
        await handler.HandleAsync(secondContext);

        Assert.True(firstContext.HasSucceeded);
        Assert.False(secondContext.HasSucceeded);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", Permissions.LocalPurchase.View),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task AdminRole_AllowsAnyPermissionWithoutExplicitRolePermission()
    {
        var roleService = new Mock<IRoleService>();
        var handler = CreateHandler(roleService);

        roleService
            .Setup(service => service.UserHasRoleAsync("user-1", "Admin"))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", It.IsAny<string>()))
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var requirement = new PermissionRequirement("Unseeded.Permission");
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleService.Verify(service => service.UserHasRoleAsync("user-1", "Admin"), Times.Once);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ServiceApiTokenScope_AllowsMatchingAppDownloadPermissionWithoutRoleLookup()
    {
        var roleService = new Mock<IRoleService>();
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.System.ManageAppDownloads);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateServiceTokenUser(Permissions.System.ManageAppDownloads),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleService.Verify(
            service => service.UserHasPermissionAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
        roleService.Verify(
            service => service.UserHasRoleAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ServiceApiTokenScope_DoesNotUnlockOtherPermissionsOrRoleFallback()
    {
        var roleService = new Mock<IRoleService>();
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.System.ViewAppDownloads);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateServiceTokenUser(Permissions.System.ManageAppDownloads),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        roleService.Verify(
            service => service.UserHasPermissionAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
        roleService.Verify(
            service => service.UserHasRoleAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task StoreManagerRoleClaim_DoesNotUnlockManagedStorePermissionWithoutDatabaseGrant()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service =>
                service.UserHasPermissionAsync("user-1", Permissions.Attendance.Schedule.ViewStore)
            )
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.Attendance.Schedule.ViewStore);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(new Claim(ClaimTypes.Role, "StoreManager")),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        roleService.Verify(service => service.UserHasRoleAsync("user-1", "StoreManager"), Times.Never);
        roleService.Verify(
            service =>
                service.UserHasPermissionAsync("user-1", Permissions.Attendance.Schedule.ViewStore),
            Times.Once
        );
    }

    [Theory]
    [InlineData(Permissions.DeviceRegistration.View)]
    [InlineData(Permissions.DeviceRegistration.Manage)]
    public async Task StoreManagerRole_DoesNotUnlockDeviceRegistrationWithoutDatabaseGrant(
        string permission
    )
    {
        var roleService = new Mock<IRoleService>();
        var handler = CreateHandler(roleService);
        roleService
            .Setup(service => service.UserHasRoleAsync("user-1", "StoreManager"))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-1", permission))
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(new Claim(ClaimTypes.Role, "StoreManager")),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        roleService.Verify(service => service.UserHasRoleAsync("user-1", "StoreManager"), Times.Never);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", permission),
            Times.Once
        );
    }

    [Fact]
    public async Task WarehouseManagerRole_DoesNotUnlockWarehousePermissionWithoutDatabaseGrant()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasRoleAsync("user-1", "WarehouseManager"))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service =>
                service.UserHasPermissionAsync("user-1", Permissions.Warehouse.ManageOrders)
            )
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.Warehouse.ManageOrders);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(new Claim(ClaimTypes.Role, "WarehouseManager")),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        roleService.Verify(service => service.UserHasRoleAsync("user-1", "WarehouseManager"), Times.Never);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", Permissions.Warehouse.ManageOrders),
            Times.Once
        );
    }

    [Fact]
    public void GetAllPermissions_IncludesDeviceRegistrationPermissions()
    {
        var permissionCodes = Permissions.GetAllPermissions().Select(item => item.Code).ToHashSet();

        Assert.Contains(Permissions.DeviceRegistration.View, permissionCodes);
        Assert.Contains(Permissions.DeviceRegistration.Manage, permissionCodes);
    }

    [Fact]
    public void PermissionSeedData_IncludesDeviceRegistrationPermissions()
    {
        var permissionCodes = PermissionSeedData.AllPermissions
            .Select(item => item.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(Permissions.DeviceRegistration.View, permissionCodes);
        Assert.Contains(Permissions.DeviceRegistration.Manage, permissionCodes);
    }

    [Fact]
    public void PermissionSeedData_IncludesAppDownloadPermissionWithoutRoleTemplateGrant()
    {
        var appDownloadPermission = Assert.Single(
            PermissionSeedData.AllPermissions,
            seed => seed.Code == Permissions.System.ViewAppDownloads
        );

        Assert.Equal("查看 App 下载", appDownloadPermission.Name);
        Assert.Equal("系统管理", appDownloadPermission.Category);
        Assert.DoesNotContain(
            PermissionSeedData.RolePermissionTemplates.SelectMany(template => template.PermissionCodes),
            code => code == Permissions.System.ViewAppDownloads
        );
    }

    [Fact]
    public async Task AttendanceSelfServicePermission_AllowsAuthenticatedUserWithoutDatabasePermission()
    {
        var roleService = new Mock<IRoleService>();
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.Attendance.Punch.Self);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleService.Verify(
            service => service.UserHasPermissionAsync("user-1", It.IsAny<string>()),
            Times.Never
        );
    }

    private static PermissionAuthorizationHandler CreateHandler(Mock<IRoleService> roleService)
    {
        roleService
            .Setup(service => service.UserHasRoleAsync("user-1", It.IsAny<string>()))
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(provider => provider.GetService(typeof(IRoleService)))
            .Returns(roleService.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(item => item.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(factory => factory.CreateScope()).Returns(scope.Object);

        return new PermissionAuthorizationHandler(
            scopeFactory.Object,
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<PermissionAuthorizationHandler>>()
        );
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        var allClaims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        allClaims.AddRange(claims);
        return new ClaimsPrincipal(new ClaimsIdentity(allClaims, "TestAuth"));
    }

    private static ClaimsPrincipal CreateServiceTokenUser(string scope)
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(ServiceApiTokenAuthenticationDefaults.TokenTypeClaim, "true"),
                    new Claim(ServiceApiTokenAuthenticationDefaults.ScopeClaim, scope),
                },
                ServiceApiTokenAuthenticationDefaults.AuthenticationScheme
            )
        );
    }
}
