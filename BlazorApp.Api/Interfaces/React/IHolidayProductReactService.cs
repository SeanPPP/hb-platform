using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IHolidayProductReactService
    {
        Task<ApiResponse<object>> ImportHolidayProductsFromExcelAsync(HolidayProductImportRequestDto request);
        Task<ApiResponse<HolidayProductAnalysisResponseDto>> GetHolidayProductsAnalysisAsync(HolidayProductAnalysisRequestDto request);
        Task<ApiResponse<WeeklySalesChartDto>> GetProductWeeklySalesAsync(string productCode, DateTime startDate, DateTime endDate, List<string>? storeCodes = null);
        Task<ApiResponse<HolidayProductListResponseDto>> GetHolidayProductsListAsync(HolidayProductListRequestDto request);
        Task<ApiResponse<PurchaseDataResponseDto>> GetPurchaseDataByProductCodesAsync(PurchaseDataRequestDto request);
        Task<ApiResponse<SalesDataResponseDto>> GetSalesDataByProductCodesAsync(SalesDataRequestDto request);
        Task<ApiResponse<HolidayProductAnalysisSimpleResponseDto>> GetHolidayProductsSimpleAsync(HolidayProductAnalysisRequestDto request);
        Task<ApiResponse<ProductBranchDetailsResponseDto>> GetProductBranchDetailsAsync(ProductBranchDetailsRequestDto request);
    }
}
