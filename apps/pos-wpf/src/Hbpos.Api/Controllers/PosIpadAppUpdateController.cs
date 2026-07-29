using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/app-updates/pos-ipad")]
[Authorize(AuthenticationSchemes = DeviceAuthConstants.Scheme)]
public sealed class PosIpadAppUpdateController(
    IOptions<PosIpadOptions> options) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResult<PosIpadAppUpdateResponse>> Check(
        [FromQuery] string? version,
        [FromQuery] string? build,
        [FromQuery] string? runtimeVersion)
    {
        var configuration = options.Value;
        var response = new PosIpadAppUpdateResponse(
            // 兼容仍读取 enabled 的旧客户端；设备鉴权成功后即可交易。
            Enabled: true,
            configuration.MinimumSupportedVersion,
            configuration.LatestVersion,
            PosIpadAppVersionPolicy.IsForceUpdateRequired(
                configuration,
                version,
                build,
                runtimeVersion),
            configuration.AppStoreUrl,
            configuration.ReleaseMessage);

        return Ok(ApiResult<PosIpadAppUpdateResponse>.Ok(response));
    }
}
