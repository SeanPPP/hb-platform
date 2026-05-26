using System.Security.Claims;
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
    public async Task LocalPurchaseViewPolicy_AllowsCanonicalAndLegacyPermissionClaims(
        string permissionClaim
    )
    {
        var roleService = new Mock<IRoleService>(MockBehavior.Strict);
        var handler = CreateHandler(roleService);
        var requirement = new PermissionRequirement(Permissions.LocalPurchase.View);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            CreateUser(new Claim("permission", permissionClaim)),
            resource: null
        );

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        roleService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LocalPurchaseViewPolicy_AllowsLegacyDatabasePermission()
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
    }

    private static PermissionAuthorizationHandler CreateHandler(Mock<IRoleService> roleService)
    {
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
}
