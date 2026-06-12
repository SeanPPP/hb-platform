using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 专用商品前缀服务接口（与原有 IProductPrefixCodeService 解耦）
    /// 仅包含 React 控制器所需的方法
    /// </summary>
    public interface IProductPrefixCodeReactService
    {
        Task<ApiResponse<List<SimpleProductPrefixCodeDto>>> GetPrefixesBySupplierCodeAsync(string supplierCode);
        Task<ApiResponse<PagedResult<ProductPrefixCodeDto>>> GetAllPrefixesAsync(ProductPrefixCodeQueryDto query);
        Task<ApiResponse<PagedResult<DomesticProductDto>>> GetProductsByPrefixCodeAsync(string prefixCode, int page, int pageSize);
        Task<ApiResponse<ProductPrefixCodeDto>> CreateProductPrefixCodeAsync(CreateProductPrefixCodeDto dto);
        Task<ApiResponse<ProductPrefixCodeDto>> UpdateProductPrefixCodeAsync(string prefixCode, UpdateProductPrefixCodeDto dto);
        Task<ApiResponse<bool>> DeleteProductPrefixCodeAsync(string prefixCode);
    }
}