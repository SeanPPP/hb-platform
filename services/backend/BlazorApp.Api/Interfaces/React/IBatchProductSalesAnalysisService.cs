using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IBatchProductSalesAnalysisService
{
    Task<ApiResponse<BatchProductSalesOptionsDto>> GetOptionsAsync(IReadOnlyList<string>? scopedStoreCodes, CancellationToken cancellationToken = default);
    Task<ApiResponse<BatchProductSalesQueryResultDto>> QueryAsync(BatchProductSalesQueryRequestDto request, IReadOnlyList<string>? scopedStoreCodes, CancellationToken cancellationToken = default);
    Task<ApiResponse<BatchProductSalesDetailDto>> GetDetailAsync(BatchProductSalesDetailRequestDto request, IReadOnlyList<string>? scopedStoreCodes, CancellationToken cancellationToken = default);
}
