using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    public interface IWarehouseCategoryService
    {
        Task<WarehouseCategoryDto> GetByIdAsync(string categoryGuid);
        Task<List<WarehouseCategoryDto>> GetAllAsync();
        Task<PagedResult<WarehouseCategoryDto>> GetAllAsync(WarehouseCategoryFilterDto filter);
        Task<WarehouseCategoryDto> CreateAsync(CreateWarehouseCategoryDto createDto);
        Task<WarehouseCategoryDto> UpdateAsync(UpdateWarehouseCategoryDto updateDto);
        Task<bool> DeleteAsync(string categoryGuid);
        Task<List<WarehouseCategoryDto>> GetChildrenAsync(string parentGuid);
        Task<List<WarehouseCategoryDto>> GetActiveCategoriesAsync();
        Task<bool> HasChildCategoriesAsync(string categoryGuid);
        Task<bool> HasProductsAsync(string categoryGuid);
    }
}