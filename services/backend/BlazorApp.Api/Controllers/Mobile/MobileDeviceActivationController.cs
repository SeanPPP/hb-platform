using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlazorApp.Api.Controllers.Mobile;

[ApiController]
[Route("api/mobile/v1/device-activation")]
[RequestSizeLimit(4096)]
public sealed class MobileDeviceActivationController(
    IMobileDeviceActivationService service) : ControllerBase
{
    [HttpPost("preview")]
    [AllowAnonymous]
    [EnableRateLimiting(MobileDeviceActivationRateLimits.AnonymousMutationPolicy)]
    public async Task<IActionResult> Preview(
        [FromBody] MobileDeviceActivationPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.PreviewAsync(request, cancellationToken));
    }

    [HttpPost("redeem")]
    [AllowAnonymous]
    [EnableRateLimiting(MobileDeviceActivationRateLimits.AnonymousMutationPolicy)]
    public async Task<IActionResult> Redeem(
        [FromBody] MobileDeviceActivationRedeemRequestDto request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.RedeemAsync(
            request,
            IsRecoveryOnlyRequest(),
            cancellationToken));
    }

    // 正常重绑必须携带有效 deviceAccount JWT；结果不确定的恢复请求改用旧绑定凭据精确恢复。
    [HttpPost("rebind")]
    [AllowAnonymous]
    [EnableRateLimiting(MobileDeviceActivationRateLimits.AnonymousMutationPolicy)]
    public async Task<IActionResult> Rebind(
        [FromBody] MobileDeviceActivationRebindRequestDto request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        MobileDeviceBindingContext? context = null;
        if (MobileDeviceBindingContextResolver.TryResolve(User, out var resolved))
        {
            context = resolved;
        }
        return Ok(await service.RebindAsync(
            request,
            context,
            IsRecoveryOnlyRequest(),
            cancellationToken));
    }

    private bool IsRecoveryOnlyRequest() =>
        Request.Headers.TryGetValue(MobileDeviceActivationHeaders.RecoveryOnly, out var value)
        && bool.TryParse(value.ToString(), out var enabled)
        && enabled;
}
