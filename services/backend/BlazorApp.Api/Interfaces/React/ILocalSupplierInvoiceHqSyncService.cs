using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocalSupplierInvoiceHqSyncService
    {
        Task<ApiResponse<LocalSupplierInvoiceHqSyncResult>> SyncForPageAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        );
    }
}
