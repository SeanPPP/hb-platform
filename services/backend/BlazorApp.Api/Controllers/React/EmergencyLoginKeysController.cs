using System.Security.Claims;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/emergency-login-keys")]
[Authorize]
public sealed class EmergencyLoginKeysController : ControllerBase
{
    private readonly EmergencyLoginKeyManagementService _service;

    public EmergencyLoginKeysController(EmergencyLoginKeyManagementService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.System.ManageSettings)]
    public async Task<IActionResult> List() => Ok(await _service.ListAsync());

    [HttpPost("generate")]
    [Authorize(Policy = Permissions.System.ManageSettings)]
    public async Task<IActionResult> Generate([FromBody] EmergencyLoginKeyGenerateRequestDto request) =>
        ToActionResult(await _service.GenerateAsync(request, ResolveActor()));

    [HttpPost("{kid}/activate")]
    [Authorize(Policy = Permissions.System.ManageSettings)]
    public async Task<IActionResult> Activate(
        string kid,
        [FromBody] EmergencyLoginKeyActivateRequestDto request
    ) => ToActionResult(await _service.ActivateAsync(kid, request, ResolveActor()));

    [HttpPost("{kid}/retire")]
    [Authorize(Policy = Permissions.System.ManageSettings)]
    public async Task<IActionResult> Retire(
        string kid,
        [FromBody] EmergencyLoginKeyRetireRequestDto request
    ) => ToActionResult(await _service.RetireAsync(kid, request, ResolveActor()));

    private IActionResult ToActionResult(ApiResponse<EmergencyLoginKeyMutationDto> response) =>
        response.ErrorCode == "EMERGENCY_KEY_VERSION_CONFLICT"
            ? Conflict(response)
            : Ok(response);

    private string ResolveActor() =>
        User.Identity?.Name
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("userId")?.Value
        ?? "System";
}
