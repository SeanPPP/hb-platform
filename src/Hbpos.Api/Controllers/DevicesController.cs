using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/devices")]
public sealed class DevicesController(IDeviceService deviceService) : ControllerBase
{
    [HttpPost("verify")]
    public async Task<ActionResult<ApiResult<DeviceVerifyResponse>>> Verify(
        [FromBody] DeviceVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var response = await deviceService.VerifyAsync(request, cancellationToken);
        return response.IsAllowed
            ? Ok(ApiResult<DeviceVerifyResponse>.Ok(response))
            : Ok(ApiResult<DeviceVerifyResponse>.Fail("DEVICE_NOT_ALLOWED", response.Message ?? "设备不可用"));
    }
}
