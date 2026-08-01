using System.Security.Claims;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Linkly;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = DeviceAuthConstants.Scheme)]
[Route("api/v1/linkly/settlements")]
public sealed class LinklySettlementsController(
    ILinklySettlementSyncService syncService) : ControllerBase
{
    internal const long MaximumRequestBytes = 1024 * 1024;

    [HttpPost("sync")]
    [RequestSizeLimit(MaximumRequestBytes)]
    public async Task<ActionResult<LinklySettlementSyncResponse>> Sync(
        [FromBody] LinklySettlementSyncRequest request,
        CancellationToken cancellationToken)
    {
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        if (User.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(storeCode) ||
            string.IsNullOrWhiteSpace(deviceCode))
        {
            return Unauthorized(new
            {
                code = "DEVICE_AUTH_REQUIRED",
                message = "Authenticated device scope claims are required."
            });
        }

        if (!string.Equals(request.StoreCode, storeCode, StringComparison.Ordinal) ||
            !string.Equals(request.DeviceCode, deviceCode, StringComparison.Ordinal))
        {
            // 上传范围只信任认证 claims；拒绝静默改写，避免错误设备的离线记录落入当前 scope。
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "DEVICE_SCOPE_FORBIDDEN",
                message = "Settlement scope does not match the authenticated device."
            });
        }

        try
        {
            return Ok(await syncService.SyncAsync(
                request,
                storeCode,
                deviceCode,
                cancellationToken));
        }
        catch (LinklySettlementValidationException ex)
        {
            return BadRequest(new { code = ex.Code, message = ex.Message });
        }
        catch (LinklySettlementConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Message });
        }
    }
}
