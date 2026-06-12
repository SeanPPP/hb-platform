using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 分店订货 HQ 全量/增量同步服务。
    /// </summary>
    public interface IStoreOrderHqSyncService
    {
        /// <summary>
        /// 执行 HQ 同步。
        /// </summary>
        Task<SyncMissingOrdersResultDto> SyncAsync(
            StoreOrderHqSyncMode mode,
            StoreOrderHqSyncRequestDto? request,
            string? jobId = null,
            CancellationToken cancellationToken = default
        );
    }
}
