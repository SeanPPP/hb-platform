using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IWarehouseCategoryReactService
    {
        Task<List<WarehouseCategoryDto>> GetTreeAsync();
        Task<PagedResult<WarehouseCategoryDto>> GetListAsync(WarehouseCategoryFilterDto filter);
        Task<WarehouseCategoryDto> CreateAsync(CreateWarehouseCategoryDto dto);
        Task<WarehouseCategoryDto> UpdateAsync(UpdateWarehouseCategoryDto dto);
        Task<bool> DeleteAsync(string categoryGuid);

        Task<bool> BatchMoveAsync(BatchMoveCategoriesDto dto);
        Task<int> BatchToggleActiveAsync(BatchToggleActiveDto dto);
        Task<bool> BatchSortAsync(BatchSortRequestDto dto);

        Task<WarehouseProductPagedResultDto> GetProductsByCategoryAsync(string categoryGuid, WarehouseProductFilterDto filter);
        Task<int> BatchAssignProductsAsync(BatchAssignProductsRequestDto dto);
        Task<int> BatchUnassignProductsAsync(BatchUnassignProductsRequestDto dto);
    }
}