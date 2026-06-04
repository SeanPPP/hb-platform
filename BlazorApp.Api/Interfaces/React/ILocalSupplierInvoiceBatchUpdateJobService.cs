using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 本地进货单批量更新后台任务服务。
    /// </summary>
    public interface ILocalSupplierInvoiceBatchUpdateJobService
    {
        Task<LocalSupplierInvoiceUpdateToStorePricesJobDto> StartUpdateToStorePricesJobAsync(
            UpdateToStorePricesRequest request,
            string updatedBy,
            CancellationToken cancellationToken = default
        );

        Task<LocalSupplierInvoiceUpdateToStorePricesJobDto?> GetUpdateToStorePricesJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );

        Task<LocalSupplierInvoiceUpdateHqProductsJobDto> StartUpdateHqProductsJobAsync(
            string invoiceGuid,
            UpdateHqProductsRequest request,
            string updatedBy,
            CancellationToken cancellationToken = default
        );

        Task<LocalSupplierInvoiceUpdateHqProductsJobDto?> GetUpdateHqProductsJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
