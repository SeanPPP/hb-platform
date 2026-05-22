using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("health")]
    [AllowAnonymous]
    public ActionResult<ApiResult<HealthCheckResponse>> Get()
    {
        return Ok(ApiResult<HealthCheckResponse>.Ok(
            new HealthCheckResponse(
                IsOnline: true,
                ServerTime: DateTimeOffset.UtcNow,
                Status: "ok")));
    }
}
