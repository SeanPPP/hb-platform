using BlazorApp.Shared.Models;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 分店服务接口
    /// </summary>
    public interface IStoreService
    {
        /// <summary>
        /// 获取分店列表
        /// </summary>
        Task<ApiResponse<PagedResult<StoreDto>>> GetStoresAsync(StoreQueryDto query);

        /// <summary>
        /// 获取所有未删除的分店列表（按名称排序）
        /// </summary>
        Task<ApiResponse<List<StoreDto>>> GetAllStoresByNameAsync();

        /// <summary>
        /// 获取所有激活的分店列表（用于数据同步分店选择）
        /// </summary>
        Task<ApiResponse<List<StoreDto>>> GetActiveStoresAsync();

        /// <summary>
        /// 根据GUID获取分店详情
        /// </summary>
        Task<ApiResponse<StoreDetailDto>> GetStoreByGuidAsync(string guid);

        /// <summary>
        /// 根据分店代码获取分店信息
        /// </summary>
        Task<ApiResponse<StoreDto>> GetStoreByCodeAsync(string storeCode);

        /// <summary>
        /// 获取下一个建议分店编码
        /// </summary>
        Task<ApiResponse<string>> GetNextStoreCodeAsync();

        /// <summary>
        /// 创建分店
        /// </summary>
        Task<ApiResponse<StoreDto>> CreateStoreAsync(CreateStoreDto dto);

        /// <summary>
        /// 根据GUID更新分店
        /// </summary>
        Task<ApiResponse<StoreDto>> UpdateStoreByGuidAsync(string guid, UpdateStoreDto dto);

        /// <summary>
        /// 根据GUID删除分店
        /// </summary>
        Task<ApiResponse<bool>> DeleteStoreByGuidAsync(string guid);

        /// <summary>
        /// 根据GUID更新分店状态
        /// </summary>
        Task<ApiResponse<bool>> UpdateStoreStatusByGuidAsync(string guid, bool isActive);

        /// <summary>
        /// 将分店信息同步到HQ分店表
        /// </summary>
        Task<ApiResponse<bool>> SyncStoreToHqAsync(string guid);

        /// <summary>
        /// 获取分店用户列表
        /// </summary>
        Task<ApiResponse<PagedResult<StoreUserDto>>> GetStoreUsersAsync(string storeGuid, UserQueryDto query);

        /// <summary>
        /// 为分店添加用户
        /// </summary>
        Task<ApiResponse<bool>> AddUserToStoreAsync(string storeGuid, AddUserToStoreDto dto);

        /// <summary>
        /// 从分店移除用户
        /// </summary>
        Task<ApiResponse<bool>> RemoveUserFromStoreAsync(string storeGuid, string userGuid);

        /// <summary>
        /// 设置用户是否可管理该分店
        /// </summary>
        Task<ApiResponse<bool>> SetPrimaryUserAsync(string storeGuid, string userGuid, bool isPrimary);

        /// <summary>
        /// 批量管理用户
        /// </summary>
        Task<ApiResponse<bool>> BatchManageUsersAsync(string storeGuid, BatchUserOperationDto dto);
    }
}
