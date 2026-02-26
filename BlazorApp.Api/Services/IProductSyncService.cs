using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 商品同步服务接口
    /// 提供商品检测、批量创建、批量更新等功能
    /// </summary>
    public interface IProductSyncService
    {
        /// <summary>
        /// 批量检测商品是否存在
        /// </summary>
        /// <param name="request">检测请求，包含商品编码和货号列表</param>
        /// <returns>检测结果，包含商品是否存在以及仓库商品信息</returns>
        Task<BatchProductOperationResponse> DetectProductsAsync(BatchProductDetectionRequest request);

        /// <summary>
        /// 批量更新仓库商品信息
        /// 更新WarehouseProduct、Product.PurchasePrice、StoreRetailPrice.PurchasePrice
        /// </summary>
        /// <param name="request">更新请求，包含要更新的商品信息</param>
        /// <returns>更新结果，包含成功/失败数量和错误信息</returns>
        Task<BatchProductOperationResponse> BatchUpdateWarehouseProductsAsync(BatchProductUpdateRequest request);

        /// <summary>
        /// 批量创建商品信息（含二次检查和事务处理）
        /// 创建Product、WarehouseProduct、StoreRetailPrice（所有活跃店铺）
        /// 如果是套装商品，同时创建ProductSetCode和StoreMultiCodeProduct
        /// </summary>
        /// <param name="request">创建请求，包含要创建的商品信息</param>
        /// <returns>创建结果，包含成功/失败/跳过数量和详细信息</returns>
        Task<BatchProductOperationResponse> BatchCreateProductsAsync(BatchProductCreateRequest request);
    }
}

