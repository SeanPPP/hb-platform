using BlazorApp.Api.Authentication;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/remote-maintenance")]
public sealed class RemoteMaintenanceController(
    RemoteMaintenanceService service,
    IConfiguration configuration) : ControllerBase
{
    private const string InternalKeyHeader = "X-HBPOS-Remote-Maintenance-Key";
    private const string AdminRoles = "Admin,管理员,SuperAdmin,超级管理员";

    [HttpGet("admin/devices")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> List([FromQuery] RemoteMaintenanceDeviceQueryDto query, CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(query, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("admin/devices/{id:guid}/credential")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> Credential(Guid id, CancellationToken cancellationToken)
    {
        // 审计主体优先记录稳定用户 ID；用户名仅作为旧认证票据的兼容回退。
        var actor = User.FindFirst("userId")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name
            ?? "unknown";
        var result = await service.GetCredentialAsync(id, actor, cancellationToken);
        if (result.Success)
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
        }
        return ToActionResult(result);
    }

    [HttpGet("admin/manifest")]
    [Authorize(Roles = AdminRoles)]
    public IActionResult Manifest() => ToActionResult(service.GetManifest());

    [HttpGet("admin/artifacts/{kind}")]
    [Authorize(Roles = AdminRoles)]
    public IActionResult Artifact(string kind)
    {
        var result = service.GetArtifactFile(kind);
        if (!result.Success) return ToActionResult(result);
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(result.Data!.Path, "application/octet-stream", result.Data.FileName, enableRangeProcessing: true);
    }

    [HttpGet("artifacts/{kind}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeviceArtifact(string kind)
    {
        if (!HasInternalKey()) return Unauthorized(new { code = "REMOTE_MAINTENANCE_INTERNAL_AUTH_REQUIRED" });
        var hardwareId = Request.Headers["X-HBPOS-Hardware-Id"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(hardwareId)) return BadRequest(new { code = "REMOTE_MAINTENANCE_HARDWARE_ID_REQUIRED" });
        var result = await service.GetArtifactFileForHardwareAsync(kind, hardwareId, HttpContext.RequestAborted);
        if (!result.Success) return ToActionResult(result);
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(result.Data!.Path, "application/octet-stream", result.Data.FileName, enableRangeProcessing: true);
    }

    [HttpPost("prepare")]
    [AllowAnonymous]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Prepare([FromBody] RemoteMaintenancePrepareRequestDto request, CancellationToken cancellationToken)
    {
        if (!HasInternalKey()) return Unauthorized(new { code = "REMOTE_MAINTENANCE_INTERNAL_AUTH_REQUIRED" });
        var hardwareId = Request.Headers["X-HBPOS-Hardware-Id"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(hardwareId)) return BadRequest(new { code = "REMOTE_MAINTENANCE_HARDWARE_ID_REQUIRED" });
        var result = await service.PrepareAsync(new(request.OperationId, hardwareId, request.ComputerName), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("commit")]
    [AllowAnonymous]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Commit([FromBody] RemoteMaintenanceCommitRequestDto request, CancellationToken cancellationToken)
    {
        if (!HasInternalKey()) return Unauthorized(new { code = "REMOTE_MAINTENANCE_INTERNAL_AUTH_REQUIRED" });
        var hardwareId = Request.Headers["X-HBPOS-Hardware-Id"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(hardwareId)) return BadRequest(new { code = "REMOTE_MAINTENANCE_HARDWARE_ID_REQUIRED" });
        var result = await service.CommitAsync(new(request.OperationId, hardwareId, request.RustdeskId, request.ClientVersion, request.Password), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("devices/{deviceId:guid}/heartbeat")]
    [AllowAnonymous]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Heartbeat(Guid deviceId, [FromBody] RemoteMaintenanceHeartbeatRequestDto request, CancellationToken cancellationToken)
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Unauthorized(new { code = "REMOTE_MAINTENANCE_TOKEN_REQUIRED" });
        var token = authorization["Bearer ".Length..].Trim();
        var result = await service.HeartbeatAsync(deviceId, token, request, cancellationToken);
        return ToActionResult(result);
    }

    private bool HasInternalKey()
    {
        var expected = configuration["RemoteMaintenance:InterApiKey"];
        var actual = Request.Headers[InternalKeyHeader].ToString();
        return !string.IsNullOrWhiteSpace(expected) &&
            !string.IsNullOrWhiteSpace(actual) &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(actual));
    }

    private IActionResult ToActionResult<T>(RemoteMaintenanceResult<T> result)
    {
        if (result.Success) return Ok(result.Data);
        var status = result.Code switch
        {
            "REMOTE_MAINTENANCE_DISABLED" or "REMOTE_MAINTENANCE_NOT_READY" => StatusCodes.Status503ServiceUnavailable,
            "REMOTE_MAINTENANCE_DEVICE_NOT_FOUND" => StatusCodes.Status404NotFound,
            "REMOTE_MAINTENANCE_SEQUENCE_REJECTED" or "REMOTE_MAINTENANCE_OPERATION_INVALID" or "REMOTE_MAINTENANCE_PASSWORD_MISMATCH" => StatusCodes.Status409Conflict,
            "REMOTE_MAINTENANCE_TOKEN_INVALID" => StatusCodes.Status401Unauthorized,
            "REMOTE_MAINTENANCE_DEVICE_DISABLED" => StatusCodes.Status403Forbidden,
            "REMOTE_MAINTENANCE_RATE_LIMITED" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, new { code = result.Code, message = result.Message });
    }
}
