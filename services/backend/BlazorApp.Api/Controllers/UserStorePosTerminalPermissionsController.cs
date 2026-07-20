using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Authorize(Policy = Permissions.Users.ManagePosTerminalPermissions)]
[Route("api/Users/guid/{userGuid}/stores/{storeGuid}/pos-terminal-permissions")]
public sealed class UserStorePosTerminalPermissionsController(
    IUserStorePosTerminalPermissionService service
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string userGuid, string storeGuid) =>
        ToActionResult(await service.GetAsync(userGuid, storeGuid));

    [HttpPut]
    public async Task<IActionResult> Put(
        string userGuid,
        string storeGuid,
        [FromBody] UpdateUserStorePosTerminalPermissionsRequest request
    ) => ToActionResult(await service.UpdateAsync(userGuid, storeGuid, request));

    [HttpDelete]
    public async Task<IActionResult> Delete(string userGuid, string storeGuid) =>
        ToActionResult(await service.DeleteAsync(userGuid, storeGuid));

    private IActionResult ToActionResult(
        ApiResponse<UserStorePosTerminalPermissionsResponse> response
    )
    {
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            "POS_PERMISSION_FORBIDDEN" or "EMPLOYEE_TARGET_REQUIRED" => Forbid(),
            "POS_PERMISSION_NOT_FOUND" => NotFound(response),
            _ => BadRequest(response),
        };
    }
}
