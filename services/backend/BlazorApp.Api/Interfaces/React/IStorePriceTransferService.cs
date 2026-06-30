using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// HQ/本地分店价格与多码双向同步服务。
    /// </summary>
    public interface IStorePriceTransferService
    {
        Task<ApiResponse<StorePriceTransferResult>> TransferAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            Action<StorePriceTransferResult>? progressCallback = null
        );
    }
}
