using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/app-update-policies")]
[Authorize]
public sealed class AppUpdatePoliciesController(INativeAppUpdatePolicyService service)
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

    private IActionResult ToMutationResult<T>(ApiResponse<T> response) =>
        response.ErrorCode
            is AppUpdatePolicyErrorCodes.VersionRequired
                or AppUpdatePolicyErrorCodes.VersionConflict
            ? Conflict(response)
            : Ok(response);
}
