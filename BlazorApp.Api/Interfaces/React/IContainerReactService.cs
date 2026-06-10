using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 专用货柜服务接口（与原有 IContainerService 解耦）
    /// 仅包含 React 控制器所需的方法
    /// </summary>
    public interface IContainerReactService
    {
        /// <summary>
        /// 获取货柜列表（React）
        /// </summary>
        Task<ContainerListResponse> GetContainersAsync(ContainerQueryRequest request);

        /// <summary>
        /// 获取货柜详情（React）
        /// </summary>
        Task<ContainerMainDto?> GetContainerDetailAsync(string containerGuid);

        /// <summary>
        /// 更新货柜信息（React）
        /// </summary>
        Task<bool> UpdateContainerAsync(string containerGuid, UpdateContainerDto dto);

        /// <summary>
        /// 获取货柜商品列表（React）
        /// </summary>
        Task<List<ContainerDetailDto>> GetContainerProductsAsync(string containerGuid);

        /// <summary>
        /// 按服务端筛选、排序和内部分页查询货柜商品明细（React）
        /// </summary>
        Task<ContainerDetailQueryResultDto> QueryContainerDetailsAsync(ContainerDetailQueryDto request);

        /// <summary>
        /// 获取符合条件的所有货柜商品明细列表（React）
        /// </summary>
        Task<List<ContainerDetailDto>> GetFilteredContainerProductsAsync(ContainerQueryRequest request);

        /// <summary>
        /// 获取日期过滤选项（React）
        /// </summary>
        Task<List<DateFilterOption>> GetDateFilterOptionsAsync();

        /// <summary>
        /// 批量更新货柜明细（React）
        /// </summary>
        /// <param name="updates">明细更新列表</param>
        /// <returns>成功更新的行数</returns>
        Task<int> BatchUpdateDetailsAsync(List<UpdateContainerDetailDto> updates);

        /// <summary>
        /// 按当前筛选范围批量调浮率并重算成本。
        /// </summary>
        Task<int> ApplyFloatRateByScopeAsync(string containerGuid, ContainerDetailApplyFloatRateRequestDto request);

        /// <summary>
        /// 按当前筛选范围批量改进口价/贴牌价。
        /// </summary>
        Task<int> ApplyPricesByScopeAsync(string containerGuid, ContainerDetailApplyPricesRequestDto request);

        /// <summary>
        /// 按当前筛选范围重算运输成本和进口价。
        /// </summary>
        Task<int> RecalculateCostsByScopeAsync(string containerGuid, ContainerDetailBatchScopeDto request);

        /// <summary>
        /// 创建新货柜（React）
        /// </summary>
        /// <param name="dto">创建货柜DTO</param>
        /// <returns>创建的货柜GUID</returns>
        Task<string> CreateContainerAsync(CreateContainerDto dto);

        /// <summary>
        /// 按 ProductCode 检查指定货柜的明细冲突（React）
        /// </summary>
        /// <param name="containerId">货柜编号或货柜编码（皆可）</param>
        /// <param name="productCodes">待检查的商品编码列表</param>
        Task<List<ContainerConflictItemDto>> CheckConflictsAsync(string containerId, List<string> productCodes);

        /// <summary>
        /// 批量分配商品到货柜（React）
        /// </summary>
        /// <param name="containerId">货柜编号或货柜编码（皆可）</param>
        /// <param name="items">分配的商品列表</param>
        /// <param name="resolution">冲突处理策略：override/increase</param>
        /// <param name="notes">备注</param>
        Task<AssignProductsResultDto> AssignProductsAsync(string containerId, List<AssignProductItemDto> items, string resolution, string? notes);

        /// <summary>
        /// 批量删除货柜明细（React）
        /// </summary>
        /// <param name="hguids">明细的 HGUID/DetailCode 列表</param>
        /// <returns>成功删除的行数</returns>
        Task<int> BatchDeleteDetailsAsync(List<string> hguids);

        Task<List<ComingSoonContainerDto>> GetComingSoonContainersAsync();

        Task<SyncResult> SyncContainersWithDetailsFromHqAsync(DateTime? startDate = null);

        Task<SyncResult> PushContainersToHbSalesAsync(List<string> containerGuids);
    }
}
