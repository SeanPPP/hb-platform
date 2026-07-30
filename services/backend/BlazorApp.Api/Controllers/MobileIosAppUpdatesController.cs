using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/app-updates/mobile-ios")]
public sealed class MobileIosAppUpdatesController(INativeAppUpdatePolicyService service)
    : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Check(
        [FromQuery] string? version,
        [FromQuery] string? build
    )
    {
        var decision = await service.GetMobileIosDecisionAsync(version, build);
        return Ok(ApiResponse<NativeAppUpdateDecisionDto>.OK(decision));
    }
}
