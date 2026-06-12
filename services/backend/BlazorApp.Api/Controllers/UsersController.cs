using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 用户管理控制器
    /// 🧑‍💼 提供用户数据的CRUD操作和权限管理功能
    /// 🔐 包含完整的授权控制，确保只有有权限的用户才能访问相应功能
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // 🔐 启用全局授权，所有端点都需要认证
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IRoleService _roleService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            IUserService userService,
            IRoleService roleService,
            ILogger<UsersController> logger
        )
        {
            _userService = userService;
            _roleService = roleService;
            _logger = logger;
        }

        /// <summary>
        /// 获取用户列表
        /// 📋 支持分页、搜索、筛选的用户数据查询
        /// </summary>
        /// <param name="query">查询参数（分页、搜索条件等）</param>
        /// <returns>分页的用户数据</returns>
        [HttpGet]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetUsers([FromQuery] UserQueryDto query)
        {
            try
            {
                var result = await _userService.GetUsersAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户列表失败");
                return StatusCode(
                    500,
                    ApiResponse<PagedResult<UserDto>>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 获取用户列表（高性能版本）
        /// ⚡ 优化查询性能，解决N+1问题，使用单一复合查询
        /// 📋 支持分页、搜索、筛选的用户数据查询
        /// </summary>
        /// <param name="query">查询参数（分页、搜索条件等）</param>
        /// <returns>分页的用户数据</returns>
        [HttpGet("optimized")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetUsersOptimized([FromQuery] UserQueryDto query)
        {
            try
            {
                var result = await _userService.GetUsersOptimizedAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户列表失败");
                return StatusCode(
                    500,
                    ApiResponse<PagedResult<UserDto>>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 性能测试端点 - 比较原始查询与优化查询的性能差异
        /// 🧪 用于评估查询优化效果
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>性能测试报告</returns>
        [HttpGet("performance-test")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> PerformanceTest([FromQuery] UserQueryDto query)
        {
            try
            {
                var result = await _userService.PerformanceTestAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "性能测试失败");
                return StatusCode(
                    500,
                    ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据GUID获取用户详情
        /// </summary>
        [HttpGet("guid/{guid}")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetUserByGuid(string guid)
        {
            try
            {
                var result = await _userService.GetUserByGuidAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户详情失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<UserDetailDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据用户名获取用户
        /// </summary>
        [HttpGet("username/{username}")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetUserByUsername(string username)
        {
            try
            {
                var result = await _userService.GetUserByUsernameAsync(username);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据用户名获取用户失败，Username: {Username}", username);
                return StatusCode(
                    500,
                    ApiResponse<UserDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据邮箱获取用户
        /// </summary>
        [HttpGet("email/{email}")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetUserByEmail(string email)
        {
            try
            {
                var result = await _userService.GetUserByEmailAsync(email);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据邮箱获取用户失败，Email: {Email}", email);
                return StatusCode(
                    500,
                    ApiResponse<UserDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 创建新用户
        /// ➕ 只有Admin角色才能创建用户，确保数据安全
        /// </summary>
        /// <param name="dto">创建用户的数据传输对象</param>
        /// <returns>创建结果</returns>
        [HttpPost]
        [Authorize(Policy = Permissions.Users.Create)]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<UserDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                var result = await _userService.CreateUserAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建用户失败");
                return StatusCode(
                    500,
                    ApiResponse<UserDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 批量创建用户（分店代码、用户名、密码；分配默认角色与对应分店）
        /// </summary>
        [HttpPost("batch-create")]
        [Authorize(Policy = Permissions.Users.Create)]
        public async Task<IActionResult> BatchCreateUsers([FromBody] BatchCreateUserRequestDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<BatchCreateUserResultDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                var result = await _userService.BatchCreateUsersAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建用户失败");
                return StatusCode(
                    500,
                    ApiResponse<BatchCreateUserResultDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 根据GUID更新用户
        /// </summary>
        [HttpPut("guid/{guid}")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> UpdateUserByGuid(string guid, [FromBody] UpdateUserDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<UserDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                var result = await _userService.UpdateUserByGuidAsync(guid, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<UserDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据GUID删除用户
        /// </summary>
        [HttpDelete("guid/{guid}")]
        [Authorize(Policy = Permissions.Users.Delete)]
        public async Task<IActionResult> DeleteUserByGuid(string guid)
        {
            try
            {
                var result = await _userService.DeleteUserByGuidAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除用户失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据GUID更新用户状态
        /// </summary>
        [HttpPut("guid/{guid}/status")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> UpdateUserStatusByGuid(
            string guid,
            [FromBody] UpdateUserStatusDto dto
        )
        {
            try
            {
                var result = await _userService.UpdateUserStatusByGuidAsync(guid, dto.IsActive);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户状态失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 更新用户密码
        /// </summary>
        [HttpPut("guid/{guid}/password")]
        [Authorize(Policy = Permissions.Users.ResetPassword)]
        public async Task<IActionResult> UpdateUserPassword(
            string guid,
            [FromBody] UpdateUserPasswordDto dto
        )
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<bool>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState)
                    );
                }

                var result = await _userService.UpdateUserPasswordByGuidAsync(guid, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户密码失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 为用户分配角色
        /// </summary>
        [HttpPost("guid/{guid}/roles")]
        [Authorize(Policy = Permissions.Users.ManageRoles)]
        public async Task<IActionResult> AssignRolesToUser(
            string guid,
            [FromBody] UserRoleAssignmentDto dto
        )
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<bool>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState)
                    );
                }

                var result = await _userService.AssignRolesToUserAsync(guid, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为用户分配角色失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取用户权限状态
        /// </summary>
        [HttpGet("guid/{guid}/permissions/state")]
        [Authorize(Policy = Permissions.Users.ManageRoles)]
        public async Task<IActionResult> GetUserPermissionState(string guid)
        {
            try
            {
                var result = await _roleService.GetUserPermissionStateAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户权限状态失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<UserPermissionStateDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 为用户分配直接权限
        /// </summary>
        [HttpPost("guid/{guid}/permissions")]
        [Authorize(Policy = Permissions.Users.ManageRoles)]
        public async Task<IActionResult> AssignPermissionsToUser(
            string guid,
            [FromBody] UserPermissionAssignmentDto dto
        )
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<bool>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState)
                    );
                }

                var result = await _roleService.AssignPermissionsToUserAsync(guid, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为用户分配直接权限失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 为用户分配分店
        /// </summary>
        [HttpPost("guid/{guid}/stores")]
        [Authorize(Policy = Permissions.Users.ManageStores)]
        public async Task<IActionResult> AssignStoresToUser(
            string guid,
            [FromBody] List<UserStoreAssignmentDto> storeAssignments
        )
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<bool>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState)
                    );
                }

                var result = await _userService.AssignStoresToUserAsync(guid, storeAssignments);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为用户分配分店失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取用户的角色列表
        /// </summary>
        [HttpGet("guid/{guid}/roles")]
        [Authorize(Policy = Permissions.Users.ManageRoles)]
        public async Task<IActionResult> GetUserRoles(string guid)
        {
            try
            {
                var result = await _userService.GetUserRolesAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户角色失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<List<RoleDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取用户的分店列表
        /// 用户可查看自己的分店，管理员可查看所有用户的分店
        /// </summary>
        [HttpGet("guid/{guid}/stores")]
        public async Task<IActionResult> GetUserStores(string guid, [FromServices] ICurrentUserService currentUser)
        {
            var currentUserGuid = currentUser.GetCurrentUserGuid();
            var isSelf = string.Equals(currentUserGuid, guid, StringComparison.OrdinalIgnoreCase);
            var canViewOtherUserStores =
                CurrentUserManageableStoreScopeService.HasAnyRole(
                    User,
                    CurrentUserManageableStoreScopeService.AdminRoleAliases
                )
                || CurrentUserManageableStoreScopeService.HasAnyRole(
                    User,
                    CurrentUserManageableStoreScopeService.WarehouseManagerRoleAliases
                );

            if (!isSelf && !canViewOtherUserStores)
            {
                return Forbid();
            }

            try
            {
                var result = await _userService.GetUserStoresAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户分店失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<List<UserStoreDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 从用户移除角色
        /// </summary>
        [HttpDelete("guid/{guid}/roles/{roleGuid}")]
        [Authorize(Policy = Permissions.Users.ManageRoles)]
        public async Task<IActionResult> RemoveRoleFromUser(string guid, string roleGuid)
        {
            try
            {
                var result = await _userService.RemoveRoleFromUserAsync(guid, roleGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "从用户移除角色失败，UserGUID: {UserGUID}, RoleGUID: {RoleGUID}",
                    guid,
                    roleGuid
                );
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 从用户移除分店
        /// </summary>
        [HttpDelete("guid/{guid}/stores/{storeGuid}")]
        [Authorize(Policy = Permissions.Users.ManageStores)]
        public async Task<IActionResult> RemoveStoreFromUser(string guid, string storeGuid)
        {
            try
            {
                var result = await _userService.RemoveStoreFromUserAsync(guid, storeGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "从用户移除分店失败，UserGUID: {UserGUID}, StoreGUID: {StoreGUID}",
                    guid,
                    storeGuid
                );
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 批量管理用户
        /// </summary>
        [HttpPost("batch")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> BatchManageUsers([FromBody] BatchUserOperationDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<bool>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState)
                    );
                }

                var result = await _userService.BatchManageUsersAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量管理用户失败");
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 导入用户
        /// </summary>
        [HttpPost("import")]
        [Authorize(Policy = Permissions.Users.Create)]
        public async Task<IActionResult> ImportUsers([FromBody] List<ImportUserDto> users)
        {
            try
            {
                var result = await _userService.ImportUsersAsync(users);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入用户失败");
                return StatusCode(
                    500,
                    ApiResponse<ImportUserResultDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 导出用户
        /// </summary>
        [HttpGet("export")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> ExportUsers([FromQuery] UserQueryDto query)
        {
            try
            {
                var result = await _userService.ExportUsersAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出用户失败");
                return StatusCode(
                    500,
                    ApiResponse<List<UserDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取用户统计信息
        /// </summary>
        [HttpGet("statistics")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> GetUserStatistics()
        {
            try
            {
                var result = await _userService.GetUserStatisticsAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户统计失败");
                return StatusCode(
                    500,
                    ApiResponse<UserStatisticsDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 检查用户名是否可用
        /// </summary>
        [HttpGet("check-username/{username}")]
        // [Authorize(Roles = "Admin")] // 暂时注释掉授权
        public async Task<IActionResult> CheckUsernameAvailability(
            string username,
            [FromQuery] string? excludeUserGuid = null
        )
        {
            try
            {
                var result = await _userService.IsUsernameAvailableAsync(username, excludeUserGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查用户名可用性失败，Username: {Username}", username);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 检查邮箱是否可用
        /// </summary>
        [HttpGet("check-email/{email}")]
        // [Authorize(Roles = "Admin")] // 暂时注释掉授权
        public async Task<IActionResult> CheckEmailAvailability(
            string email,
            [FromQuery] string? excludeUserGuid = null
        )
        {
            try
            {
                var result = await _userService.IsEmailAvailableAsync(email, excludeUserGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查邮箱可用性失败，Email: {Email}", email);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 重置用户密码
        /// </summary>
        [HttpPost("guid/{guid}/reset-password")]
        [Authorize(Policy = Permissions.Users.ResetPassword)]
        public async Task<IActionResult> ResetUserPassword(string guid)
        {
            try
            {
                var result = await _userService.ResetUserPasswordAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置用户密码失败，UserGUID: {UserGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<string>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 锁定/解锁用户
        /// </summary>
        [HttpPut("guid/{guid}/lock")]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> LockUser(string guid, [FromBody] bool isLocked)
        {
            try
            {
                var result = await _userService.LockUserAsync(guid, isLocked);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "锁定/解锁用户失败，UserGUID: {UserGUID}, IsLocked: {IsLocked}",
                    guid,
                    isLocked
                );
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }
    }
}
