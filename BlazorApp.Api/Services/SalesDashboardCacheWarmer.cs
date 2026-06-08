using BlazorApp.Api.Cache;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 销售仪表板缓存预热服务
    /// 负责预热和清除销售仪表板缓存
    /// </summary>
    public class SalesDashboardCacheWarmer : ISalesDashboardCacheWarmer
    {
        private readonly ISalesDashboardReactService _service;
        private readonly ILogger<SalesDashboardCacheWarmer> _logger;
        private readonly IMemoryCache _cache;

        public SalesDashboardCacheWarmer(
            ISalesDashboardReactService service,
            ILogger<SalesDashboardCacheWarmer> logger,
            IMemoryCache cache
        )
        {
            _service = service;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// 预热所有销售仪表板缓存
        /// </summary>
        public async Task WarmUpAsync(DateRangeDto dateRange)
        {
            _logger.LogInformation(
                "开始预热销售仪表板缓存: {Start} - {End}",
                dateRange.StartDate,
                dateRange.EndDate
            );

            var tasks = new List<Task>
            {
                WarmUpSummaryAsync(dateRange),
                _service.GetHourlySalesAsync(dateRange),
                _service.GetStoreSalesRankAsync(dateRange),
                _service.GetSupplierSalesRankAsync(dateRange),
                _service.GetChinaSupplierSalesRankAsync(dateRange),
                _service.GetBestSellersAsync(dateRange, null, 1, 50),
            };

            await Task.WhenAll(tasks);

            _logger.LogInformation("销售仪表板缓存预热完成");
        }

        /// <summary>
        /// 预热仪表板汇总数据缓存
        /// </summary>
        public async Task WarmUpSummaryAsync(DateRangeDto dateRange)
        {
            await _service.GetDashboardSummaryAsync(dateRange);
            _logger.LogInformation("汇总数据缓存预热完成");
        }

        /// <summary>
        /// 清除所有销售仪表板缓存
        /// </summary>
        public Task ClearCacheAsync()
        {
            var keysToClear = SalesDashboardCacheKeys.ActiveKeys.ToList();

            foreach (var key in keysToClear)
            {
                _cache.Remove(key);
            }

            SalesDashboardCacheKeys.ClearActiveKeys();
            _logger.LogInformation("已清除 {Count} 个销售仪表板缓存", keysToClear.Count);

            return Task.CompletedTask;
        }
    }
}
