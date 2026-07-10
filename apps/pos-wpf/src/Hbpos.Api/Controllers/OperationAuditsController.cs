using System.Security.Claims;
using BlazorApp.Shared.DTOs;
using Hbpos.Api.Services;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/operation-audits")]
public sealed class OperationAuditsController(IOperationAuditIngestService ingestService) : ControllerBase
{
    internal const int MaximumBatchSize = 100;
    internal const long MaximumRequestBytes = 4L * 1024 * 1024;

    [HttpPost("batch")]
    [RequestSizeLimit(MaximumRequestBytes)]
    public async Task<ActionResult<OperationAuditBatchResultDto>> Batch(
        [FromBody] OperationAuditBatchRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { code = "DEVICE_AUTH_REQUIRED", message = "Device authorization is required." });
        }

        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        if (string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(deviceCode))
        {
            return Unauthorized(new { code = "DEVICE_AUTH_REQUIRED", message = "Device scope claims are required." });
        }

        if (Request.ContentLength > MaximumRequestBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new
            {
                code = "PAYLOAD_TOO_LARGE",
                message = "Request body must not exceed 4 MiB."
            });
        }

        if (request?.Events is null || request.Events.Count == 0)
        {
            return BadRequest(new { code = "EVENTS_REQUIRED", message = "At least one event is required." });
        }

        if (request.Events.Count > MaximumBatchSize)
        {
            return BadRequest(new { code = "BATCH_TOO_LARGE", message = "A batch can contain at most 100 events." });
        }

        if (request.Events.Any(static item => item is null))
        {
            return BadRequest(new { code = "EVENT_REQUIRED", message = "Batch events cannot contain null." });
        }

        // 门店和终端以认证 claims 为准；请求体只允许完全匹配，不能被客户端静默改写。
        if (request.Events.Any(item =>
                !string.Equals(item.StoreCode, storeCode, StringComparison.Ordinal) ||
                !string.Equals(item.DeviceCode, deviceCode, StringComparison.Ordinal)))
        {
            return Forbid();
        }

        var result = await ingestService.IngestAsync(
            request,
            storeCode,
            deviceCode,
            cancellationToken);
        return Ok(result);
    }
}
