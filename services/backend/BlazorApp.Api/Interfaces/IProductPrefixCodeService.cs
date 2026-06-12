using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 商品前缀管理服务接口
    /// </summary>
    public interface IProductPrefixCodeService
    {
        /// <summary>
        /// 获取商品前缀分页列表
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页结果</returns>
        Task<ApiResponse<PagedResult<ProductPrefixCodeDto>>> GetProductPrefixCodesAsync(ProductPrefixCodeQueryDto query);

        /// <summary>
        /// 根据编码获取商品前缀详情
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <returns>商品前缀详情</returns>
        Task<ApiResponse<ProductPrefixCodeDetailDto>> GetProductPrefixCodeByCodeAsync(string prefixCode);

        /// <summary>
        /// 根据供应商编码获取前缀列表
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>前缀列表</returns>
        Task<ApiResponse<List<SimpleProductPrefixCodeDto>>> GetPrefixesBySupplierCodeAsync(string supplierCode);

        /// <summary>
        /// 获取启用的前缀列表（用于下拉选择）
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <returns>启用的前缀列表</returns>
        Task<ApiResponse<List<SimpleProductPrefixCodeDto>>> GetActivePrefixesAsync(string? supplierCode = null);

        /// <summary>
        /// 创建商品前缀
        /// </summary>
        /// <param name="dto">创建商品前缀DTO</param>
        /// <returns>创建的商品前缀</returns>
        Task<ApiResponse<ProductPrefixCodeDto>> CreateProductPrefixCodeAsync(CreateProductPrefixCodeDto dto);

        /// <summary>
        /// 更新商品前缀
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <param name="dto">更新商品前缀DTO</param>
        /// <returns>更新的商品前缀</returns>
        Task<ApiResponse<ProductPrefixCodeDto>> UpdateProductPrefixCodeAsync(string prefixCode, UpdateProductPrefixCodeDto dto);

        /// <summary>
        /// 删除商品前缀
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> DeleteProductPrefixCodeAsync(string prefixCode);

        /// <summary>
        /// 切换商品前缀状态
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <param name="isActive">是否启用</param>
        /// <returns>更新的商品前缀</returns>
        Task<ApiResponse<ProductPrefixCodeDto>> TogglePrefixStatusAsync(string prefixCode, bool isActive);

        /// <summary>
        /// 检查前缀代码是否存在
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="prefixName">前缀代码</param>
        /// <param name="excludePrefixCode">排除的前缀编码（用于更新时）</param>
        /// <returns>是否存在</returns>
        Task<ApiResponse<bool>> CheckPrefixNameExistsAsync(string supplierCode, string prefixName, string? excludePrefixCode = null);

        /// <summary>
        /// 批量创建商品前缀
        /// </summary>
        /// <param name="dto">批量创建DTO</param>
        /// <returns>创建结果</returns>
        Task<ApiResponse<List<ProductPrefixCodeDto>>> BatchCreateProductPrefixCodesAsync(BatchCreateProductPrefixCodeDto dto);

        /// <summary>
        /// 批量删除商品前缀
        /// </summary>
        /// <param name="prefixCodes">前缀编码列表</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> BatchDeleteProductPrefixCodesAsync(List<string> prefixCodes);

        /// <summary>
        /// 更新前缀排序
        /// </summary>
        /// <param name="prefixCodes">前缀编码和排序顺序的字典</param>
        /// <returns>更新结果</returns>
        Task<ApiResponse<bool>> UpdatePrefixSortOrderAsync(Dictionary<string, int> prefixCodes);
    }
}
