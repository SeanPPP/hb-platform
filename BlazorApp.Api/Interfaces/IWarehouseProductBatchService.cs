using BlazorApp.Shared.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 仓库商品批量管理服务接口
    /// </summary>
    public interface IWarehouseProductBatchService
    {
        /// <summary>
        /// 根据过滤条件获取商品列表（分页）
        /// </summary>
        /// <param name="filter">过滤条件</param>
        /// <returns>分页结果</returns>
        Task<PagedResultDto<WarehouseProductBatchDto>> GetByFilterAsync(WarehouseProductBatchFilterDto filter);

        /// <summary>
        /// 批量更新商品信息（全部保存）
        /// 使用事务确保数据一致性
        /// </summary>
        /// <param name="request">批量更新请求</param>
        /// <returns>更新结果</returns>
        Task<BatchUpdateResult> BatchUpdateAsync(BatchUpdateRequest request);

        /// <summary>
        /// 增量保存（保存单条或部分修改）
        /// </summary>
        /// <param name="request">增量保存请求</param>
        /// <returns>保存结果</returns>
        Task<IncrementalSaveResult> IncrementalSaveAsync(IncrementalSaveRequest request);

        /// <summary>
        /// 批量设置价格
        /// </summary>
        /// <param name="request">批量设置价格请求</param>
        /// <returns>操作结果</returns>
        Task<BulkOperationResult> BulkSetPriceAsync(BulkSetPriceRequest request);

        /// <summary>
        /// 批量调整库存
        /// </summary>
        /// <param name="request">批量调整库存请求</param>
        /// <returns>操作结果</returns>
        Task<BulkOperationResult> BulkAdjustStockAsync(BulkAdjustStockRequest request);

        /// <summary>
        /// 批量设置使用状态
        /// </summary>
        /// <param name="request">批量设置状态请求</param>
        /// <returns>操作结果</returns>
        Task<BulkOperationResult> BulkSetStatusAsync(BulkSetStatusRequest request);

        /// <summary>
        /// 批量设置仓位
        /// </summary>
        /// <param name="request">批量设置仓位请求</param>
        /// <returns>操作结果</returns>
        Task<BulkOperationResult> BulkSetLocationAsync(BulkSetLocationRequest request);

        /// <summary>
        /// 更新单个商品的仓位信息
        /// </summary>
        /// <param name="request">仓位编辑请求</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdateLocationAsync(LocationEditDto request);

        /// <summary>
        /// 获取所有可用仓位列表
        /// </summary>
        /// <returns>仓位列表</returns>
        Task<List<LocationOptionDto>> GetAvailableLocationsAsync();

        /// <summary>
        /// 导出为Excel
        /// </summary>
        /// <param name="filter">过滤条件（导出符合条件的数据）</param>
        /// <returns>Excel文件字节数组</returns>
        Task<byte[]> ExportToExcelAsync(WarehouseProductBatchFilterDto filter);

        /// <summary>
        /// 导出为PDF
        /// </summary>
        /// <param name="filter">过滤条件（导出符合条件的数据）</param>
        /// <returns>PDF文件字节数组</returns>
        Task<byte[]> ExportToPdfAsync(WarehouseProductBatchFilterDto filter);
    }
}

