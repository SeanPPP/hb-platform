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
    /// 分店管理控制器
    /// 🏢 提供分店数据的CRUD操作和同步功能
    /// 🔐 包含完整的授权控制，确保只有有权限的用户才能访问相应功能
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // 🔐 启用全局授权，所有端点都需要认证
    // 全局授权：控制器级别的[Authorize]会应用到所有Action方法
    // 如果启用，所有端点都需要认证，除非单独标记[AllowAnonymous]
    public class StoresController : ControllerBase
    {
        private readonly IStoreService _storeService;
        private readonly StoreSyncService _syncService;
        private readonly ILogger<StoresController> _logger;

        public StoresController(
            IStoreService storeService,
            StoreSyncService syncService,
            ILogger<StoresController> logger
        )
        {
            _storeService = storeService;
            _syncService = syncService;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有未删除的分店列表（按名称排序）
        /// </summary>
        /// <returns>分店列表</returns>
        [HttpGet("all-by-name")]
        public async Task<IActionResult> GetAllStoresByName()
        {
            try
            {
                var result = await _storeService.GetAllStoresByNameAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有分店列表失败");
                return StatusCode(
                    500,
                    ApiResponse<List<StoreDto>>.Error("获取所有分店列表失败", "INTERNAL_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取所有激活的分店列表（用于数据同步分店选择）
        /// 🏢 返回所有状态为激活的分店信息，主要用于数据同步时的分店选择
        /// </summary>
        /// <returns>激活的分店列表</returns>
        [HttpGet("active")]
        [Authorize(Policy = Permissions.Stores.View)]
        public async Task<IActionResult> GetActiveStores()
        {
            try
            {
                var result = await _storeService.GetActiveStoresAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取激活分店列表失败");
                return StatusCode(
                    500,
                    ApiResponse<List<StoreDto>>.Error("获取激活分店列表失败", "INTERNAL_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取分店列表
        /// 📋 支持分页、搜索、筛选的分店数据查询
        /// </summary>
        /// <param name="query">查询参数（分页、搜索条件等）</param>
        /// <returns>分页的分店数据</returns>
        [HttpGet]
        [Authorize(Policy = Permissions.Stores.View)]
        public async Task<IActionResult> GetStores([FromQuery] StoreQueryDto query)
        {
            try
            {
                var result = await _storeService.GetStoresAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分店列表失败");
                return StatusCode(
                    500,
                    ApiResponse<PagedResult<StoreDto>>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 根据GUID获取分店详情
        /// </summary>
        [HttpGet("guid/{guid}")]
        [Authorize(Policy = Permissions.Stores.View)]
        public async Task<IActionResult> GetStoreByGuid(string guid)
        {
            try
            {
                var result = await _storeService.GetStoreByGuidAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分店详情失败，GUID: {StoreGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<StoreDetailDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 创建新分店
        /// ➕ 只有Admin角色才能创建分店，确保数据安全
        /// </summary>
        /// <param name="dto">创建分店的数据传输对象</param>
        /// <returns>创建结果</returns>
        [HttpPost]
        [Authorize(Policy = Permissions.Stores.Create)]
        public async Task<IActionResult> CreateStore([FromBody] CreateStoreDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<StoreDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                var result = await _storeService.CreateStoreAsync(dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建分店失败");
                return StatusCode(
                    500,
                    ApiResponse<StoreDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据GUID更新分店
        /// </summary>
        [HttpPut("guid/{guid}")]
        [Authorize(Policy = Permissions.Stores.Edit)]
        public async Task<IActionResult> UpdateStoreByGuid(
            string guid,
            [FromBody] UpdateStoreDto dto
        )
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<StoreDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                var result = await _storeService.UpdateStoreByGuidAsync(guid, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店失败，GUID: {StoreGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<StoreDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据GUID删除分店
        /// </summary>
        [HttpDelete("guid/{guid}")]
        [Authorize(Policy = Permissions.Stores.Delete)]
        public async Task<IActionResult> DeleteStoreByGuid(string guid)
        {
            try
            {
                var result = await _storeService.DeleteStoreByGuidAsync(guid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除分店失败，GUID: {StoreGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 根据GUID更新分店状态
        /// </summary>
        [HttpPut("guid/{guid}/status")]
        [Authorize(Policy = Permissions.Stores.Edit)]
        public async Task<IActionResult> UpdateStoreStatusByGuid(
            string guid,
            [FromBody] UpdateStoreStatusDto dto
        )
        {
            try
            {
                var result = await _storeService.UpdateStoreStatusByGuidAsync(guid, dto.IsActive);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店状态失败，GUID: {StoreGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取分店用户列表
        /// </summary>
        [HttpGet("guid/{guid}/users")]
        [Authorize(Policy = Permissions.Stores.View)]
        public async Task<IActionResult> GetStoreUsers(string guid, [FromQuery] UserQueryDto query)
        {
            try
            {
                var result = await _storeService.GetStoreUsersAsync(guid, query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分店用户列表失败，StoreGUID: {StoreGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<PagedResult<StoreUserDto>>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 为分店添加用户
        /// </summary>
        [HttpPost("guid/{guid}/users")]
        [Authorize(Policy = Permissions.Stores.Edit)]
        public async Task<IActionResult> AddUserToStore(
            string guid,
            [FromBody] AddUserToStoreDto dto
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

                var result = await _storeService.AddUserToStoreAsync(guid, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为分店添加用户失败，StoreGUID: {StoreGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 从分店移除用户
        /// </summary>
        [HttpDelete("guid/{guid}/users/{userGuid}")]
        [Authorize(Policy = Permissions.Stores.Edit)]
        public async Task<IActionResult> RemoveUserFromStore(string guid, string userGuid)
        {
            try
            {
                var result = await _storeService.RemoveUserFromStoreAsync(guid, userGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "从分店移除用户失败，StoreGUID: {StoreGUID}, UserGUID: {UserGUID}",
                    guid,
                    userGuid
                );
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 设置用户是否可管理该分店
        /// </summary>
        [HttpPut("guid/{guid}/users/{userGuid}/primary")]
        [Authorize(Policy = Permissions.Stores.Edit)]
        public async Task<IActionResult> SetPrimaryUser(
            string guid,
            string userGuid,
            [FromBody] bool isPrimary
        )
        {
            try
            {
                var result = await _storeService.SetPrimaryUserAsync(guid, userGuid, isPrimary);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "设置分店管理关系失败，StoreGUID: {StoreGUID}, UserGUID: {UserGUID}",
                    guid,
                    userGuid
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
        [HttpPost("guid/{guid}/users/batch")]
        [Authorize(Policy = Permissions.Stores.Edit)]
        public async Task<IActionResult> BatchManageUsers(
            string guid,
            [FromBody] BatchUserOperationDto dto
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

                var result = await _storeService.BatchManageUsersAsync(guid, dto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量管理用户失败，StoreGUID: {StoreGUID}", guid);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 从HQ总部同步分店数据
        /// 🔄 从总部数据库获取最新分店信息并更新本地数据库
        /// 这是一个敏感操作，只有Admin角色才能执行
        /// </summary>
        /// <returns>同步结果</returns>
        [HttpPost("sync")]
        [Authorize(Policy = Permissions.Stores.Sync)]
        public async Task<IActionResult> SyncStoresFromHq()
        {
            try
            {
                _logger.LogInformation("开始从HQ数据库同步分店数据");

                var result = await _syncService.SyncStoresFromHqAsync();

                if (result.IsSuccess)
                {
                    _logger.LogInformation("分店数据同步成功: {Message}", result.Message);
                    return Ok(ApiResponse<SyncResult>.OK(result, result.Message));
                }
                else
                {
                    _logger.LogWarning("分店数据同步失败: {Message}", result.Message);
                    return BadRequest(
                        ApiResponse<SyncResult>.Error(result.Message, "SYNC_FAILED", result)
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步分店数据时发生异常");
                return StatusCode(
                    500,
                    ApiResponse<SyncResult>.Error("同步过程中发生内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 获取同步历史记录
        /// </summary>
        [HttpGet("sync/history")]
        [Authorize(Policy = Permissions.Stores.View)]
        public async Task<IActionResult> GetSyncHistory([FromQuery] int pageSize = 10)
        {
            try
            {
                var history = await _syncService.GetSyncHistoryAsync(pageSize);
                return Ok(ApiResponse<List<SyncHistory>>.OK(history));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取同步历史失败");
                return StatusCode(
                    500,
                    ApiResponse<List<SyncHistory>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }
    }
}
