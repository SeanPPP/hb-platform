using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 义乌货柜服务接口 - 基于新的Container模型
    /// </summary>
    public interface IYiwuContainerService
    {
        #region 货柜主表操作

        /// <summary>
        /// 获取货柜列表
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>货柜列表响应</returns>
        Task<YiwuContainerListResponse> GetContainersAsync(YiwuContainerQueryRequest request);

        /// <summary>
        /// 获取货柜详情
        /// </summary>
        /// <param name="containerCode">货柜编码</param>
        /// <returns>货柜详情</returns>
        Task<YiwuContainerDto?> GetContainerAsync(string containerCode);

        /// <summary>
        /// 创建货柜
        /// </summary>
        /// <param name="containerDto">货柜DTO</param>
        /// <returns>创建结果</returns>
        Task<string> CreateContainerAsync(YiwuContainerDto containerDto);

        /// <summary>
        /// 更新货柜
        /// </summary>
        /// <param name="containerDto">货柜DTO</param>
        /// <returns>更新结果</returns>
        Task<bool> UpdateContainerAsync(YiwuContainerDto containerDto);

        /// <summary>
        /// 删除货柜
        /// </summary>
        /// <param name="containerCode">货柜编码</param>
        /// <returns>删除结果</returns>
        Task<bool> DeleteContainerAsync(string containerCode);

        /// <summary>
        /// 批量删除货柜
        /// </summary>
        /// <param name="containerCodes">货柜编码列表</param>
        /// <returns>批量操作结果</returns>
        Task<BatchOperationResponse> BatchDeleteContainersAsync(List<string> containerCodes);

        #endregion

        #region 货柜明细操作

        /// <summary>
        /// 获取货柜明细列表
        /// </summary>
        /// <param name="containerCode">货柜编码</param>
        /// <returns>明细列表</returns>
        Task<List<YiwuContainerDetailDto>> GetContainerDetailsAsync(string containerCode);

        /// <summary>
        /// 获取货柜明细详情
        /// </summary>
        /// <param name="detailCode">明细编码</param>
        /// <returns>明细详情</returns>
        Task<YiwuContainerDetailDto?> GetContainerDetailAsync(string detailCode);

        /// <summary>
        /// 创建货柜明细
        /// </summary>
        /// <param name="detailDto">明细DTO</param>
        /// <returns>创建结果</returns>
        Task<string> CreateContainerDetailAsync(YiwuContainerDetailDto detailDto);

        /// <summary>
        /// 更新货柜明细
        /// </summary>
        /// <param name="detailDto">明细DTO</param>
        /// <returns>更新结果</returns>
        Task<bool> UpdateContainerDetailAsync(YiwuContainerDetailDto detailDto);

        /// <summary>
        /// 删除货柜明细
        /// </summary>
        /// <param name="detailCode">明细编码</param>
        /// <returns>删除结果</returns>
        Task<bool> DeleteContainerDetailAsync(string detailCode);

        /// <summary>
        /// 批量添加货柜明细（通过货号和件数）
        /// </summary>
        /// <param name="request">批量添加请求</param>
        /// <returns>批量操作结果</returns>
        Task<BatchOperationResponse> BatchAddContainerDetailsAsync(BatchAddYiwuContainerDetailsRequest request);

        /// <summary>
        /// 批量删除货柜明细
        /// </summary>
        /// <param name="detailCodes">明细编码列表</param>
        /// <returns>批量操作结果</returns>
        Task<BatchOperationResponse> BatchDeleteContainerDetailsAsync(List<string> detailCodes);

        /// <summary>
        /// 批量更新货柜明细
        /// </summary>
        /// <param name="details">明细列表</param>
        /// <returns>批量操作结果</returns>
        Task<BatchOperationResponse> BatchUpdateContainerDetailsAsync(List<YiwuContainerDetailDto> details);

        #endregion

        #region 业务逻辑

        /// <summary>
        /// 重新计算货柜汇总信息
        /// </summary>
        /// <param name="containerCode">货柜编码</param>
        /// <returns>计算结果</returns>
        Task<bool> RecalculateContainerSummaryAsync(string containerCode);

        /// <summary>
        /// 分摊运输成本到各个商品
        /// </summary>
        /// <param name="containerCode">货柜编码</param>
        /// <returns>分摊结果</returns>
        Task<bool> AllocateTransportCostAsync(string containerCode);

        /// <summary>
        /// 根据商品信息查询相关货柜
        /// </summary>
        /// <param name="itemNumber">商品货号</param>
        /// <returns>相关货柜列表</returns>
        Task<List<YiwuContainerDto>> GetContainersByItemNumberAsync(string itemNumber);

        /// <summary>
        /// 获取货柜状态选项
        /// </summary>
        /// <returns>状态选项列表</returns>
        Task<List<KeyValuePair<int, string>>> GetContainerStatusOptionsAsync();

        /// <summary>
        /// 验证货柜明细数据
        /// </summary>
        /// <param name="details">明细列表</param>
        /// <returns>验证结果</returns>
        Task<(bool IsValid, List<string> Errors)> ValidateContainerDetailsAsync(List<BatchYiwuContainerDetailItem> details);

        /// <summary>
        /// 批量翻译商品名称
        /// </summary>
        /// <param name="chineseNames">中文名称列表</param>
        /// <returns>翻译结果</returns>
        Task<Dictionary<string, string>> BatchTranslateProductNamesAsync(List<string> chineseNames);

        /// <summary>
        /// 批量更新国内商品信息
        /// </summary>
        /// <param name="products">商品列表</param>
        /// <returns>批量操作结果</returns>
        Task<BatchOperationResponse> BatchUpdateDomesticProductsAsync(List<DomesticProductDto> products);

        /// <summary>
        /// 导出货柜明细到Excel
        /// </summary>
        /// <param name="request">导出请求</param>
        /// <returns>导出结果</returns>
        Task<ApiResponse<FileExportResponse>> ExportContainerDetailsToExcelAsync(ContainerDetailsExportRequest request);

        /// <summary>
        /// 导出货柜明细到PDF
        /// </summary>
        /// <param name="request">导出请求</param>
        /// <returns>导出结果</returns>
        Task<ApiResponse<FileExportResponse>> ExportContainerDetailsToPdfAsync(ContainerDetailsExportRequest request);

        #endregion
    }
}
