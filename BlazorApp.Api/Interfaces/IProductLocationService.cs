using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    public interface IProductLocationService
    {
        Task<ProductLocationDto> GetByIdAsync(string guid);
        Task<PagedResult<ProductLocationDto>> GetAllAsync(ProductLocationFilterDto filter);
        Task<ProductLocationDto> CreateAsync(CreateProductLocationDto createDto);
        Task<ProductLocationDto> UpdateAsync(UpdateProductLocationDto updateDto);
        Task<bool> DeleteAsync(string guid);
        Task<bool> DeleteByProductAndLocationAsync(string productCode, string locationGuid);
        Task<List<LocationDto>> GetLocationsByProductAsync(string productCode);
        Task<List<WarehouseProductDto>> GetProductsByLocationAsync(string locationGuid);
        Task<ProductWithLocationsDto> GetProductWithLocationsAsync(string productCode);
        Task<LocationWithProductsDto> GetLocationWithProductsAsync(string locationGuid);
        Task<bool> BatchCreateAsync(BatchProductLocationDto batchDto);
        Task<bool> BatchDeleteByProductAsync(string productCode);
        Task<bool> BatchDeleteByLocationAsync(string locationGuid);
        Task<bool> ExistsAsync(string productCode, string locationGuid);
    }
}