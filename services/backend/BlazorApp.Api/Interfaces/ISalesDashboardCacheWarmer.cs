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
        /// 清除所有销售仪表板缓存
        /// </summary>
        Task ClearCacheAsync();
    }
}
