using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IStoreOrderReactService
    {
        /// <summary>
        /// 获取订货商品列表
        /// </summary>
        Task<PagedListReactDto<StoreOrderProductDto>> GetPagedListAsync(StoreOrderFilterDto filter);

        /// <summary>
        /// 获取分店当前的购物车 (FlowStatus=0)
        /// </summary>
        Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartAsync(string storeCode);

        /// <summary>
        /// 添加到购物车 (或更新数量)
        /// </summary>
        Task<ApiResponse<bool>> AddToCartAsync(AddToCartRequestDto request);

        /// <summary>
        /// 更新购物车项数量 (覆盖)
        /// </summary>
        Task<ApiResponse<bool>> UpdateCartItemAsync(AddToCartRequestDto request);

        /// <summary>
        /// 移除购物车项
        /// </summary>
        Task<ApiResponse<bool>> RemoveFromCartAsync(RemoveFromCartRequestDto request);

        /// <summary>
        /// 清空购物车 (删除 FlowStatus=0 的订单的所有明细)
        /// </summary>
        Task<ApiResponse<StoreOrderCartDto?>> ClearCartAsync(string storeCode);

        /// <summary>
        /// 提交订单 (FlowStatus 0 -> 1)
        /// </summary>
        Task<ApiResponse<bool>> SubmitOrderAsync(SubmitStoreOrderRequestDto request);

        /// <summary>
        /// 批量获取商品动态数据 (历史订单 + 购物车数量)
        /// </summary>
        Task<ApiResponse<List<StoreOrderDynamicDataDto>>> GetProductsDynamicDataAsync(
            StoreOrderDynamicDataRequestDto request
        );

        /// <summary>
        /// 获取订单列表
        /// </summary>
        Task<PagedListReactDto<StoreOrderListItemDto>> GetOrderListAsync(StoreOrderListFilterDto filter);

        /// <summary>
        /// 获取订单详情
        /// </summary>
        Task<ApiResponse<StoreOrderCartDto?>> GetOrderDetailAsync(string orderGuid);

        /// <summary>
        /// 获取订单中使用过的分店信息
        /// </summary>
        Task<ApiResponse<List<BranchDto>>> GetUsedBranchesAsync();

        /// <summary>
        /// 创建新订单 (FlowStatus=1)
        /// </summary>
        Task<ApiResponse<string>> CreateOrderAsync(CreateStoreOrderDto request);

        /// <summary>
        /// 添加商品到指定订单
        /// </summary>
        Task<ApiResponse<bool>> AddOrderLineAsync(AddOrderLineDto request);

        /// <summary>
        /// 批量添加商品到指定订单
        /// </summary>
        Task<ApiResponse<bool>> BatchAddOrderLineAsync(BatchAddOrderLineDto request);

        /// <summary>
        /// 更新指定订单行数量
        /// </summary>
        Task<ApiResponse<bool>> UpdateOrderLineAsync(UpdateOrderLineDto request);

        /// <summary>
        /// 软删除指定订单行
        /// </summary>
        Task<ApiResponse<bool>> RemoveOrderLineAsync(RemoveOrderLineDto request);

        /// <summary>
        /// 批量更新订单行 (数量或价格)
        /// </summary>
        Task<ApiResponse<bool>> BatchUpdateOrderLineAsync(BatchUpdateOrderLineDto request);

        /// <summary>
        /// 更新订单头信息
        /// </summary>
        Task<ApiResponse<bool>> UpdateOrderHeaderAsync(UpdateOrderHeaderDto request);

        /// <summary>
        /// 软删除订单
        /// </summary>
        Task<ApiResponse<bool>> DeleteOrderAsync(string orderGuid);

        /// <summary>
        /// 更新商品状态
        /// </summary>
        Task<ApiResponse<bool>> UpdateProductStatusAsync(UpdateProductStatusDto request);

        /// <summary>
        /// 批量更新商品状态
        /// </summary>
        Task<ApiResponse<bool>> BatchUpdateProductStatusAsync(BatchUpdateProductStatusDto request);

        /// <summary>
        /// 复制订单到另一个分店
        /// </summary>
        Task<ApiResponse<CopyOrderResultDto>> CopyOrderAsync(CopyOrderDto request);

        /// <summary>
        /// 从 HQ 同步本地不存在的仓库订单（主表 + 明细表）
        /// </summary>
        /// <param name="storeCode">分店代码</param>
        /// <returns>同步结果</returns>
        Task<SyncMissingOrdersResultDto> SyncMissingOrdersFromHqAsync(string? storeCode);

        /// <summary>
        /// 完成订单 (FlowStatus -> 2)
        /// </summary>
        Task<ApiResponse<bool>> CompleteOrderAsync(string orderGuid);

        /// <summary>
        /// 开始配货 (FlowStatus -> 3)
        /// </summary>
        Task<ApiResponse<bool>> StartPickingAsync(string orderGuid);

        /// <summary>
        /// 更新订单状态 (支持双向切换 Submitted/Completed)
        /// </summary>
        Task<ApiResponse<bool>> UpdateOrderStatusAsync(string orderGuid, int newStatus);

        /// <summary>
        /// 批量更新订单状态
        /// </summary>
        Task<ApiResponse<int>> BatchUpdateOrderStatusAsync(List<string> orderGuids, int newStatus);
    }
}
