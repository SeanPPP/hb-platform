using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/app-update-policies")]
[Authorize]
public sealed class AppUpdatePoliciesController(
    INativeAppUpdatePolicyService service,
    IPosHandheldUpdatePolicyService? posHandheldService = null
)
    : ControllerBase
{
    [HttpGet("mobile-ios")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetMobileIos()
    {
        return Ok(await service.GetMobileIosPolicyAsync());
    }

    [HttpPut("mobile-ios")]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> PutMobileIos([FromBody] NativeUpdatePolicyRequest request)
    {
        var response = await service.SetMobileIosPolicyAsync(
            request,
            User.Identity?.Name ?? "System"
        );
        return ToMutationResult(response);
    }

    [HttpGet("pos-ipad/native")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetPosIpadNative()
    {
        return Ok(await service.GetPosIpadNativePolicyAsync());
    }

    [HttpPut("pos-ipad/native")]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> PutPosIpadNative(
        [FromBody] PosIpadNativeUpdatePolicyRequest request
    )
    {
        var response = await service.SetPosIpadNativePolicyAsync(
            request,
            User.Identity?.Name ?? "System"
        );
        return ToMutationResult(response);
    }

    [HttpGet("pos-ipad/store-options")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetPosIpadStoreOptions()
    {
        return Ok(await service.GetStoreOptionsAsync());
    }

    [HttpGet("pos-handheld")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetPosHandheld()
    {
        return Ok(await PosHandheldService.GetPoliciesAsync());
    }

    [HttpGet("pos-handheld/candidates/native/android")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetPosHandheldAndroidCandidates()
    {
        return Ok(
            await PosHandheldService.GetCandidatesAsync(
                PosHandheldUpdateLanes.AndroidNative
            )
        );
    }

    [HttpGet("pos-handheld/candidates/native/ios")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetPosHandheldIosCandidates()
    {
        return Ok(
            await PosHandheldService.GetCandidatesAsync(
                PosHandheldUpdateLanes.IosNative
            )
        );
    }

    [HttpGet("pos-handheld/candidates/ota")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetPosHandheldOtaCandidates(
        [FromQuery] string platform
    )
    {
        var lane = string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase)
            ? PosHandheldUpdateLanes.AndroidOta
            : string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase)
                ? PosHandheldUpdateLanes.IosOta
                : platform;
        return Ok(await PosHandheldService.GetCandidatesAsync(lane));
    }

    [HttpPut("pos-handheld/{lane}")]
    [Authorize(Policy = Permissions.System.ManageAppDownloads)]
    public async Task<IActionResult> PutPosHandheldLane(
        string lane,
        [FromBody] PosHandheldUpdatePolicyRequest request
    )
    {
        var response = await PosHandheldService.SetLaneAsync(
            lane,
            request,
            User.Identity?.Name ?? "System"
        );
        return ToMutationResult(response);
    }

    [HttpGet("pos-handheld/revisions")]
    [Authorize(Policy = Permissions.System.ViewAppDownloads)]
    public async Task<IActionResult> GetPosHandheldRevisions([FromQuery] string lane)
    {
        return Ok(await PosHandheldService.GetRevisionsAsync(lane));
    }

    private IPosHandheldUpdatePolicyService PosHandheldService =>
        posHandheldService
        ?? throw new InvalidOperationException("手持 POS 更新策略服务未注册");

    private IActionResult ToMutationResult<T>(ApiResponse<T> response) =>
        response.ErrorCode
            is AppUpdatePolicyErrorCodes.VersionRequired
                or AppUpdatePolicyErrorCodes.VersionConflict
            ? Conflict(response)
            : Ok(response);
}
