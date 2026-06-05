using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 商品同步到分店后台任务服务。
    /// </summary>
    public interface IProductStoreSyncJobService
    {
        Task<SyncProductsToStoresJobDto> StartJobAsync(
            SyncProductsToStoresRequest request,
            CancellationToken cancellationToken = default
        );

        Task<SyncProductsToStoresJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
