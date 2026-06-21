using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocalSupplierInvoiceOcrService
    {
        Task<ApiResponse<string>> ExtractTextAsync(
            byte[] fileBytes,
            string fileName,
            CancellationToken cancellationToken = default
        );
    }
}
