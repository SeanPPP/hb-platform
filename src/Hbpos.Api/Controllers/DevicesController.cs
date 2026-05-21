using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/devices")]
public sealed class DevicesController(IDeviceService deviceService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<ApiResult<DeviceRegisterResponse>>> Register(
        [FromBody] DeviceRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var response = await deviceService.RegisterAsync(request, cancellationToken);
        return Ok(ApiResult<DeviceRegisterResponse>.Ok(response));
    }

    [HttpPost("verify")]
    public async Task<ActionResult<ApiResult<DeviceVerifyResponse>>> Verify(
        [FromBody] DeviceVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await deviceService.VerifyAsync(request, cancellationToken);
        return Ok(ApiResult<DeviceVerifyResponse>.Ok(response));
    }
}
