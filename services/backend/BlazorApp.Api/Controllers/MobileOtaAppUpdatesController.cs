using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/app-updates/mobile-ota")]
[AllowAnonymous]
public sealed class MobileOtaAppUpdatesController(IMobileOtaPolicyService service)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] MobileOtaDecisionRequest request)
    {
        if (
            !MobileOtaPolicyService.TryNormalizeDecisionLane(
                request.ClientChannel,
                request.Platform,
                out _,
                out _,
                out _
            )
            || string.IsNullOrWhiteSpace(request.RuntimeVersion)
        )
        {
            return BadRequest(
                ApiResponse<MobileOtaDecisionDto>.Error(
                    "Mobile OTA 决策请求身份无效",
                    "MOBILE_OTA_DECISION_REQUEST_INVALID"
                )
            );
        }

        var decision = await service.GetDecisionAsync(request);
        if (decision is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<MobileOtaDecisionDto>.Error(
                    "Mobile OTA required 策略目标暂时不可用",
                    "MOBILE_OTA_REQUIRED_TARGET_UNAVAILABLE"
                )
            );
        }

        // 客户端合同固定为十一字段，成功响应不包 ApiResponse envelope。
        return Ok(decision);
    }
}
