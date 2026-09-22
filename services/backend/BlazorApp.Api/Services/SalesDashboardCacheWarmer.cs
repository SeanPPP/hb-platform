using BlazorApp.Api.Cache;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 销售仪表板缓存预热服务
    /// 负责预热和清除销售仪表板缓存
    /// </summary>
    public class SalesDashboardCacheWarmer : ISalesDashboardCacheWarmer
    {
        // 与移动端一致：供应商排行取前 1000，商品明细每页 20、默认按数量降序、限定全部中国供应商。
        private const int MobileSupplierRankTopN = 1000;
        private const int MobileProductPageSize = 20;

        private readonly ISalesDashboardReactService _service;
        private readonly ILogger<SalesDashboardCacheWarmer> _logger;
        private readonly IMemoryCache _cache;
        private readonly IProductMovementReportService? _storeOptions;

        public SalesDashboardCacheWarmer(
            ISalesDashboardReactService service,
            ILogger<SalesDashboardCacheWarmer> logger,
            IMemoryCache cache
        )
            : this(service, logger, cache, null)
        {
        }

        public SalesDashboardCacheWarmer(
            ISalesDashboardReactService service,
            ILogger<SalesDashboardCacheWarmer> logger,
            IMemoryCache cache,
            IProductMovementReportService? storeOptions
        )
        {
            _service = service;
            _logger = logger;
            _cache = cache;
            _storeOptions = storeOptions;
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

        /// <inheritdoc />
        public async Task WarmUpMobileChinaTabAsync(CancellationToken cancellationToken = default)
        {
            if (_storeOptions == null)
            {
                _logger.LogDebug("移动端中国供应商页签预热跳过：未提供门店选项服务。");
                return;
            }

            // 移动端把 IsActive=1 的全部门店代码显式传给后端；预热必须用同一份列表才能命中同一个缓存键。
            var branchCodes = (await _storeOptions.GetStoreOptionsAsync(null))
                .Select(option => option.Value?.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (branchCodes.Count == 0)
            {
                return;
            }

            var today = SalesStatisticsBusinessDate.GetBusinessDate(DateTimeOffset.UtcNow);
            foreach (var date in new[] { today, today.AddDays(-1) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var range = MobileProductReportRange.Day(date);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await WarmUpMobileChinaTabRangeAsync(range, branchCodes, cancellationToken);
                // 命中缓存时整轮只有几十毫秒；超过 1 秒说明这轮真的替前台把冷数据算好了，值得留一条记录。
                if (watch.ElapsedMilliseconds >= 1000)
                    _logger.LogInformation("移动端中国供应商页签预热完成: {Range} 分店 {Branches} 家，耗时 {ElapsedMs}ms", range, branchCodes.Count, watch.ElapsedMilliseconds);
                else
                    _logger.LogDebug("移动端中国供应商页签预热命中缓存: {Range} {ElapsedMs}ms", range, watch.ElapsedMilliseconds);
            }
        }

        private async Task WarmUpMobileChinaTabRangeAsync(
            DateRangeDto dateRange,
            List<string> branchCodes,
            CancellationToken cancellationToken
        )
        {
            ProductReportStatisticStatusDto status;
            try
            {
                status = await _service.GetProductReportStatisticStatusAsync(dateRange);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "移动端中国供应商页签预热读取统计状态失败: {Range}", dateRange);
                return;
            }
            if (!string.Equals(status.StatisticStatus, SalesStatisticRefreshStatus.Fresh, StringComparison.OrdinalIgnoreCase))
            {
                // 统计补算中，服务层本来也会返回空；等下一轮再预热。
                return;
            }

            // 三块数据串行执行：预热是后台工作，不与前台请求抢并发；命中缓存时每块只花几毫秒。
            await WarmUpStepAsync(
                "中国供应商排行",
                dateRange,
                () => _service.GetChinaSupplierSalesRankAsync(dateRange, branchCodes, MobileSupplierRankTopN, null, status),
                cancellationToken
            );
            await WarmUpStepAsync(
                "分店中国货合计",
                dateRange,
                () => _service.GetChinaSupplierBranchTotalsAsync(dateRange, branchCodes, status),
                cancellationToken
            );
            await WarmUpStepAsync(
                "中国商品明细首页",
                dateRange,
                () => _service.GetEnhancedSalesProductDetailsAsync(
                    dateRange,
                    branchCodes,
                    localSupplierCodes: null,
                    chinaSupplierCodes: null,
                    pageIndex: 1,
                    pageSize: MobileProductPageSize,
                    productSearch: null,
                    status,
                    chinaSupplierScope: true,
                    sortField: "quantity",
                    sortOrder: "desc"
                ),
                cancellationToken
            );
        }

        private async Task WarmUpStepAsync(
            string step,
            DateRangeDto dateRange,
            Func<Task> action,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 预热失败只影响首次打开的速度，不能影响统计任务或宿主。
                _logger.LogWarning(ex, "移动端中国供应商页签预热失败: {Step} {Range}", step, dateRange);
            }
        }

        /// <inheritdoc />
        public Task ClearCacheAsync()
        {
            var keysToClear = SalesDashboardCacheKeys.ClearActiveKeysAndGetKeysToClear().ToList();

            foreach (var key in keysToClear)
            {
                _cache.Remove(key);
            }

            _logger.LogInformation("已清除 {Count} 个销售仪表板缓存", keysToClear.Count);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task ClearAllCacheAsync()
        {
            await ClearCacheAsync();
            var versionedKeys = SalesDashboardCacheKeys.ClearVersionedKeysAndGetKeysToClear();
            foreach (var key in versionedKeys)
            {
                _cache.Remove(key);
            }

            _logger.LogInformation("已清除 {Count} 个按统计版本缓存的完整报表条目", versionedKeys.Count);
        }
    }
}
