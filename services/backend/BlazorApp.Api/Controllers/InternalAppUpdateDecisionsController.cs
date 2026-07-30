using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/internal/app-update-decisions/pos-ipad")]
[Authorize(AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.AuthenticationScheme)]
public sealed class InternalAppUpdateDecisionsController(
    INativeAppUpdatePolicyService nativeService,
    IPosIpadOtaPolicyService otaService
) : ControllerBase
{
    [HttpPost("native")]
    public async Task<IActionResult> Native([FromBody] PosIpadNativeDecisionRequest request)
    {
        var decision = await nativeService.GetPosIpadNativeDecisionAsync(request);
        return Ok(ApiResponse<NativeAppUpdateDecisionDto>.OK(decision));
    }

    [HttpPost("ota")]
    public async Task<IActionResult> Ota([FromBody] PosIpadOtaDecisionRequest request)
    {
        var decision = await otaService.GetDecisionAsync(request);
        return Ok(ApiResponse<PosIpadOtaDecisionDto>.OK(decision));
    }
}
