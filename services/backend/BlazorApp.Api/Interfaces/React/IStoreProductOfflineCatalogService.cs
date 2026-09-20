using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 离线目录索引构建器。
    ///
    /// 索引构建可能持续数十秒，且在客户端断开后仍需继续（结果进缓存供下次请求复用）。
    /// 因此必须在独立 DI scope 中解析本接口：绝不能捕获 HTTP 请求的 scoped DbContext，
    /// 否则请求结束、scope 释放后，后台查询会静默失败。
    /// </summary>
    public interface IOfflineCatalogIndexBuilder
    {
        Task<Services.React.OfflineCatalog.OfflineCatalogIndex?> BuildIndexAsync(
            string storeCode,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 移动端离线商品目录同步（sync-plan / page / delta page）。
    /// 返回 null 表示门店不存在或不可用；越权由控制器在调用前拦截。
    /// </summary>
    public interface IStoreProductOfflineCatalogService
    {
        Task<OfflineCatalogSyncPlanDto?> GetSyncPlanAsync(
            string storeCode,
            string? baseCatalogVersion,
            CancellationToken cancellationToken);

        Task<OfflineCatalogPageDto?> GetPageAsync(
            string storeCode,
            string? cursor,
            int pageSize,
            string? catalogVersion,
            string? downloadLeaseId,
            CancellationToken cancellationToken);

        Task<OfflineCatalogDeltaPageDto> GetDeltaPageAsync(
            string storeCode,
            string baseCatalogVersion,
            string targetCatalogVersion,
            string? cursor,
            int pageSize,
            string? downloadLeaseId,
            CancellationToken cancellationToken);
    }
}
