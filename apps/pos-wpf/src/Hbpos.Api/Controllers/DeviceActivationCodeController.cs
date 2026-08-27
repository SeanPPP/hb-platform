using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using BlazorApp.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/devices/activation-code")]
public sealed class DeviceActivationCodeController(
    IDeviceActivationCodeService service) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(DeviceActivationRateLimitOptions.PolicyName)]
    [HttpPost("preview")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(typeof(ApiResult<DeviceActivationCodePreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResult<DeviceActivationCodePreviewResponse>>> Preview(
        [FromBody, BindRequired] DeviceActivationCodePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.PreviewAsync(request, cancellationToken);
        return Ok(ApiResult<DeviceActivationCodePreviewResponse>.Ok(response));
    }

    [AllowAnonymous]
    [EnableRateLimiting(DeviceActivationRateLimitOptions.PolicyName)]
    [HttpPost("redeem")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(typeof(ApiResult<DeviceActivationCodeRedeemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResult<DeviceActivationCodeRedeemResponse>>> Redeem(
        [FromBody, BindRequired] DeviceActivationCodeRedeemRequest request,
        CancellationToken cancellationToken,
        [FromHeader(Name = DeviceActivationHeaders.RecoveryOnly)] bool recoveryOnly = false)
    {
        if (DeviceActivationCodeCodec.ContainsReservedActivationCode(request.HardwareId)
            || DeviceActivationCodeCodec.ContainsReservedActivationCode(request.TerminalName))
        {
            return BadRequest(InvalidMetadataProblem());
        }

        var response = await service.RedeemAsync(request, recoveryOnly, cancellationToken);
        return Ok(ApiResult<DeviceActivationCodeRedeemResponse>.Ok(response));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.DeviceRegistration)]
    [HttpPost("rebind")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(typeof(ApiResult<DeviceActivationCodeRedeemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResult<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResult<DeviceActivationCodeRedeemResponse>>> Rebind(
        [FromBody, BindRequired] DeviceActivationCodeRebindRequest request,
        CancellationToken cancellationToken)
    {
        if (DeviceActivationCodeCodec.ContainsReservedActivationCode(request.TerminalName))
        {
            return BadRequest(InvalidMetadataProblem());
        }

        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var hardwareId = User.FindFirstValue(DeviceAuthConstants.HardwareIdClaim);
        var deviceSystem = User.FindFirstValue(DeviceAuthConstants.DeviceSystemClaim);
        if (string.IsNullOrWhiteSpace(deviceCode)
            || string.IsNullOrWhiteSpace(storeCode)
            || string.IsNullOrWhiteSpace(hardwareId)
            || string.IsNullOrWhiteSpace(deviceSystem))
        {
            return Unauthorized(ApiResult<object>.Fail(
                "DEVICE_AUTH_REQUIRED",
                "Device authorization is required."));
        }

        var response = await service.RebindAsync(
            request,
            new DeviceActivationRebindContext(
                deviceCode,
                storeCode,
                hardwareId,
                deviceSystem),
            cancellationToken);
        return Ok(ApiResult<DeviceActivationCodeRedeemResponse>.Ok(response));
    }

    private static ProblemDetails InvalidMetadataProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Invalid device metadata.",
        Detail = "Public device metadata must not contain a device activation code.",
    };
}
