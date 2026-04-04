using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IProductCategoryReactService
    {
        Task<List<ProductCategoryDto>> GetTreeAsync();

        Task<PagedResultDto<ProductCategoryDto>> GetListAsync(
            ProductCategoryFilterDto filter
        );

        Task<ProductCategoryDto> CreateAsync(CreateProductCategoryDto dto);

        Task<ProductCategoryDto> UpdateAsync(UpdateProductCategoryDto dto);

        Task<bool> DeleteAsync(string categoryGuid);

        Task<bool> BatchMoveAsync(BatchMoveCategoriesDto dto);

        Task<int> BatchToggleActiveAsync(BatchToggleActiveDto dto);

        Task<bool> BatchSortAsync(BatchSortRequestDto dto);
    }
}
