using System.Reflection;
using System.Security.Claims;
using BlazorApp.Api.Authorization;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ContainerAllocationSalesAuthorizationPipelineTests
{
    [Theory]
    [InlineData(nameof(ReactContainerController.QueryAllocationSales))]
    [InlineData(nameof(ReactContainerController.QueryAllocationSalesBranches))]
    public async Task AllocationSalesEndpoints_无货柜查看权限时经授权中间件返回403(string methodName)
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasRoleAsync("user-no-container-view", It.IsAny<string>()))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        roleService
            .Setup(service => service.UserHasPermissionAsync("user-no-container-view", It.IsAny<string>()))
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAuthentication("TestAuth");
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped(_ => roleService.Object);
        services.Replace(ServiceDescriptor.Singleton<IAuthenticationService, PipelineAuthenticationService>());
        await using var provider = services.BuildServiceProvider();

        var nextInvoked = false;
        var appBuilder = new ApplicationBuilder(provider);
        appBuilder.UseAuthorization();
        appBuilder.Run(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext { RequestServices = provider };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "user-no-container-view") },
            "TestAuth"
        ));
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(GetAuthorizationMetadata(methodName)),
            methodName
        ));

        await appBuilder.Build()(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextInvoked);
    }

    private static object[] GetAuthorizationMetadata(string methodName)
    {
        var controllerAttributes = typeof(ReactContainerController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Cast<object>();
        var methodAttributes = typeof(ReactContainerController)
            .GetMethod(methodName)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Cast<object>();
        return controllerAttributes.Concat(methodAttributes).ToArray();
    }

    private sealed class PipelineAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(context.User, scheme ?? "TestAuth")));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties
        ) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }
}
