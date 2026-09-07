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

        Task<ApiResponse<UpdateHqProductsResult>> UpdateHqProductsAsync(
            string invoiceGuid,
            UpdateHqProductsRequest? request,
            string updatedBy
        );

        Task<ApiResponse<UpdateHqProductsResult>> UpdateHqProductsAsync(
            string invoiceGuid,
            UpdateHqProductsRequest? request,
            string? actorUserGuid,
            string actorName
        );

        /// <summary>
        /// 按指定的HQ成本锁等待时长更新商品。默认实现保留旧实现兼容性，实际服务可覆写为带锁等待的实现。
        /// </summary>
        Task<ApiResponse<UpdateHqProductsResult>> UpdateHqProductsAsync(
            string invoiceGuid,
            UpdateHqProductsRequest? request,
            string? actorUserGuid,
            string actorName,
            int lockWaitMilliseconds
        ) => string.IsNullOrWhiteSpace(actorUserGuid)
            ? UpdateHqProductsAsync(invoiceGuid, request, actorName)
            : UpdateHqProductsAsync(invoiceGuid, request, actorUserGuid, actorName);
    }
}
