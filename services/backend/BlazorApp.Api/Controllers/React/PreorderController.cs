using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/preorders")]
[Authorize]
public sealed class PreorderController : ControllerBase
{
    private static readonly string[] ReadPermissions =
    {
        Permissions.OrderFront.View,
        Permissions.Orders.View,
        Permissions.Warehouse.ManageOrders,
    };
    private static readonly string[] WritePermissions =
    {
        Permissions.OrderFront.View,
        Permissions.Orders.Create,
        Permissions.Warehouse.ManageOrders,
    };

    private readonly IPreorderReactService _service;
    private readonly IAuthorizationService _authorization;

    public PreorderController(IPreorderReactService service, IAuthorizationService authorization)
    {
        _service = service;
        _authorization = authorization;
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive([FromQuery] string storeCode)
    {
        if (!await HasAnyPermissionAsync(ReadPermissions)) return Forbid();
        return await ExecuteAsync(() => _service.GetActiveAsync(storeCode));
    }

    [HttpGet("activations/{activationGuid}")]
    public async Task<IActionResult> GetActivation(
        string activationGuid,
        [FromQuery] string storeCode
    )
    {
        if (!await HasAnyPermissionAsync(ReadPermissions)) return Forbid();
        return await ExecuteAsync(() => _service.GetActivationAsync(activationGuid, storeCode));
    }

    [HttpPut("activations/{activationGuid}/draft")]
    public async Task<IActionResult> SaveDraft(
        string activationGuid,
        [FromBody] SavePreorderDraftDto request
    )
    {
        if (!await HasAnyPermissionAsync(WritePermissions)) return Forbid();
        return await ExecuteAsync(() => _service.SaveDraftAsync(activationGuid, request), "草稿已保存");
    }

    [HttpPost("activations/{activationGuid}/submit")]
    public async Task<IActionResult> Submit(
        string activationGuid,
        [FromBody] SubmitPreorderDto request
    )
    {
        if (!await HasAnyPermissionAsync(WritePermissions)) return Forbid();
        return await ExecuteAsync(() => _service.SubmitAsync(activationGuid, request), "Preorder 已提交");
    }

    private async Task<bool> HasAnyPermissionAsync(IEnumerable<string> policies)
    {
        foreach (var policy in policies)
        {
            if ((await _authorization.AuthorizeAsync(User, null, policy)).Succeeded)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action, string message = "操作成功")
    {
        try
        {
            return Ok(ApiResponse<T>.OK(await action(), message));
        }
        catch (PreorderBusinessException ex)
        {
            return StatusCode(
                ex.StatusCode,
                ApiResponse<T>.Error(ex.Message, ex.ErrorCode, ex.Details)
            );
        }
    }
}
