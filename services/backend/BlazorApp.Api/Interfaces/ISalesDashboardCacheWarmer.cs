using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 销售仪表板缓存预热服务接口
    /// </summary>
    public interface ISalesDashboardCacheWarmer
    {
        /// <summary>
        /// 预热所有销售仪表板缓存
        /// </summary>
        Task WarmUpAsync(DateRangeDto dateRange);

        /// <summary>
        /// 预热仪表板汇总数据缓存
        /// </summary>
        Task WarmUpSummaryAsync(DateRangeDto dateRange);

        /// <summary>
        /// 预热移动端商品报告「中国供应商」页签的默认视图（今天、昨天 × 全部启用分店）：
        /// 供应商排行、分店中国货合计、商品明细第一页（数量降序）。统计未完整时跳过，失败只记日志。
        /// </summary>
        Task WarmUpMobileChinaTabAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 清除统计刷新后可能过期的销售仪表板缓存；按统计批次版本做键的完整报表条目保留。
        /// </summary>
        Task ClearCacheAsync();

        /// <summary>
        /// 手动全量清除，包括按统计批次版本做键的完整报表条目。
        /// </summary>
        Task ClearAllCacheAsync();
    }
}
