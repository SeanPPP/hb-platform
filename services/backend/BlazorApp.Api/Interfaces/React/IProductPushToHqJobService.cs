using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 商品推送 HQ 后台任务服务。
    /// </summary>
    public interface IProductPushToHqJobService
    {
        Task<PushProductsToHqJobDto> StartJobAsync(
            PushProductsToHqRequest request,
            CancellationToken cancellationToken = default
        );

        Task<PushProductsToHqJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        );
    }
}
