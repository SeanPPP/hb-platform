using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 仓库商品 HQ 同步后台任务服务。
    /// </summary>
    public interface IWarehouseProductHqSyncJobService
    {
        Task<WarehouseProductHqSyncJobDto> StartJobAsync(
            WarehouseProductHqSyncJobRequestDto request,
            CancellationToken cancellationToken = default
        );

        Task<WarehouseProductHqSyncJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
