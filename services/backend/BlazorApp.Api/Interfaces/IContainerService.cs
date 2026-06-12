using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 货柜服务接口
    /// </summary>
    public interface IContainerService
    {
        /// <summary>
        /// 获取货柜列表
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>货柜列表响应</returns>
        Task<ContainerListResponse> GetContainersAsync(ContainerQueryRequest request);

        /// <summary>
        /// 获取货柜详情
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <returns>货柜详情</returns>
        Task<ContainerMainDto?> GetContainerDetailAsync(string containerGuid);

        /// <summary>
        /// 获取货柜商品列表
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <returns>商品列表</returns>
        Task<List<ContainerDetailDto>> GetContainerProductsAsync(string containerGuid);

        /// <summary>
        /// 获取符合条件的所有货柜商品明细列表
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>商品明细列表</returns>
        Task<List<ContainerDetailDto>> GetFilteredContainerProductsAsync(ContainerQueryRequest request);

        /// <summary>
        /// 获取日期过滤选项
        /// </summary>
        /// <returns>日期选项列表</returns>
        Task<List<DateFilterOption>> GetDateFilterOptionsAsync();

        /// <summary>
        /// 更新货柜信息
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <param name="dto">更新DTO</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateContainerAsync(string containerGuid, UpdateContainerDto dto);

        /// <summary>
        /// 批量更新货柜明细
        /// </summary>
        /// <param name="updates">明细更新列表</param>
        /// <returns>成功更新的行数</returns>
        Task<int> BatchUpdateDetailsAsync(List<UpdateContainerDetailDto> updates);

        /// <summary>
        /// 创建新货柜
        /// </summary>
        /// <param name="dto">创建货柜DTO</param>
        /// <returns>创建的货柜GUID</returns>
        Task<string> CreateContainerAsync(CreateContainerDto dto);
    }

    /// <summary>
    /// 日期过滤选项
    /// </summary>
    public class DateFilterOption
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string DateType { get; set; } = "预计到岸日期";
    }
}