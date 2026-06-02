using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 商品 HQ 同步服务。
    /// 全量仅处理 Product 主表；增量先处理 Product，再同步全局一品多码到 ProductSetCode。
    /// </summary>
    public interface IProductHqSyncService
    {
        Task<ApiResponse<HqProductSyncResult>> SyncFullAsync();

        Task<ApiResponse<HqProductSyncResult>> SyncIncrementalAsync(DateTime? startDate = null);

        Task<ApiResponse<PushProductsToHqResult>> PushToHqAsync(List<string> productCodes);
    }
}
