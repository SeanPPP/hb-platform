using Hbpos.Api.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/app-update")]
public sealed class AppUpdateController(
    ILocalAppUpdateService updateService,
    IAppUpdateDeviceIdentityValidator identityValidator) : ControllerBase
{
    [HttpGet("check")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AppUpdateCheckResponse>>> Check(
        [FromQuery] string currentVersion,
        [FromQuery] string channel = "production",
        CancellationToken cancellationToken = default)
    {
        var deviceIdentity = await GetValidatedDeviceIdentityAsync(cancellationToken);
        var response = await updateService.CheckAsync(
            new AppUpdateCheckRequest
            {
                CurrentVersion = currentVersion,
                Channel = channel
            },
            deviceIdentity,
            cancellationToken);

        return Ok(ApiResult<AppUpdateCheckResponse>.Ok(response));
    }

    private async Task<AppUpdateDeviceIdentity?> GetValidatedDeviceIdentityAsync(
        CancellationToken cancellationToken)
    {
        var hardwareId = Request.Headers[DeviceAuthConstants.HardwareIdHeader].ToString().Trim();
        var authorizationHeader = Request.Headers[DeviceAuthConstants.AuthorizationHeader].ToString();
        if (string.IsNullOrWhiteSpace(hardwareId) ||
            string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith(DeviceAuthConstants.BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var authorizationCode = authorizationHeader[DeviceAuthConstants.BearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return null;
        }

        // 关键逻辑：更新检查独立验证硬件号和授权码，不让旧门店缓存阻断设备迁店后的冷启动更新。
        var validatedIdentity = await identityValidator.ValidateAsync(
            hardwareId,
            authorizationCode,
            cancellationToken);
        return validatedIdentity is null || string.IsNullOrWhiteSpace(validatedIdentity.HardwareId)
            ? null
            : new AppUpdateDeviceIdentity(validatedIdentity.HardwareId, authorizationCode);
    }
}
