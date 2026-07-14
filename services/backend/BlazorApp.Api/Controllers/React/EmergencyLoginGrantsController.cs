using System.Security.Claims;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/emergency-login-grants")]
[Authorize]
public sealed class EmergencyLoginGrantsController : ControllerBase
{
    private readonly EmergencyLoginGrantService _service;

    public EmergencyLoginGrantsController(EmergencyLoginGrantService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.DeviceRegistration.Manage)]
    [Authorize(Policy = Permissions.System.ManageSettings)]
    public async Task<IActionResult> List([FromQuery] string? storeCode)
    {
        return Ok(await _service.ListAsync(storeCode));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.DeviceRegistration.Manage)]
    [Authorize(Policy = Permissions.System.ManageSettings)]
    public async Task<IActionResult> Create([FromBody] EmergencyLoginGrantCreateRequestDto request)
    {
        return Ok(await _service.CreateAsync(request, ResolveActor()));
    }

    [HttpPost("{grantId:guid}/revoke")]
    [Authorize(Policy = Permissions.DeviceRegistration.Manage)]
    [Authorize(Policy = Permissions.System.ManageSettings)]
    public async Task<IActionResult> Revoke(
        Guid grantId,
        [FromBody] EmergencyLoginGrantRevokeRequestDto request
    )
    {
        return Ok(await _service.RevokeAsync(grantId, request, ResolveActor()));
    }

    private string ResolveActor() =>
        User.Identity?.Name
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("userId")?.Value
        ?? "System";
}
