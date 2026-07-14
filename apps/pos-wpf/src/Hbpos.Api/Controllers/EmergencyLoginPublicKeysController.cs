using System.Security.Claims;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.EmergencyLogin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = DeviceAuthConstants.Scheme)]
[Route("api/v1/emergency-login/public-keys")]
public sealed class EmergencyLoginPublicKeysController(
    IEmergencyLoginPublicKeyDistributionService service) : ControllerBase
{
    [HttpGet("")]
    public async Task<ActionResult<EmergencyLoginPublicKeyPackage>> Get(
        CancellationToken cancellationToken)
    {
        var package = await service.GetAsync(cancellationToken);
        var etag = BuildEtag(package.Version);
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, no-cache";
        var etagMatches = Request.Headers.IfNoneMatch
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries) ?? [])
            .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal));
        if (etagMatches)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(package);
    }

    [HttpPost("ack")]
    public async Task<ActionResult<EmergencyLoginPublicKeyAckResponse>> Acknowledge(
        [FromBody] EmergencyLoginPublicKeyAckRequest request,
        CancellationToken cancellationToken)
    {
        var identity = GetAuthenticatedDevice();
        if (identity is null)
        {
            return Unauthorized(ApiResult<object>.Fail(
                "DEVICE_AUTH_REQUIRED",
                "Device authorization is required."));
        }

        var result = await service.AcknowledgeAsync(identity, request.Version, cancellationToken);
        if (result == EmergencyLoginPublicKeyAckResult.StaleIgnored)
        {
            // 轮换恰好发生在 GET 与 ACK 之间时，明确告知客户端立即拉取当前版本。
            var current = await service.GetAsync(cancellationToken);
            return Conflict(new EmergencyLoginPublicKeyAckResponse(current.Version));
        }

        return result switch
        {
            EmergencyLoginPublicKeyAckResult.FutureVersion => BadRequest(ApiResult<object>.Fail(
                "EMERGENCY_KEY_VERSION_FUTURE",
                "Acknowledged key version is newer than the server version.")),
            EmergencyLoginPublicKeyAckResult.DeviceNotFound => Unauthorized(ApiResult<object>.Fail(
                "DEVICE_AUTH_REQUIRED",
                "Authenticated device was not found.")),
            _ => Ok(new EmergencyLoginPublicKeyAckResponse(request.Version))
        };
    }

    private EmergencyLoginDeviceIdentity? GetAuthenticatedDevice()
    {
        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var hardwareId = User.FindFirstValue(DeviceAuthConstants.HardwareIdClaim);
        return string.IsNullOrWhiteSpace(deviceCode) ||
            string.IsNullOrWhiteSpace(storeCode) ||
            string.IsNullOrWhiteSpace(hardwareId)
            ? null
            : new EmergencyLoginDeviceIdentity(deviceCode, storeCode, hardwareId);
    }

    internal static string BuildEtag(long version) => $"\"emergency-login-keys-v{version}\"";
}
