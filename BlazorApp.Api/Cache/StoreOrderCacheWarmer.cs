using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Cache
{
    /// <summary>
    /// 订货商品列表缓存预热服务
    /// 负责预热和清除商品列表缓存
    /// </summary>
    public class StoreOrderCacheWarmer : IStoreOrderCacheWarmer
    {
        private readonly IStoreOrderReactService _service;
        private readonly ILogger<StoreOrderCacheWarmer> _logger;
        private readonly IMemoryCache _cache;

        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(10);

        public StoreOrderCacheWarmer(
            IStoreOrderReactService service,
            ILogger<StoreOrderCacheWarmer> logger,
            IMemoryCache cache
        )
        {
            _service = service;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// 预热首页默认数据缓存
        /// </summary>
        public async Task WarmUpHomePageAsync()
        {
            _logger.LogInformation("开始预热首页商品列表缓存");

            try
            {
                await WarmUpProductListAsync(50);
                await WarmUpProductListAsync(18);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "预热首页商品列表缓存失败");
                throw;
            }
        }

        private async Task WarmUpProductListAsync(int pageSize)
        {
            var filter = new StoreOrderFilterDto
            {
                PageNumber = 1,
                PageSize = pageSize,
                ItemNumber = null,
                ProductName = null,
                CategoryGUID = null,
                SortBy = "Default"
            };

            var result = await _service.GetPagedListAsync(filter);
            var cacheKey = StoreOrderCacheKeys.Products(filter);
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CACHE_DURATION)
                .SetPriority(Microsoft.Extensions.Caching.Memory.CacheItemPriority.High);

            _cache.Set(cacheKey, result, cacheOptions);

            _logger.LogInformation(
                "首页商品列表缓存预热完成，PageSize={PageSize}，共 {Count} 条商品，缓存键: {CacheKey}",
                pageSize,
                result.Items?.Count ?? 0,
                cacheKey
            );
        }

        /// <summary>
        /// 清除所有商品列表缓存（排除首页缓存）
        /// </summary>
        public Task ClearCacheAsync()
        {
            // 先获取首页缓存键，确保 Web 与 Expo 首页默认列表都保留。
            var homePageCacheKeys = new HashSet<string>(StringComparer.Ordinal)
            {
                StoreOrderCacheKeys.GetHomePageCacheKey(50),
                StoreOrderCacheKeys.GetHomePageCacheKey(18),
            };
            var keysToClear = StoreOrderCacheKeys.ActiveKeys.ToList();

            // 排除首页缓存，只清除其他缓存
            var keysToRemove = keysToClear.Where(k => !homePageCacheKeys.Contains(k)).ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }

            // 清除活动键列表，但保留首页缓存键
            StoreOrderCacheKeys.ClearActiveKeys();
            // 重新添加首页缓存键到活动键列表。
            foreach (var pageSize in new[] { 50, 18 })
            {
                _ = StoreOrderCacheKeys.GetHomePageCacheKey(pageSize);
            }

            _logger.LogInformation(
                "已清除 {Count} 个商品列表缓存（首页缓存已保留）",
                keysToRemove.Count
            );

            return Task.CompletedTask;
        }
    }
}
