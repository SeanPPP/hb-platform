using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 分店订货缺失订单同步 job 服务。
    /// </summary>
    public interface IStoreOrderSyncJobService
    {
        /// <summary>
        /// 创建同步 job；若同一用户与分店集合已有运行中任务，则返回已有任务。
        /// </summary>
        Task<StoreOrderSyncJobDto> StartJobAsync(
            string userId,
            SyncMissingOrdersRequestDto? request,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// 创建全量/增量 HQ 同步 job；若同一用户、模式、范围、日期已有运行中任务，则返回已有任务。
        /// </summary>
        Task<StoreOrderSyncJobDto> StartHqSyncJobAsync(
            string userId,
            StoreOrderHqSyncMode mode,
            StoreOrderHqSyncRequestDto? request,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// 获取同步 job 状态。
        /// </summary>
        Task<StoreOrderSyncJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
