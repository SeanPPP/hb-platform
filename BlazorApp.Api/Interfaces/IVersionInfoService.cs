using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Service.Models.HBPOSM_POSM;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 版本管理服务接口
    /// </summary>
    public interface IVersionInfoService
    {
        /// <summary>
        /// 获取版本列表（分页）
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>分页的版本数据</returns>
        Task<ApiResponse<PagedResult<VersionInfoDto>>> GetVersionsAsync(VersionInfoQueryDto query);

        /// <summary>
        /// 根据版本号获取版本详情
        /// </summary>
        /// <param name="version">版本号</param>
        /// <returns>版本详情</returns>
        Task<ApiResponse<VersionInfoDto>> GetVersionByAsync(string version);

        /// <summary>
        /// 创建版本
        /// </summary>
        /// <param name="dto">创建版本的数据传输对象</param>
        /// <param name="createdBy">创建者</param>
        /// <returns>创建的版本信息</returns>
        Task<ApiResponse<VersionInfoDto>> CreateVersionAsync(CreateVersionInfoDto dto, string createdBy);

        /// <summary>
        /// 更新版本
        /// </summary>
        /// <param name="version">版本号</param>
        /// <param name="dto">更新版本的数据传输对象</param>
        /// <param name="modifiedBy">修改者</param>
        /// <returns>更新后的版本信息</returns>
        Task<ApiResponse<VersionInfoDto>> UpdateVersionAsync(string version, UpdateVersionInfoDto dto, string modifiedBy);

        /// <summary>
        /// 删除版本
        /// </summary>
        /// <param name="version">版本号</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> DeleteVersionAsync(string version);
    }
}
