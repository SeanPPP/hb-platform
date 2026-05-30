using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-users")]
    [Authorize]
    public class ReactStoreUsersController : ControllerBase
    {
        private readonly IStoreUserReactService _service;

        public ReactStoreUsersController(IStoreUserReactService service)
        {
            _service = service;
        }

        [HttpPost("grid")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> Grid([FromBody] StoreUserGridRequestDto request)
        {
            var result = await _service.GetGridDataAsync(request);
            if (result.Success)
            {
                return Ok(
                    new
                    {
                        success = true,
                        data = new { items = result.Items, total = result.Total },
                        message = result.Message,
                    }
                );
            }

            return Ok(
                new
                {
                    success = false,
                    data = new { items = result.Items ?? new List<StoreUserListDto>(), total = result.Total },
                    message = result.Message,
                }
            );
        }

        [HttpGet("{userGuid}")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetByUserGuid(string userGuid, [FromQuery] string? storeCode)
        {
            var result = await _service.GetByUserGuidAsync(userGuid, storeCode);
            return Ok(result);
        }

        [HttpGet("{userGuid}/profile")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetProfile(string userGuid, [FromQuery] string? storeCode)
        {
            var result = await _service.GetByUserGuidAsync(userGuid, storeCode);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Policy = Permissions.Users.Create)]
        public async Task<IActionResult> Create([FromBody] CreateStoreUserDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(
                    ApiResponse<StoreUserDetailDto>.Error(
                        "请求参数验证失败",
                        "VALIDATION_ERROR",
                        ModelState
                    )
                );
            }

            var result = await _service.CreateAsync(dto, User.Identity?.Name ?? "system");
            return Ok(result);
        }

        [HttpPut("{userGuid}")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> Update(string userGuid, [FromBody] UpdateStoreUserDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(
                    ApiResponse<StoreUserDetailDto>.Error(
                        "请求参数验证失败",
                        "VALIDATION_ERROR",
                        ModelState
                    )
                );
            }

            var result = await _service.UpdateAsync(userGuid, dto, User.Identity?.Name ?? "system");
            return Ok(result);
        }

        [HttpPut("{userGuid}/status")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> UpdateStatus(
            string userGuid,
            [FromBody] UpdateStoreUserStatusDto dto
        )
        {
            var result = await _service.UpdateStatusAsync(
                userGuid,
                dto,
                User.Identity?.Name ?? "system"
            );
            return Ok(result);
        }

        [HttpPut("{userGuid}/password")]
        [Authorize(Policy = Permissions.Users.ResetPassword)]
        public async Task<IActionResult> UpdatePassword(
            string userGuid,
            [FromBody] UpdateStoreUserPasswordDto dto
        )
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(
                    ApiResponse<bool>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState)
                );
            }

            var result = await _service.UpdatePasswordAsync(
                userGuid,
                dto,
                User.Identity?.Name ?? "system"
            );
            return Ok(result);
        }
    }
}
