using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/EmployeeProfiles")]
    [Route("api/employee-profiles")]
    [Authorize]
    public class EmployeeProfilesController : ControllerBase
    {
        private readonly IEmployeeProfileService _service;
        private readonly ILogger<EmployeeProfilesController> _logger;

        public EmployeeProfilesController(
            IEmployeeProfileService service,
            ILogger<EmployeeProfilesController> logger
        )
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("admin")]
        [Authorize(Roles = "Admin,管理员")]
        [Authorize(Policy = Permissions.EmployeeProfiles.View)]
        public async Task<IActionResult> GetAdminList([FromQuery] EmployeeProfileQueryDto query)
        {
            try
            {
                return Ok(await _service.GetAdminListAsync(query));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取员工个人信息列表失败");
                return StatusCode(
                    500,
                    ApiResponse<PagedResult<EmployeeProfileListItemDto>>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpGet("admin/{userGuid}")]
        [Authorize(Roles = "Admin,管理员")]
        [Authorize(Policy = Permissions.EmployeeProfiles.View)]
        public async Task<IActionResult> GetAdminDetail(string userGuid)
        {
            try
            {
                return Ok(await _service.GetAdminDetailAsync(userGuid));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取员工个人信息详情失败，UserGUID: {UserGUID}", userGuid);
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPut("admin/{userGuid}")]
        [Authorize(Roles = "Admin,管理员")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> UpsertAdmin(string userGuid, [FromBody] EmployeeProfileUpsertDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<EmployeeProfileDetailDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                return Ok(await _service.UpsertAdminAsync(userGuid, dto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存员工个人信息失败，UserGUID: {UserGUID}", userGuid);
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetSelf()
        {
            try
            {
                return Ok(await _service.GetSelfAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前员工个人信息失败");
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPut("me")]
        public async Task<IActionResult> UpsertSelf([FromBody] EmployeeProfileUpsertDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<EmployeeProfileDetailDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                return Ok(await _service.UpsertSelfAsync(dto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存当前员工个人信息失败");
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }
    }
}
