using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/pos-ipad/ota-releases")]
[Authorize]
public sealed class PosIpadOtaReleasesController(IPosIpadOtaPolicyService service)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> Get()
    {
        return Ok(await service.GetReleasesAsync());
    }

    [HttpPost("preflight")]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> Preflight(
        [FromBody] PosIpadOtaChannelPreflightRequest request
    )
    {
        return Ok(await service.PreflightReleaseChannelAsync(request));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> Create([FromBody] PosIpadOtaReleaseCreateRequest request)
    {
        return Ok(
            await service.CreateReleaseAsync(
                request,
                User.Identity?.Name ?? "System"
            )
        );
    }
}
