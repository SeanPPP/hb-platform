using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 仓库商品服务接口
    /// </summary>
    public interface IWarehouseProductService
    {
        /// <summary>
        /// 分页查询仓库商品
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页结果</returns>
        Task<WarehouseProductPagedResultDto> GetPagedProductsAsync(WarehouseProductQueryDto query);

        /// <summary>
        /// 根据商品编码获取商品详情
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>商品详情</returns>
        Task<WarehouseProductDto?> GetProductByCodeAsync(string productCode);

        /// <summary>
        /// 根据货号查询商品（支持ItemNumber, ProductCode, Barcode）
        /// </summary>
        /// <param name="itemNumber">货号</param>
        /// <returns>商品详情</returns>
        Task<WarehouseProductDto?> GetProductByItemNumberAsync(string itemNumber);

        /// <summary>
        /// 批量通过货号查询商品（优化版本，只通过ItemNumber匹配）
        /// </summary>
        /// <param name="itemNumbers">货号列表</param>
        /// <returns>商品字典，Key为货号，Value为商品信息</returns>
        Task<Dictionary<string, WarehouseProductDto>> BatchGetProductsByItemNumbersAsync(List<string> itemNumbers);

        /// <summary>
        /// 创建商品
        /// </summary>
        /// <param name="productDto">商品DTO</param>
        /// <returns>创建的商品</returns>
        Task<WarehouseProductDto> CreateProductAsync(CreateWarehouseProductDto productDto);

        /// <summary>
        /// 更新商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="productDto">商品DTO</param>
        /// <returns>更新的商品</returns>
        Task<WarehouseProductDto?> UpdateProductAsync(string productCode, UpdateWarehouseProductDto productDto);

        /// <summary>
        /// 删除商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteProductAsync(string productCode);

        /// <summary>
        /// 批量更新商品状态
        /// </summary>
        /// <param name="productCodes">商品编码列表</param>
        /// <param name="isActive">是否启用</param>
        /// <returns>更新数量</returns>
        Task<int> BatchUpdateProductStatusAsync(List<string> productCodes, bool isActive);

        /// <summary>
        /// 获取库存预警商品
        /// </summary>
        /// <param name="locationGuids">仓库位置GUID列表</param>
        /// <returns>预警商品列表</returns>
        Task<List<WarehouseProductListDto>> GetStockAlertProductsAsync(List<string>? locationGuids = null);

        /// <summary>
        /// 更新商品库存
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="stockQuantity">库存数量</param>
        /// <param name="stockValue">库存金额</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdateProductStockAsync(string productCode, int stockQuantity, decimal? stockValue = null);

        /// <summary>
        /// 根据条码搜索商品
        /// </summary>
        /// <param name="barcode">条码</param>
        /// <returns>商品列表</returns>
        Task<List<WarehouseProductListDto>> SearchProductsByBarcodeAsync(string barcode);

        /// <summary>
        /// 获取商品统计信息
        /// </summary>
        /// <param name="categoryGuid">分类GUID（可选）</param>
        /// <param name="locationGuids">仓库位置GUID列表（可选）</param>
        /// <returns>统计信息</returns>
        Task<WarehouseProductStatsDto> GetProductStatsAsync(string? categoryGuid = null, List<string>? locationGuids = null);

        /// <summary>
        /// 导出商品数据
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>商品列表</returns>
        Task<List<WarehouseProductListDto>> ExportProductsAsync(WarehouseProductQueryDto query);

        /// <summary>
        /// 检查商品编码是否存在
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>是否存在</returns>
        Task<bool> IsProductCodeExistsAsync(string productCode);

        /// <summary>
        /// 检查条码是否存在
        /// </summary>
        /// <param name="barcode">条码</param>
        /// <param name="excludeProductCode">排除的商品编码</param>
        /// <returns>是否存在</returns>
        Task<bool> IsBarcodeExistsAsync(string barcode, string? excludeProductCode = null);
    }
}