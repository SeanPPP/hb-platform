using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocalSupplierInvoiceSalesAnalysisService
    {
        Task<ApiResponse<LocalSupplierInvoiceSalesAnalysisResponseDto>> GetAnalysisAsync(
            string invoiceGuid
        );
    }
}
