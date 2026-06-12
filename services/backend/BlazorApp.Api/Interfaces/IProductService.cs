using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    public interface IProductService
    {
        Task<ProductDto> GetByIdAsync(string productGuid);
        Task<PagedResult<ProductDto>> GetAllAsync(ProductFilterDto filter);
        Task<ProductDto> CreateAsync(CreateProductDto createDto);
        Task<ProductDto> UpdateAsync(UpdateProductDto updateDto);
        Task<bool> DeleteAsync(string productGuid);
        Task<List<ProductDto>> GetByCategoryAsync(string productCategoryGuid);
        Task<List<ProductDto>> GetByWarehouseCategoryAsync(string warehouseCategoryGuid);
        Task<List<ProductDto>> SearchByBarcodeAsync(string barcode);
        Task<bool> ExistsAsync(string productCode);
        Task<bool> ToggleActiveStatusAsync(string productGuid);
        Task<List<ProductDto>> GetByCodesAsync(List<string> productCodes);
    }
}