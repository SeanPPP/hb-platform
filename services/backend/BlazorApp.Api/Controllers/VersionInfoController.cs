using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 软件版本管理控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VersionInfoController : ControllerBase
    {
        private readonly IVersionInfoService _versionInfoService;
        private readonly ILogger<VersionInfoController> _logger;

        public VersionInfoController(
            IVersionInfoService versionInfoService,
            ILogger<VersionInfoController> logger
        )
        {
            _versionInfoService = versionInfoService;
            _logger = logger;
        }

        /// <summary>
        /// 获取版本列表
        /// </summary>
        /// <param name="query">查询参数（分页、搜索条件等）</param>
        /// <returns>分页的版本数据</returns>
        [HttpGet]
        public async Task<IActionResult> GetVersions([FromQuery] VersionInfoQueryDto query)
        {
            try
            {
                var result = await _versionInfoService.GetVersionsAsync(query);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取版本列表失败");
                return StatusCode(
                    500,
                    ApiResponse<PagedResult<VersionInfoDto>>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        /// <summary>
        /// 根据版本号获取版本详情
        /// </summary>
        /// <param name="version">版本号</param>
        /// <returns>版本详情</returns>
        [HttpGet("{version}")]
        public async Task<IActionResult> GetVersion(string version)
        {
            try
            {
                var result = await _versionInfoService.GetVersionByAsync(version);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取版本详情失败，Version: {Version}", version);
                return StatusCode(
                    500,
                    ApiResponse<VersionInfoDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 创建新版本
        /// </summary>
        /// <param name="dto">创建版本的数据传输对象</param>
        /// <returns>创建结果</returns>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateVersion([FromBody] CreateVersionInfoDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<VersionInfoDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                var currentUser = User.Identity?.Name ?? "System";
                var result = await _versionInfoService.CreateVersionAsync(dto, currentUser);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建版本失败");
                return StatusCode(
                    500,
                    ApiResponse<VersionInfoDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 更新版本
        /// </summary>
        /// <param name="version">版本号</param>
        /// <param name="dto">更新版本的数据传输对象</param>
        /// <returns>更新结果</returns>
        [HttpPut("{version}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateVersion(
            string version,
            [FromBody] UpdateVersionInfoDto dto
        )
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<VersionInfoDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                var currentUser = User.Identity?.Name ?? "System";
                var result = await _versionInfoService.UpdateVersionAsync(
                    version,
                    dto,
                    currentUser
                );
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新版本失败，Version: {Version}", version);
                return StatusCode(
                    500,
                    ApiResponse<VersionInfoDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }

        /// <summary>
        /// 删除版本
        /// </summary>
        /// <param name="version">版本号</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{version}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteVersion(string version)
        {
            try
            {
                var result = await _versionInfoService.DeleteVersionAsync(version);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除版本失败，Version: {Version}", version);
                return StatusCode(
                    500,
                    ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR")
                );
            }
        }
    }
}
