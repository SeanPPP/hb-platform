using BlazorApp.Shared.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// Product React专用服务接口
    /// 提供Product表的CRUD操作、分页查询、排序和过滤功能
    /// </summary>
    public interface IProductReactService
    {
        /// <summary>
        /// 分页查询商品列表（支持排序和过滤）
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>商品列表和总数</returns>
        Task<PagedListReactDto<ProductDto>> GetPagedListAsync(ProductReactFilterDto query);

        /// <summary>
        /// 根据ProductCode获取商品详情
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>商品详情</returns>
        Task<ApiResponse<ProductDto>> GetByIdAsync(string productCode);

        /// <summary>
        /// 创建商品
        /// </summary>
        /// <param name="dto">创建DTO</param>
        /// <returns>创建结果</returns>
        Task<ApiResponse<ProductDto>> CreateAsync(CreateProductDto dto);

        /// <summary>
        /// 更新商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="dto">更新DTO</param>
        /// <returns>更新结果</returns>
        Task<ApiResponse<ProductDto>> UpdateAsync(string productCode, UpdateProductDto dto);

        /// <summary>
        /// 删除商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> DeleteAsync(string productCode);

        /// <summary>
        /// 批量更新商品（使用事务）
        /// </summary>
        /// <param name="items">批量更新项</param>
        /// <returns>批量操作结果</returns>
        Task<ApiResponse<BatchOperationReactResult>> BatchUpdateAsync(List<BatchUpdateProductReactDto> items);

        /// <summary>
        /// 批量删除商品（使用事务）
        /// </summary>
        /// <param name="productCodes">商品编码列表</param>
        /// <returns>批量操作结果</returns>
        Task<ApiResponse<BatchOperationReactResult>> BatchDeleteAsync(List<string> productCodes);

        /// <summary>
        /// 高级过滤查询商品列表（支持商品信息表与分店价格表的组合过滤）
        /// </summary>
        /// <param name="filter">过滤条件</param>
        /// <returns>商品价格列表和总数</returns>
        Task<PagedProductPriceListDto> GetPriceFilteredPagedListAsync(ProductPriceFilterDto filter);
    }
}
