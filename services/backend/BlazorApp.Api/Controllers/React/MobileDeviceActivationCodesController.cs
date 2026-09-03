using System.Security.Claims;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/mobile-device-activation-codes")]
[Authorize(Policy = Permissions.DeviceRegistration.MobileActivationCodes.Manage)]
public sealed class MobileDeviceActivationCodesController(
    IMobileDeviceActivationCodeManagementService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string? storeCode = null,
        [FromQuery] string? deviceSystem = null,
        [FromQuery] string? status = null) =>
        Ok(await service.ListAsync(page, pageSize, storeCode, deviceSystem, status));

    [HttpGet("manageable-stores")]
    public async Task<IActionResult> ManageableStores() =>
        Ok(await service.GetManageableStoresAsync());

    [HttpGet("manageable-accounts")]
    public async Task<IActionResult> ManageableAccounts([FromQuery] string? storeCode) =>
        Ok(await service.GetManageableAccountsAsync(storeCode));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] MobileDeviceActivationCodeCreateRequestDto request)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.CreateAsync(request, ResolveActor()));
    }

    [HttpPost("{grantId:guid}/revoke")]
    public async Task<IActionResult> Revoke(
        Guid grantId,
        [FromBody] MobileDeviceActivationCodeRevokeRequestDto request) =>
        Ok(await service.RevokeAsync(grantId, request, ResolveActor()));

    private string ResolveActor() =>
        User.Identity?.Name
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("userId")?.Value
        ?? "System";
}
