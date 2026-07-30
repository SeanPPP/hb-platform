using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/app-update-releases/ios")]
[Authorize]
public sealed class IosAppStoreReleasesController(IIosAppStoreReleaseService service)
    : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> Get([FromQuery] IosAppStoreReleaseQuery query)
    {
        return Ok(await service.GetAsync(query));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> Create(
        [FromBody] IosAppStoreReleaseCreateRequest request,
        CancellationToken cancellationToken
    )
    {
        return Ok(
            await service.CreateAsync(
                request,
                User.Identity?.Name ?? "System",
                cancellationToken
            )
        );
    }
}
