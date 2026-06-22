using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IProductMovementReportService
    {
        Task<ProductMovementReportResponseDto> GetReportAsync(
            ProductMovementReportQueryDto query,
            IReadOnlyList<string>? scopedStoreCodes
        );

        Task<List<ProductMovementReportStoreOptionDto>> GetStoreOptionsAsync(
            IReadOnlyList<string>? scopedStoreCodes
        );
    }
}
