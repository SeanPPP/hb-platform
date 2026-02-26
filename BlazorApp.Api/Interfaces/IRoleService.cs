using BlazorApp.Shared.Models;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 角色管理服务接口
    /// 🔐 提供角色数据的CRUD操作和权限管理功能
    /// </summary>
    public interface IRoleService
    {
        /// <summary>
        /// 获取角色列表（分页）
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>分页的角色数据</returns>
        Task<ApiResponse<PagedResult<RoleDto>>> GetRolesAsync(RoleQueryDto query);

        /// <summary>
        /// 获取角色列表（高性能版本）
        /// ⚡ 优化查询性能，解决N+1问题，使用单一复合查询
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>分页的角色数据</returns>
        Task<ApiResponse<PagedResult<RoleDto>>> GetRolesOptimizedAsync(RoleQueryDto query);

        /// <summary>
        /// 角色服务性能测试方法 - 比较不同查询方式的性能
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>性能测试结果</returns>
        Task<ApiResponse<object>> PerformanceTestAsync(RoleQueryDto query);

        /// <summary>
        /// 获取所有活跃角色（不分页）
        /// </summary>
        /// <returns>活跃角色列表</returns>
        Task<ApiResponse<List<RoleDto>>> GetActiveRolesAsync();

        /// <summary>
        /// 根据GUID获取角色详情
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <returns>角色详情</returns>
        Task<ApiResponse<RoleDetailDto>> GetRoleByGuidAsync(string roleGuid);

        /// <summary>
        /// 根据角色名获取角色
        /// </summary>
        /// <param name="roleName">角色名</param>
        /// <returns>角色信息</returns>
        Task<ApiResponse<RoleDto>> GetRoleByNameAsync(string roleName);

        /// <summary>
        /// 创建新角色
        /// </summary>
        /// <param name="dto">创建角色DTO</param>
        /// <returns>创建的角色信息</returns>
        Task<ApiResponse<RoleDto>> CreateRoleAsync(CreateRoleDto dto);

        /// <summary>
        /// 根据GUID更新角色
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <param name="dto">更新角色DTO</param>
        /// <returns>更新的角色信息</returns>
        Task<ApiResponse<RoleDto>> UpdateRoleByGuidAsync(string roleGuid, UpdateRoleDto dto);

        /// <summary>
        /// 根据GUID删除角色
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> DeleteRoleByGuidAsync(string roleGuid);

        /// <summary>
        /// 根据GUID更新角色状态
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <param name="isActive">是否激活</param>
        /// <returns>更新结果</returns>
        Task<ApiResponse<bool>> UpdateRoleStatusByGuidAsync(string roleGuid, bool isActive);

        /// <summary>
        /// 获取角色的用户列表
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <param name="query">查询参数</param>
        /// <returns>用户列表</returns>
        Task<ApiResponse<PagedResult<RoleUserDto>>> GetRoleUsersAsync(string roleGuid, RoleQueryDto query);

        /// <summary>
        /// 为角色添加用户
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <param name="userGuids">用户GUID列表</param>
        /// <returns>添加结果</returns>
        Task<ApiResponse<bool>> AddUsersToRoleAsync(string roleGuid, List<string> userGuids);

        /// <summary>
        /// 从角色移除用户
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <param name="userGuid">用户GUID</param>
        /// <returns>移除结果</returns>
        Task<ApiResponse<bool>> RemoveUserFromRoleAsync(string roleGuid, string userGuid);

        /// <summary>
        /// 批量管理角色
        /// </summary>
        /// <param name="dto">批量操作DTO</param>
        /// <returns>操作结果</returns>
        Task<ApiResponse<bool>> BatchManageRolesAsync(BatchRoleOperationDto dto);

        /// <summary>
        /// 获取角色统计信息
        /// </summary>
        /// <returns>统计信息</returns>
        Task<ApiResponse<RoleStatisticsDto>> GetRoleStatisticsAsync();

        /// <summary>
        /// 检查角色名是否可用
        /// </summary>
        /// <param name="roleName">角色名</param>
        /// <param name="excludeRoleGuid">排除的角色GUID（更新时使用）</param>
        /// <returns>是否可用</returns>
        Task<ApiResponse<bool>> IsRoleNameAvailableAsync(string roleName, string? excludeRoleGuid = null);

        /// <summary>
        /// 获取所有权限列表
        /// </summary>
        /// <returns>权限列表</returns>
        Task<ApiResponse<List<PermissionCategoryDto>>> GetPermissionsAsync();

        /// <summary>
        /// 获取角色的权限列表
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <returns>权限列表</returns>
        Task<ApiResponse<List<string>>> GetRolePermissionsAsync(string roleGuid);

        /// <summary>
        /// 为角色分配权限
        /// </summary>
        /// <param name="roleGuid">角色GUID</param>
        /// <param name="dto">权限分配DTO</param>
        /// <returns>分配结果</returns>
        Task<ApiResponse<bool>> AssignPermissionsToRoleAsync(string roleGuid, RolePermissionAssignmentDto dto);

        /// <summary>
        /// 检查用户是否有指定角色
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="roleName">角色名</param>
        /// <returns>是否有角色</returns>
        Task<ApiResponse<bool>> UserHasRoleAsync(string userGuid, string roleName);

        /// <summary>
        /// 检查用户是否有指定权限
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="permission">权限名</param>
        /// <returns>是否有权限</returns>
        Task<ApiResponse<bool>> UserHasPermissionAsync(string userGuid, string permission);

        /// <summary>
        /// 复制角色
        /// </summary>
        /// <param name="sourceRoleGuid">源角色GUID</param>
        /// <param name="newRoleName">新角色名</param>
        /// <param name="newDescription">新角色描述</param>
        /// <returns>新角色</returns>
        Task<ApiResponse<RoleDto>> DuplicateRoleAsync(string sourceRoleGuid, string newRoleName, string? newDescription = null);

        /// <summary>
        /// 获取拥有指定权限的角色列表
        /// </summary>
        /// <param name="permissionCode">权限代码</param>
        /// <returns>角色列表</returns>
        Task<ApiResponse<List<RoleDto>>> GetPermissionRolesAsync(string permissionCode);

        /// <summary>
        /// 为权限分配角色
        /// </summary>
        /// <param name="permissionCode">权限代码</param>
        /// <param name="roleGuids">角色GUID列表</param>
        /// <returns>操作结果</returns>
        Task<ApiResponse<bool>> AssignRolesToPermissionAsync(string permissionCode, List<string> roleGuids);

        /// <summary>
        /// 获取所有权限（扁平列表，用于管理表格）
        /// </summary>
        /// <returns>权限列表</returns>
        Task<ApiResponse<List<SysPermission>>> GetSysPermissionsAsync();

        /// <summary>
        /// 创建新权限
        /// </summary>
        /// <param name="dto">权限信息</param>
        /// <returns>创建的权限对象列表</returns>
        Task<ApiResponse<List<SysPermission>>> CreatePermissionAsync(CreateSysPermissionDto dto);
    }
}