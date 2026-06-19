using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocalSupplierInvoiceImportService
    {
        Task<ApiResponse<LocalSupplierInvoiceImportPreviewDto>> PreviewAsync(
            IFormFile file,
            CancellationToken cancellationToken = default
        );

        Task<ApiResponse<LocalSupplierInvoiceImportConfirmResultDto>> ConfirmAsync(
            LocalSupplierInvoiceImportConfirmRequest request,
            CancellationToken cancellationToken = default
        );
    }
}
