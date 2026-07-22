using System.Security.Claims;
using Hbpos.Api.Controllers;
using Hbpos.Api.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class AppUpdateControllerTests
{
    [Fact]
    public async Task Check_stale_cached_store_forwards_identity_when_hardware_and_auth_are_valid()
    {
        var updateService = new CapturingUpdateService();
        var validator = new StubIdentityValidator(new AppUpdateValidatedDeviceIdentity("HW-001"));
        var controller = CreateController(updateService, validator, "device-auth-secret");
        controller.HttpContext.Request.Headers[DeviceAuthConstants.DeviceCodeHeader] = "POS-OLD";
        controller.HttpContext.Request.Headers[DeviceAuthConstants.StoreCodeHeader] = "STORE-OLD";

        var result = await controller.Check("1.0.0", "production");

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("HW-001", validator.LastHardwareId);
        Assert.Equal("device-auth-secret", validator.LastAuthorizationCode);
        Assert.Equal(new AppUpdateDeviceIdentity("HW-001", "device-auth-secret"), updateService.LastIdentity);
    }

    [Fact]
    public async Task Check_forged_identity_does_not_forward_device_credentials()
    {
        var updateService = new CapturingUpdateService();
        var validator = CreateValidator(
            authorizationCode: "real-secret",
            deviceStatus: 1,
            deviceType: "POS",
            deviceSystem: "Windows");
        var controller = CreateController(updateService, validator, "forged-secret");

        await controller.Check("1.0.0", "production");

        Assert.Null(updateService.LastIdentity);
    }

    [Theory]
    [InlineData(0, "POS", "Windows")]
    [InlineData(1, "Mobile", "Windows")]
    [InlineData(1, "POS", "iOS")]
    public async Task Check_rejected_latest_registration_does_not_forward_device_credentials(
        int deviceStatus,
        string deviceType,
        string deviceSystem)
    {
        var updateService = new CapturingUpdateService();
        var validator = CreateValidator("device-auth-secret", deviceStatus, deviceType, deviceSystem);
        var controller = CreateController(updateService, validator, "device-auth-secret");

        await controller.Check("1.0.0", "production");

        Assert.Null(updateService.LastIdentity);
    }

    [Fact]
    public async Task Check_missing_raw_hardware_header_does_not_use_authenticated_user_claim()
    {
        var updateService = new CapturingUpdateService();
        var validator = new StubIdentityValidator(new AppUpdateValidatedDeviceIdentity("HW-CLAIM"));
        var controller = CreateController(updateService, validator, "device-auth-secret", hardwareId: string.Empty);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(DeviceAuthConstants.HardwareIdClaim, "HW-CLAIM")],
            DeviceAuthConstants.Scheme));

        await controller.Check("1.0.0", "production");

        Assert.Equal(0, validator.CallCount);
        Assert.Null(updateService.LastIdentity);
    }

    [Theory]
    [InlineData("", "device-auth-secret")]
    [InlineData("HW-001", "")]
    public async Task Check_incomplete_identity_does_not_call_validator_or_forward(
        string hardwareId,
        string bearerToken)
    {
        var updateService = new CapturingUpdateService();
        var validator = new StubIdentityValidator(new AppUpdateValidatedDeviceIdentity("HW-001"));
        var controller = CreateController(updateService, validator, bearerToken, hardwareId);

        await controller.Check("1.0.0", "production");

        Assert.Equal(0, validator.CallCount);
        Assert.Null(updateService.LastIdentity);
    }

    private static AppUpdateController CreateController(
        ILocalAppUpdateService updateService,
        IAppUpdateDeviceIdentityValidator validator,
        string bearerToken,
        string hardwareId = "HW-001")
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {bearerToken}";
        context.Request.Headers[DeviceAuthConstants.HardwareIdHeader] = hardwareId;

        return new AppUpdateController(updateService, validator)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static AppUpdateDeviceIdentityValidator CreateValidator(
        string authorizationCode,
        int deviceStatus,
        string deviceType,
        string deviceSystem)
    {
        var snapshot = new AppUpdateDeviceRegistrationSnapshot(
            "HW-001",
            authorizationCode,
            deviceStatus,
            deviceType,
            deviceSystem);
        return new AppUpdateDeviceIdentityValidator(
            (hardwareId, cancellationToken) => Task.FromResult<AppUpdateDeviceRegistrationSnapshot?>(snapshot));
    }

    private sealed class StubIdentityValidator(AppUpdateValidatedDeviceIdentity? result)
        : IAppUpdateDeviceIdentityValidator
    {
        public int CallCount { get; private set; }

        public string? LastHardwareId { get; private set; }

        public string? LastAuthorizationCode { get; private set; }

        public Task<AppUpdateValidatedDeviceIdentity?> ValidateAsync(
            string hardwareId,
            string authorizationCode,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastHardwareId = hardwareId;
            LastAuthorizationCode = authorizationCode;
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingUpdateService : ILocalAppUpdateService
    {
        public AppUpdateDeviceIdentity? LastIdentity { get; private set; }

        public Task<AppUpdateCheckResponse> CheckAsync(
            AppUpdateCheckRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppUpdateCheckResponse.NoUpdate(request.CurrentVersion));

        public Task<AppUpdateCheckResponse> CheckAsync(
            AppUpdateCheckRequest request,
            AppUpdateDeviceIdentity? deviceIdentity,
            CancellationToken cancellationToken = default)
        {
            LastIdentity = deviceIdentity;
            return Task.FromResult(AppUpdateCheckResponse.NoUpdate(request.CurrentVersion));
        }
    }
}
