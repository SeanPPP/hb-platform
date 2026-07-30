using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/pos-ipad/ota-rollout")]
[Authorize]
public sealed class PosIpadOtaRolloutController(IPosIpadOtaPolicyService service)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> Get()
    {
        return Ok(await service.GetRolloutAsync());
    }

    [HttpPut]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> Put([FromBody] PosIpadOtaRolloutRequest request)
    {
        var response = await service.SetRolloutAsync(
            request,
            User.Identity?.Name ?? "System"
        );
        return response.ErrorCode
            is AppUpdatePolicyErrorCodes.VersionRequired
                or AppUpdatePolicyErrorCodes.VersionConflict
            ? Conflict(response)
            : Ok(response);
    }
}
