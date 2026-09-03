using System.Security.Claims;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlazorApp.Api.Controllers.Mobile;

[ApiController]
[Route("api/mobile/v1")]
[RequestSizeLimit(4096)]
public sealed class MobileDeviceSessionController(
    IMobileDeviceActivationService service) : ControllerBase
{
    [HttpPost("device-session/exchange")]
    [AllowAnonymous]
    [EnableRateLimiting(MobileDeviceActivationRateLimits.SessionExchangePolicy)]
    public async Task<IActionResult> Exchange(
        [FromBody] MobileDeviceSessionExchangeRequestDto request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.ExchangeSessionAsync(request, cancellationToken));
    }

    [HttpPost("device-binding/unbind")]
    [Authorize]
    public async Task<IActionResult> Unbind(
        [FromBody] MobileDeviceUnbindRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!MobileDeviceBindingContextResolver.TryResolve(User, out var context))
        {
            return Forbid();
        }
        return Ok(await service.UnbindAsync(
            context,
            request,
            ResolveActor(),
            cancellationToken));
    }

    private string ResolveActor() =>
        User.Identity?.Name
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "MobileDevice";
}
