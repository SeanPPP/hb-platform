using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// HQ/本地分店价格与多码双向同步后台任务服务。
    /// </summary>
    public interface IStorePriceTransferJobService
    {
        Task<StorePriceTransferJobDto> StartJobAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            CancellationToken cancellationToken = default
        );

        Task<StorePriceTransferJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
