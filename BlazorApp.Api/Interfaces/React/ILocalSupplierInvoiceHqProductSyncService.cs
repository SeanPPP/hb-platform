using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocalSupplierInvoiceHqProductSyncService
    {
        Task<ApiResponse<EnsureHqProductsResult>> EnsureHqProductsAsync(
            string invoiceGuid,
            EnsureHqProductsRequest request,
            string updatedBy
        );
    }
}
