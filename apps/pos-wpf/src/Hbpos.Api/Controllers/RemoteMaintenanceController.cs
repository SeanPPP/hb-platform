using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.RemoteMaintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/remote-maintenance")]
[Authorize(Policy = CashierAuthorizationPolicies.RemoteMaintenance)]
public sealed class RemoteMaintenanceController(
    IRemoteMaintenanceGateway gateway,
    IAppUpdateDeviceIdentityValidator identityValidator) : ControllerBase
{
    [HttpPost("prepare")]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Prepare([FromBody] RemoteMaintenancePrepareRequest request, CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.ComputerName))
            return BadRequest(new { code = "REMOTE_MAINTENANCE_INVALID_REQUEST", message = "operationId 和 computerName 必须提供" });
        var hardwareId = await GetCurrentHardwareIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(hardwareId)) return Unauthorized(new { code = "DEVICE_AUTH_INVALID", message = "设备授权信息无效或已过期" });
        var result = await gateway.PrepareAsync(hardwareId, request, cancellationToken);
        if (!result.Success) return StatusCode(MapStatus(result.StatusCode, result.Code), new { code = result.Code, message = result.Message });
        var data = result.Data!;
        var manifest = data.ArtifactManifest with
        {
            Rustdesk = data.ArtifactManifest.Rustdesk with { DownloadUrl = "api/remote-maintenance/artifacts/rustdesk" },
            StatusAgent = data.ArtifactManifest.StatusAgent with { DownloadUrl = "api/remote-maintenance/artifacts/status-agent" }
        };
        return Ok(data with { ArtifactManifest = manifest });
    }

    [HttpPost("commit")]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Commit([FromBody] RemoteMaintenanceCommitRequest request, CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.RustdeskId) || string.IsNullOrWhiteSpace(request.ClientVersion) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { code = "REMOTE_MAINTENANCE_INVALID_REQUEST", message = "commit 参数不完整" });
        var hardwareId = await GetCurrentHardwareIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(hardwareId)) return Unauthorized(new { code = "DEVICE_AUTH_INVALID", message = "设备授权信息无效或已过期" });
        var result = await gateway.CommitAsync(hardwareId, request, cancellationToken);
        if (!result.Success) return StatusCode(MapStatus(result.StatusCode, result.Code), new { code = result.Code, message = result.Message });
        return Ok(result.Data);
    }

    [HttpGet("artifacts/{kind}")]
    public async Task<IActionResult> Artifact(string kind, CancellationToken cancellationToken)
    {
        var hardwareId = await GetCurrentHardwareIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(hardwareId)) return Unauthorized(new { code = "DEVICE_AUTH_INVALID", message = "设备授权信息无效或已过期" });
        var result = await gateway.DownloadArtifactAsync(hardwareId, kind, cancellationToken);
        if (!result.Success) return StatusCode(MapStatus(result.StatusCode, result.Code), new { code = result.Code, message = result.Message });
        Response.Headers.CacheControl = "no-store";
        return File(result.Data!, "application/octet-stream");
    }

    private static int MapStatus(int statusCode, string? code) => statusCode > 0 ? statusCode : code switch
    {
        "REMOTE_MAINTENANCE_DISABLED" or "REMOTE_MAINTENANCE_NOT_READY" => 503,
        "REMOTE_MAINTENANCE_PASSWORD_MISMATCH" or "REMOTE_MAINTENANCE_OPERATION_INVALID" => 409,
        _ => 400
    };

    private async Task<string?> GetCurrentHardwareIdAsync(CancellationToken cancellationToken)
    {
        var hardwareId = User.FindFirstValue(DeviceAuthConstants.HardwareIdClaim)?.Trim();
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(hardwareId)
            || !authorization.StartsWith(DeviceAuthConstants.BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var authorizationCode = authorization[DeviceAuthConstants.BearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(authorizationCode)) return null;
        var validated = await identityValidator.ValidateAsync(
            hardwareId,
            authorizationCode,
            User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim) ?? string.Empty,
            User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim) ?? string.Empty,
            cancellationToken);
        return validated?.HardwareId;
    }
}
