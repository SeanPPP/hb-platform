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

        public ReactCashRegisterUserController(ICashRegisterUserReactService service)
        {
            _service = service;
        }

        [HttpPost("grid")]
        [Authorize(Policy = Permissions.Store.ManageOperations)]
        public async Task<IActionResult> Grid([FromBody] GridRequestDto request)
        {
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

        [HttpGet("{hGuid}")]
        [Authorize(Policy = Permissions.Store.ManageOperations)]
        public async Task<IActionResult> GetByHGuid(string hGuid)
        {
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
        [Authorize(Policy = Permissions.Store.ManageOperations)]
        public async Task<IActionResult> Create([FromBody] CreateCashRegisterUserDto dto)
        {
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
        [Authorize(Policy = Permissions.Store.ManageOperations)]
        public async Task<IActionResult> Update(
            string hGuid,
            [FromBody] UpdateCashRegisterUserDto dto
        )
        {
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
