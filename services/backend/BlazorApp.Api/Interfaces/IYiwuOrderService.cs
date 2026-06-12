using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 义乌订单服务接口
    /// </summary>
    public interface IYiwuOrderService
    {
        #region 义乌订单主表相关
        /// <summary>
        /// 获取义乌订单列表
        /// </summary>
        Task<PagedResult<YIWU_Order>> GetOrdersAsync(int pageIndex = 1, int pageSize = 20, string? keyword = null);

        /// <summary>
        /// 根据ID获取义乌订单详情
        /// </summary>
        Task<YIWU_Order?> GetOrderByIdAsync(int orderId);

        /// <summary>
        /// 根据订单编号获取义乌订单
        /// </summary>
        Task<YIWU_Order?> GetOrderByOrderNoAsync(string orderNo);

        /// <summary>
        /// 创建义乌订单
        /// </summary>
        Task<YIWU_Order> CreateOrderAsync(YIWU_Order order);

        /// <summary>
        /// 更新义乌订单
        /// </summary>
        Task<bool> UpdateOrderAsync(YIWU_Order order);

        /// <summary>
        /// 删除义乌订单
        /// </summary>
        Task<bool> DeleteOrderAsync(int orderId);

        /// <summary>
        /// 生成新的订单编号
        /// </summary>
        Task<string> GenerateOrderNoAsync();
        #endregion

        #region 义乌订单明细相关
        /// <summary>
        /// 获取订单明细列表
        /// </summary>
        Task<PagedResult<YIWU_OrderDetail>> GetOrderDetailsAsync(string? orderNo = null, int pageIndex = 1, int pageSize = 20);

        /// <summary>
        /// 根据ID获取订单明细
        /// </summary>
        Task<YIWU_OrderDetail?> GetOrderDetailByIdAsync(int detailId);

        /// <summary>
        /// 创建订单明细
        /// </summary>
        Task<YIWU_OrderDetail> CreateOrderDetailAsync(YIWU_OrderDetail orderDetail);

        /// <summary>
        /// 更新订单明细
        /// </summary>
        Task<bool> UpdateOrderDetailAsync(YIWU_OrderDetail orderDetail);

        /// <summary>
        /// 删除订单明细
        /// </summary>
        Task<bool> DeleteOrderDetailAsync(int detailId);

        /// <summary>
        /// 批量创建订单明细
        /// </summary>
        Task<List<YIWU_OrderDetail>> CreateOrderDetailsAsync(List<YIWU_OrderDetail> orderDetails);
        #endregion

        #region 从PDA订单创建义乌订单
        /// <summary>
        /// 从PDA订单明细创建义乌订单（按供应商分组）
        /// </summary>
        Task<List<YIWU_Order>> CreateOrdersFromPDAAsync();

        /// <summary>
        /// 获取PDA订单明细（订单号为"PDA"的明细）
        /// </summary>
        Task<List<YIWU_OrderDetail>> GetPDAOrderDetailsAsync();

        /// <summary>
        /// 按供应商分组PDA订单明细
        /// </summary>
        Task<Dictionary<string, List<YIWU_OrderDetail>>> GroupPDADetailsBySupplierAsync();
        #endregion

        #region 导出功能
        /// <summary>
        /// 导出义乌订单到Excel
        /// </summary>
        Task<byte[]> ExportOrderToExcelAsync(int orderId);

        /// <summary>
        /// 下载并插入图片到Excel
        /// </summary>
        Task<byte[]> ExportOrderToExcelWithImagesAsync(int orderId);

        /// <summary>
        /// 批量导出多个订单到Excel（带图片）
        /// </summary>
        Task<byte[]> ExportMultipleOrdersToExcelWithImagesAsync(IEnumerable<int> orderIds, int maxConcurrency = 3);
        #endregion

        #region 统计功能
        /// <summary>
        /// 获取订单统计信息
        /// </summary>
        Task<OrderStatisticsDto> GetOrderStatisticsAsync();

        /// <summary>
        /// 更新订单总金额和总体积
        /// </summary>
        Task<bool> UpdateOrderTotalsAsync(string orderNo);
        #endregion
    }
}