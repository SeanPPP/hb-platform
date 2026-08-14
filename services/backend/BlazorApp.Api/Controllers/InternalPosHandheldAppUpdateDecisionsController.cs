using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/internal/app-update-decisions/pos-handheld")]
[Authorize(
    AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
    Policy = ServiceApiScopes.ReadAppUpdateDecisions
)]
public sealed class InternalPosHandheldAppUpdateDecisionsController(
    IPosHandheldUpdateDecisionService service
) : ControllerBase
{
    [HttpPost("native")]
    public async Task<IActionResult> Native(
        [FromBody] PosHandheldNativeDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var decision = await service.GetNativeDecisionAsync(request, cancellationToken);
        if (decision is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<PosHandheldNativeDecisionDto>.Error(
                    "手持收银原生更新策略暂时不可用",
                    "POS_HANDHELD_NATIVE_DECISION_UNAVAILABLE"
                )
            );
        }

        return Ok(ApiResponse<PosHandheldNativeDecisionDto>.OK(decision));
    }

    [HttpPost("ota")]
    public async Task<IActionResult> Ota(
        [FromBody] PosHandheldOtaDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var decision = await service.GetOtaDecisionAsync(request, cancellationToken);
        if (decision is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<PosHandheldOtaDecisionDto>.Error(
                    "手持收银 OTA 更新策略暂时不可用",
                    "POS_HANDHELD_OTA_DECISION_UNAVAILABLE"
                )
            );
        }

        return Ok(ApiResponse<PosHandheldOtaDecisionDto>.OK(decision));
    }
}
