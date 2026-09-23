using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/cash-register-users")]
    [Authorize]
    public class ReactCashRegisterUserController : ControllerBase
    {
        private readonly ICashRegisterUserReactService _service;
        private readonly IAuthorizationService _authorizationService;

        // 关键逻辑：Web 仍只认 Store.ManageOperations；移动端用独立权限码，管理与打印可分别授予。
        // 读取（列表/详情）三者任一即可，写入只认 Web 权限或移动端管理权限，打印确认只认移动端打印权限。
        internal static readonly string[] ReadPermissions =
        {
            Permissions.Store.ManageOperations,
            Permissions.CashRegisterUsers.MobileManage,
            Permissions.CashRegisterUsers.MobilePrint,
        };

        internal static readonly string[] ManagePermissions =
        {
            Permissions.Store.ManageOperations,
            Permissions.CashRegisterUsers.MobileManage,
        };

        public ReactCashRegisterUserController(
            ICashRegisterUserReactService service,
            IAuthorizationService authorizationService
        )
        {
            _service = service;
            _authorizationService = authorizationService;
        }

        private async Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissions)
        {
            foreach (var permission in permissions)
            {
                if ((await _authorizationService.AuthorizeAsync(User, null, permission)).Succeeded)
                {
                    return true;
                }
            }

            return false;
        }

        [HttpPost("grid")]
        public async Task<IActionResult> Grid([FromBody] GridRequestDto request)
        {
            if (!await HasAnyPermissionAsync(ReadPermissions))
                return Forbid();
            var result = await _service.GetGridDataAsync(request);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = new { Items = result.Items, Total = result.Total },
                        message = result.Message,
                    }
                );
            return Ok(
                new
                {
                    success = false,
                    data = new
                    {
                        Items = result.Items ?? new List<CashRegisterUserListDto>(),
                        Total = result.Total,
                    },
                    message = result.Message,
                }
            );
        }

        [HttpGet("scope")]
        public async Task<IActionResult> GetScope()
        {
            if (!await HasAnyPermissionAsync(ReadPermissions))
                return Forbid();
            var result = await _service.GetScopeAsync();
            if (result.Success && result.Data != null)
            {
                // 关键逻辑：移动端按这里的实时判定显示按钮，客户端登录时缓存的权限可能已过期。
                result.Data.CanManage = await HasAnyPermissionAsync(new[] { Permissions.CashRegisterUsers.MobileManage });
                result.Data.CanPrint = await HasAnyPermissionAsync(new[] { Permissions.CashRegisterUsers.MobilePrint });
                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpGet("user-options")]
        public async Task<IActionResult> GetUserOptions()
        {
            if (!await HasAnyPermissionAsync(ManagePermissions))
                return Forbid();
            var result = await _service.GetUserOptionsAsync();
            if (result.Success)
                return Ok(new { success = true, data = result.Data, message = result.Message });
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpGet("{hGuid}")]
        public async Task<IActionResult> GetByHGuid(string hGuid)
        {
            if (!await HasAnyPermissionAsync(ReadPermissions))
                return Forbid();
            var result = await _service.GetByHGuidAsync(hGuid);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return NotFound(new { success = false, message = result.Message });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateCashRegisterUserDto dto)
        {
            if (!await HasAnyPermissionAsync(ManagePermissions))
                return Forbid();
            var user = User.Identity?.Name ?? "system";
            var result = await _service.CreateAsync(dto, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPut("{hGuid}")]
        public async Task<IActionResult> Update(
            string hGuid,
            [FromBody] UpdateCashRegisterUserDto dto
        )
        {
            if (!await HasAnyPermissionAsync(ManagePermissions))
                return Forbid();
            var user = User.Identity?.Name ?? "system";
            var result = await _service.UpdateAsync(hGuid, dto, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("{hGuid}/print-confirmation")]
        [Authorize(Policy = Permissions.CashRegisterUsers.MobilePrint)]
        public async Task<IActionResult> ConfirmPrint(
            string hGuid,
            [FromBody] ConfirmCashRegisterUserPrintDto dto
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.ConfirmPrintAsync(hGuid, dto, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpDelete("{hGuid}")]
        [Authorize(Policy = Permissions.Store.ManageOperations)]
        public async Task<IActionResult> Delete(string hGuid)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.DeleteAsync(hGuid, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("batch-delete")]
        [Authorize(Policy = Permissions.Store.ManageOperations)]
        public async Task<IActionResult> BatchDelete([FromBody] List<string> hGuids)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchDeleteAsync(hGuids, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }
    }
}
