using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 购物车服务接口
    /// </summary>
    public interface ICartService
    {
        /// <summary>
        /// 获取用户的购物车（不绑定门店）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <returns>购物车DTO</returns>
        Task<CartDto?> GetUserCartAsync(string userGuid);

        /// <summary>
        /// 获取或创建用户购物车（不绑定门店）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <returns>购物车DTO</returns>
        Task<CartDto> GetOrCreateUserCartAsync(string userGuid);

        /// <summary>
        /// 添加商品到购物车
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">添加到购物车请求</param>
        /// <returns>操作结果</returns>
        Task<bool> AddToCartAsync(string userGuid, AddToCartRequest request);

        /// <summary>
        /// 从购物车移除商品
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="cartItemGuid">购物车项GUID</param>
        /// <returns>操作结果</returns>
        Task<bool> RemoveFromCartAsync(string userGuid, string cartItemGuid);

        /// <summary>
        /// 更新购物车项数量
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">更新请求</param>
        /// <returns>操作结果</returns>
        Task<bool> UpdateCartItemQuantityAsync(
            string userGuid,
            UpdateCartItemQuantityRequest request
        );

        /// <summary>
        /// 清空购物车
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <returns>操作结果</returns>
        Task<bool> ClearCartAsync(string userGuid);

        /// <summary>
        /// 同步本地购物车到服务器
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">同步请求</param>
        /// <returns>服务器端购物车</returns>
        Task<CartDto> SyncCartAsync(string userGuid, CartSyncRequest request);

        /// <summary>
        /// 批量更新购物车项数量
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="updates">更新字典(CartItemGUID -> 新数量)</param>
        /// <returns>操作结果</returns>
        Task<bool> BatchUpdateCartItemQuantitiesAsync(
            string userGuid,
            Dictionary<string, int> updates
        );

        /// <summary>
        /// 批量移除购物车项
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="cartItemGuids">购物车项GUID列表</param>
        /// <returns>操作结果</returns>
        Task<bool> BatchRemoveCartItemsAsync(string userGuid, List<string> cartItemGuids);

        /// <summary>
        /// 获取购物车统计信息
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <returns>购物车统计</returns>
        Task<CartSummaryDto> GetCartSummaryAsync(string userGuid);

        /// <summary>
        /// 检查商品是否在购物车中
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="productCode">商品代码</param>
        /// <returns>是否在购物车中</returns>
        Task<bool> IsProductInCartAsync(string userGuid, string productCode);

        /// <summary>
        /// 获取商品在购物车中的数量
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="productCode">商品代码</param>
        /// <returns>数量</returns>
        Task<int> GetProductQuantityInCartAsync(string userGuid, string productCode);

        /// <summary>
        /// 批量检查商品是否在购物车中
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="productCodes">商品代码列表</param>
        /// <returns>商品购物车状态字典(ProductCode -> (InCart, Quantity))</returns>
        Task<Dictionary<string, (bool InCart, int Quantity)>> BatchCheckProductsInCartAsync(
            string userGuid,
            List<string> productCodes
        );

        /// <summary>
        /// 清理过期的购物车
        /// </summary>
        /// <returns>清理的购物车数量</returns>
        Task<int> CleanExpiredCartsAsync();

        /// <summary>
        /// 从购物车创建订单（选择门店）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">创建订单请求</param>
        /// <returns>订单GUID</returns>
        Task<string?> CreateOrderFromCartAsync(string userGuid, CreateOrderFromCartRequest request);

        /// <summary>
        /// 保存购物车状态（更新状态为Save）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">保存购物车请求</param>
        /// <returns>操作结果</returns>
        Task<bool> SaveCartStatusAsync(string userGuid, SaveCartStatusRequest request);

        /// <summary>
        /// 提交购物车（Checkout - 更新状态为Submitted）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">提交购物车请求</param>
        /// <returns>订单号</returns>
        Task<string?> SubmitCartAsync(string userGuid, SubmitCartRequest request);

        /// <summary>
        /// 获取用户购物车列表（支持状态过滤和分页）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">查询请求</param>
        /// <returns>购物车列表</returns>
        Task<CartListResponse> GetCartListAsync(string userGuid, CartListRequest request);

        /// <summary>
        /// 生成下一个订单号
        /// </summary>
        /// <returns>新订单号</returns>
        Task<string> GenerateNextOrderNumberAsync();

        /// <summary>
        /// 检查用户是否有Active状态的购物车
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <returns>检查结果</returns>
        Task<ActiveCartCheckResponse> CheckActiveCartAsync(string userGuid);

        /// <summary>
        /// 切换购物车状态
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">状态切换请求</param>
        /// <returns>操作结果</returns>
        Task<bool> SwitchCartStatusAsync(string userGuid, CartStatusSwitchRequest request);

        /// <summary>
        /// 合并购物车
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">合并请求</param>
        /// <returns>操作结果</returns>
        Task<bool> MergeCartsAsync(string userGuid, CartMergeRequest request);

        /// <summary>
        /// 根据购物车GUID获取购物车商品详情
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>购物车商品列表</returns>
        Task<List<CartItemDto>> GetCartItemsByCartGuidAsync(string cartGuid);

        /// <summary>
        /// 软删除购物车（仅限Saved状态的购物车）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>操作结果</returns>
        Task<bool> SoftDeleteCartAsync(string userGuid, string cartGuid);

        /// <summary>
        /// 恢复删除的购物车（将状态从Deleted改回Save）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>操作结果</returns>
        Task<bool> RestoreCartAsync(string userGuid, string cartGuid);

        /// <summary>
        /// 更新购物车备注
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="remarks">备注内容</param>
        /// <returns>操作结果</returns>
        Task<bool> UpdateCartRemarksAsync(string userGuid, string? remarks);

        /// <summary>
        /// 根据购物车GUID获取购物车详情
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <returns>购物车详情</returns>
        Task<CartDto?> GetCartByIdAsync(string cartGuid);

        /// <summary>
        /// 更改订单所属分店
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="newStoreGuid">新分店GUID</param>
        /// <param name="reason">更改原因</param>
        /// <param name="userGuid">操作人</param>
        /// <returns>操作结果</returns>
        Task<bool> ChangeOrderStoreAsync(
            string userGuid,
            string cartGuid,
            string newStoreGuid,
            string reason
        );

        /// <summary>
        /// 更新购物车项价格
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">价格更新请求</param>
        /// <returns>操作结果</returns>
        Task<bool> UpdateCartItemPriceAsync(string userGuid, UpdateCartItemPriceRequest request);

        /// <summary>
        /// 批量更新购物车项价格
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="updates">价格更新字典</param>
        /// <returns>操作结果</returns>
        Task<bool> BatchUpdateCartItemPricesAsync(
            string userGuid,
            Dictionary<string, decimal?> updates
        );

        /// <summary>
        /// 更新购物车折扣和运费
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="discount">折扣金额</param>
        /// <param name="freightFee">运费</param>
        /// <param name="userGuid">操作人</param>
        ///
        /// <returns>操作结果</returns>
        Task<bool> UpdateCartDiscountAndFreightAsync(
            string cartGuid,
            decimal? discount,
            decimal? freightFee,
            string userGuid
        );

        #region 仓库管理员订单管理功能

        /// <summary>
        /// 仓库管理员创建分店订单
        /// </summary>
        /// <param name="userGuid">创建者用户GUID</param>
        /// <param name="request">创建订单请求</param>
        /// <returns>新创建的订单</returns>
        Task<CartDto?> CreateStoreOrderAsync(string userGuid, CreateStoreOrderRequest request);

        /// <summary>
        /// 通过货号批量查询商品信息
        /// </summary>
        /// <param name="itemNumbers">货号列表</param>
        /// <returns>商品信息列表</returns>
        Task<List<ProductSearchResult>> BatchSearchProductsAsync(List<string> itemNumbers);

        /// <summary>
        /// 批量添加商品到订单
        /// </summary>
        /// <param name="request">批量添加请求</param>
        /// <param name="userGuid">操作人</param>
        /// <returns>添加结果</returns>
        Task<BatchAddResult> BatchAddItemsToCartAsync(
            BatchAddItemsRequest request,
            string userGuid
        );

        /// <summary>
        /// 搜索商品（支持分类和关键字过滤）
        /// </summary>
        /// <param name="request">搜索请求</param>
        /// <returns>搜索结果</returns>
        Task<ProductSearchResponse> SearchProductsAsync(ProductSearchRequest request);

        /// <summary>
        /// 清除指定购物车的所有商品（仓库管理员功能）
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="userGuid">操作人</param>
        /// <returns>操作结果</returns>
        Task<bool> ClearCartByIdAsync(string cartGuid, string userGuid);

        /// <summary>
        /// 批量删除指定购物车的商品项（仓库管理员功能）
        /// </summary>
        /// <param name="cartGuid">购物车GUID</param>
        /// <param name="cartItemGuids">购物车项GUID列表</param>
        /// <param name="userGuid">操作人</param>
        /// <returns>操作结果</returns>
        Task<bool> BatchRemoveCartItemsByCartIdAsync(
            string cartGuid,
            List<string> cartItemGuids,
            string userGuid
        );

        /// <summary>
        /// Excel导入商品到购物车（仓库管理员功能）
        /// </summary>
        /// <param name="request">Excel导入请求</param>
        /// <param name="userGuid">操作人</param>
        /// <returns>导入结果</returns>
        Task<ExcelImportResult> ImportExcelItemsToCartAsync(
            ExcelImportRequest request,
            string userGuid
        );

        #endregion

        #region 用户订单和仓库订单分离查询

        /// <summary>
        /// 获取用户相关订单列表（用户创建和关联分店的订单）
        /// </summary>
        /// <param name="userGuid">用户GUID</param>
        /// <param name="request">查询请求</param>
        /// <returns>订单列表</returns>
        Task<CartListResponse> GetUserRelatedOrdersAsync(string userGuid, CartListRequest request);

        /// <summary>
        /// 获取所有订单列表（仓库管理员视图）
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>订单列表</returns>
        Task<CartListResponse> GetAllOrdersAsync(CartListRequest request);

        #endregion

        #region PDA设备专用购物车操作方法

        /// <summary>
        /// PDA设备创建购物车（基于设备ID和分店信息）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="storeGuid">分店GUID</param>
        /// <param name="cartName">购物车名称</param>
        /// <param name="remarks">备注</param>
        /// <returns>创建的购物车DTO</returns>
        Task<CartDto?> CreatePDACartAsync(
            string deviceId,
            string storeGuid,
            string? cartName = null,
            string? remarks = null
        );

        /// <summary>
        /// PDA设备更新购物车信息
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="cartName">购物车名称</param>
        /// <param name="remarks">备注</param>
        /// <returns>更新结果</returns>
        Task<CartDto?> UpdatePDACartAsync(
            string deviceId,
            string cartId,
            string? cartName = null,
            string? remarks = null
        );

        /// <summary>
        /// PDA设备获取购物车列表（基于设备关联的分店）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="storeGuid">分店GUID（可选）</param>
        /// <param name="request">查询请求</param>
        /// <returns>购物车列表</returns>
        Task<CartListResponse> GetPDACartListAsync(
            string deviceId,
            string? storeGuid,
            CartListRequest request
        );

        /// <summary>
        /// PDA设备获取购物车详情
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <returns>购物车详情</returns>
        Task<CartDto?> GetPDACartByIdAsync(string deviceId, string cartId);

        /// <summary>
        /// PDA设备搜索商品（专用方法）
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="storeGuid">分店GUID（用于库存查询）</param>
        /// <param name="pageSize">每页数量</param>
        /// <returns>商品搜索结果</returns>
        Task<List<ProductDto>> SearchPDAProductsAsync(
            string deviceId,
            string keyword,
            string? storeGuid = null,
            int pageSize = 50
        );

        /// <summary>
        /// PDA设备添加商品到购物车
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="productCode">商品代码</param>
        /// <param name="quantity">数量</param>
        /// <param name="unitPrice">单价（可选）</param>
        /// <returns>操作结果</returns>
        Task<bool> AddProductToPDACartAsync(
            string deviceId,
            string cartId,
            string productCode,
            int quantity,
            decimal? unitPrice = null
        );

        /// <summary>
        /// PDA设备批量添加商品到购物车
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="items">商品列表</param>
        /// <returns>批量添加结果</returns>
        Task<(
            int successCount,
            int failureCount,
            List<string> errors
        )> BatchAddProductsToPDACartAsync(
            string deviceId,
            string cartId,
            List<(string productCode, int quantity, decimal? unitPrice)> items
        );

        /// <summary>
        /// PDA设备更新购物车商品数量
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="cartItemId">购物车项ID</param>
        /// <param name="newQuantity">新数量</param>
        /// <returns>操作结果</returns>
        Task<bool> UpdatePDACartItemQuantityAsync(
            string deviceId,
            string cartId,
            string cartItemId,
            int newQuantity
        );

        /// <summary>
        /// PDA设备从购物车移除商品
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="cartId">购物车ID</param>
        /// <param name="cartItemId">购物车项ID</param>
        /// <returns>操作结果</returns>
        Task<bool> RemoveProductFromPDACartAsync(string deviceId, string cartId, string cartItemId);

        #endregion
    }
}
