using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 国内商品货号条码批量创建服务接口
    /// </summary>
    public interface IDomesticProductCreationService
    {
        /// <summary>
        /// 批量创建国内商品
        /// </summary>
        /// <param name="request">批量创建请求</param>
        /// <returns>批量创建结果</returns>
        Task<ApiResponse<CreateDomesticProductBatchResponse>> CreateBatchAsync(CreateDomesticProductBatchRequest request);

        /// <summary>
        /// 获取批次列表（分页）
        /// </summary>
        /// <param name="page">页码</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <returns>批次列表</returns>
        Task<ApiResponse<PagedResult<DomesticProductBatchDto>>> GetBatchListAsync(
            int page = 1,
            int pageSize = 20,
            string? supplierCode = null,
            DateTime? startDate = null,
            DateTime? endDate = null);

        /// <summary>
        /// 获取批次详情
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <returns>批次详情</returns>
        Task<ApiResponse<DomesticProductBatchDetailDto>> GetBatchDetailAsync(string batchNumber);

        /// <summary>
        /// 导出批次创建结果
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <returns>Excel文件</returns>
        Task<ApiResponse<DomesticProductBatchExportFileDto>> ExportBatchAsync(string batchNumber);

        /// <summary>
        /// 批量更新私牌价格
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <param name="request">更新请求</param>
        /// <returns>更新结果</returns>
        Task<ApiResponse<object>> UpdatePrivateLabelPriceAsync(string batchNumber, UpdatePrivateLabelPriceRequest request);

        /// <summary>
        /// 更新批次明细商品字段
        /// </summary>
        /// <param name="batchNumber">批次号</param>
        /// <param name="request">更新请求</param>
        /// <returns>更新结果</returns>
        Task<ApiResponse<object>> UpdateBatchItemsAsync(string batchNumber, UpdateBatchItemsRequest request);
    }
}
