using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class DeviceAuthenticationHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_ProjectsDisabledTransactionPermissionIntoClaim()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDeviceAuthorizationService>(
            new StubDeviceAuthorizationService(allowTransactions: false));
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(
                DeviceAuthConstants.Scheme,
                _ => { });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
        };
        context.Request.Headers.Authorization = "Bearer AUTH-1";
        context.Request.Headers[DeviceAuthConstants.DeviceCodeHeader] = "POS-1";
        context.Request.Headers[DeviceAuthConstants.StoreCodeHeader] = "STORE-1";
        context.Request.Headers[DeviceAuthConstants.HardwareIdHeader] = "HW-1";

        var result = await context.AuthenticateAsync(DeviceAuthConstants.Scheme);

        Assert.True(result.Succeeded);
        Assert.Equal(
            bool.FalseString,
            result.Principal?.FindFirst(DeviceAuthConstants.AllowTransactionsClaim)?.Value);
    }

    private sealed class StubDeviceAuthorizationService(bool allowTransactions)
        : IDeviceAuthorizationService
    {
        public Task<DeviceAuthorizationValidationResult> ValidateAsync(
            string authorizationCode,
            string deviceCode,
            string storeCode,
            string? hardwareId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(DeviceAuthorizationValidationResult.Authorized(
                new DeviceAuthorizationResult(
                    deviceCode,
                    storeCode,
                    hardwareId ?? string.Empty,
                    DeviceSystems.IpadOs,
                    allowTransactions)));
        }
    }
}
