using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/app-ota-releases")]
[Authorize]
public sealed class AppOtaReleasesController(
    IAppOtaReleaseService service,
    IPosHandheldOtaLegacyBackfillService legacyBackfillService
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> List([FromQuery] AppOtaReleaseQuery query)
    {
        var response = await service.ListAsync(query);
        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpPost("preflight")]
    [Authorize(
        AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.PolicyScheme,
        Policy = Permissions.System.ManageAppDownloads
    )]
    public async Task<IActionResult> Preflight(
        [FromBody] AppOtaReleasePreflightRequest request
    )
    {
        var response = await service.PreflightAsync(request);
        return ToRegistrationResult(response);
    }

    [HttpPost("register")]
    [Authorize(
        AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.PolicyScheme,
        Policy = Permissions.System.ManageAppDownloads
    )]
    public async Task<IActionResult> Register(
        [FromBody] AppOtaReleaseRegisterRequest request
    )
    {
        var response = await service.RegisterAsync(
            request,
            User.Identity?.Name ?? "System"
        );
        return ToRegistrationResult(response);
    }

    [HttpPost("pos-handheld-legacy-backfill/prepare")]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> PreparePosHandheldLegacyBackfill()
    {
        // prepare 只读生成快照指纹，不会自动回填或激活策略。
        var response = await legacyBackfillService.PrepareAsync();
        return ToRegistrationResult(response);
    }

    [HttpPost("pos-handheld-legacy-backfill/apply")]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> ApplyPosHandheldLegacyBackfill(
        [FromBody] PosHandheldOtaLegacyBackfillApplyRequest request
    )
    {
        var response = await legacyBackfillService.ApplyAsync(
            request.PreparationFingerprint,
            User.Identity?.Name ?? "System"
        );
        return ToRegistrationResult(response);
    }

    private IActionResult ToRegistrationResult<T>(ApiResponse<T> response) =>
        response.Success
            ? Ok(response)
            : response.ErrorCode == AppOtaReleaseErrorCodes.FactConflict
                ? Conflict(response)
                : BadRequest(response);
}
