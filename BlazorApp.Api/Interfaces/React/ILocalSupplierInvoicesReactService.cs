using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocalSupplierInvoicesReactService
    {
        Task<GridResponseDto<LocalSupplierInvoiceListDto>> GetGridDataAsync(GridRequestDto request);
        Task<ApiResponse<LocalSupplierInvoiceDetailDto>> GetInvoiceAsync(string invoiceGuid);
        Task<ApiResponse<List<LocalSupplierInvoiceItemDto>>> GetDetailsAsync(string invoiceGuid);
        Task<ApiResponse<string>> CreateAsync(CreateInvoiceRequest dto);
        Task<ApiResponse<bool>> DeleteAsync(string invoiceGuid, string updatedBy);
        Task<ApiResponse<List<SupplierItemDetectResult>>> DetectSupplierItemAsync(
            DetectSupplierItemRequest dto
        );
        Task<ApiResponse<List<BarcodeDetectResult>>> DetectBarcodeAsync(DetectBarcodeRequest dto);
        Task<ApiResponse<bool>> UpdateAsync(string invoiceGuid, UpdateInvoiceRequest dto);
        Task<ApiResponse<BatchResultDto>> BatchUpsertDetailsAsync(
            string invoiceGuid,
            List<InvoiceDetailUpsertItemDto> items,
            string updatedBy
        );
    }
}
