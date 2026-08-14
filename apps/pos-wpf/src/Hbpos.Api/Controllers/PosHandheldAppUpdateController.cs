using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.AppUpdates;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/app-updates/pos-handheld")]
[Authorize(AuthenticationSchemes = DeviceAuthConstants.Scheme)]
public sealed class PosHandheldAppUpdateController(
    IPosHandheldUpdateDecisionGateway gateway
) : ControllerBase
{
    private const long JavaScriptSafeIntegerMax = 9007199254740991;
    private const string AllowTransactionsHeaderName = "X-HBPOS-Allow-Transactions";

    [HttpGet]
    public async Task<ActionResult<ApiResult<PosHandheldNativeUpdateResponse>>> Check(
        [FromQuery] string? version,
        [FromQuery] string? build,
        CancellationToken cancellationToken)
    {
        if (!TryGetDeviceScope(out var storeCode, out var platform, out var error))
        {
            return error!;
        }

        if (!IsCanonicalCurrentBuild(build))
        {
            return BadRequest(
                ApiResult<PosHandheldNativeUpdateResponse>.Fail(
                    "POS_HANDHELD_CURRENT_BUILD_INVALID",
                    "Handheld current build must be a canonical positive integer."
                )
            );
        }

        var decision = await gateway.GetNativeDecisionAsync(
            new PosHandheldNativeUpdateDecisionRequest(storeCode, platform, version, build),
            cancellationToken
        );
        if (decision is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<PosHandheldNativeUpdateResponse>.Fail(
                    "POS_HANDHELD_NATIVE_DECISION_UNAVAILABLE",
                    "Handheld native update decision is temporarily unavailable."
                )
            );
        }

        // 保持旧手持端严格校验的响应体不变；新客户端从认证响应头读取交易权限。
        Response.Headers[AllowTransactionsHeaderName] = AllowsTransactions()
            ? "true"
            : "false";
        return Ok(ApiResult<PosHandheldNativeUpdateResponse>.Ok(decision));
    }

    [HttpGet("ota")]
    public async Task<ActionResult<ApiResult<PosHandheldOtaUpdateResponse>>> CheckOta(
        [FromQuery] string? runtimeVersion,
        [FromQuery] string? currentUpdateId,
        [FromQuery] string? currentUpdateGroupId,
        CancellationToken cancellationToken)
    {
        if (!TryGetDeviceScope(out var storeCode, out var platform, out var error))
        {
            var nativeError = (ApiResult<PosHandheldNativeUpdateResponse>)error!.Value!;
            var otaError = ApiResult<PosHandheldOtaUpdateResponse>.Fail(
                nativeError.ErrorCode ?? "POS_HANDHELD_SCOPE_INVALID",
                nativeError.Message ?? "Authenticated handheld scope is invalid."
            );
            return error.StatusCode == StatusCodes.Status401Unauthorized
                ? Unauthorized(otaError)
                : BadRequest(otaError);
        }

        var decision = await gateway.GetOtaDecisionAsync(
            new PosHandheldOtaUpdateDecisionRequest(
                storeCode,
                platform,
                runtimeVersion,
                currentUpdateId,
                currentUpdateGroupId
            ),
            cancellationToken
        );
        if (decision is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<PosHandheldOtaUpdateResponse>.Fail(
                    "POS_HANDHELD_OTA_DECISION_UNAVAILABLE",
                    "Handheld OTA update decision is temporarily unavailable."
                )
            );
        }

        return Ok(ApiResult<PosHandheldOtaUpdateResponse>.Ok(decision));
    }

    private bool TryGetDeviceScope(
        out string storeCode,
        out string platform,
        out ObjectResult? error)
    {
        storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim)?.Trim()
            ?? string.Empty;
        platform = User.FindFirstValue(DeviceAuthConstants.DeviceSystemClaim)?.Trim()
            ?? string.Empty;
        if (storeCode.Length == 0)
        {
            error = Unauthorized(
                ApiResult<PosHandheldNativeUpdateResponse>.Fail(
                    "DEVICE_STORE_SCOPE_REQUIRED",
                    "Authenticated POS device store scope is required."
                )
            );
            return false;
        }

        if (!DeviceSystems.TryNormalize(platform, out var normalized)
            || normalized is not (DeviceSystems.Ios or DeviceSystems.Android))
        {
            error = BadRequest(
                ApiResult<PosHandheldNativeUpdateResponse>.Fail(
                    "POS_HANDHELD_PLATFORM_REQUIRED",
                    "Authenticated device must use iOS or Android."
                )
            );
            return false;
        }

        platform = normalized;
        error = null;
        return true;
    }

    private static bool IsCanonicalCurrentBuild(string? value)
    {
        if (value is not { Length: > 0 and <= 16 } || value[0] == '0')
        {
            return false;
        }

        // 请求边界必须在转发前拒绝非规范 build，避免中心服务把它解释为低版本。
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return long.TryParse(value, out var build)
            && build is > 0 and <= JavaScriptSafeIntegerMax;
    }

    private bool AllowsTransactions()
    {
        var claimValue = HttpContext?.User.FindFirstValue(
            DeviceAuthConstants.AllowTransactionsClaim);
        // 兼容没有该 claim 的旧认证票据；只有明确 false 才关闭新交易。
        return !bool.TryParse(claimValue, out var allowed) || allowed;
    }
}
