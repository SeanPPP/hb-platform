using Hbpos.Api.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/app-update")]
public sealed class AppUpdateController(ILocalAppUpdateService updateService) : ControllerBase
{
    [HttpGet("check")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AppUpdateCheckResponse>>> Check(
        [FromQuery] string currentVersion,
        [FromQuery] string channel = "production",
        CancellationToken cancellationToken = default)
    {
        var response = await updateService.CheckAsync(
            new AppUpdateCheckRequest
            {
                CurrentVersion = currentVersion,
                Channel = channel
            },
            cancellationToken);

        return Ok(ApiResult<AppUpdateCheckResponse>.Ok(response));
    }
}
