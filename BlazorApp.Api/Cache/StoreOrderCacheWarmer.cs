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
                // 首页默认查询参数：pageNumber=1, pageSize=50, 无分类和搜索条件
                var filter = new StoreOrderFilterDto
                {
                    PageNumber = 1,
                    PageSize = 50,
                    ItemNumber = null,
                    ProductName = null,
                    CategoryGUID = null,
                    SortBy = "Default"
                };

                // 调用服务获取数据，这会触发缓存逻辑
                var result = await _service.GetPagedListAsync(filter);

                // 手动设置缓存，确保缓存已写入
                var cacheKey = StoreOrderCacheKeys.Products(filter);
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(CACHE_DURATION)
                    .SetPriority(Microsoft.Extensions.Caching.Memory.CacheItemPriority.High);

                _cache.Set(cacheKey, result, cacheOptions);

                _logger.LogInformation(
                    "首页商品列表缓存预热完成，共 {Count} 条商品，缓存键: {CacheKey}",
                    result.Items?.Count ?? 0,
                    cacheKey
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "预热首页商品列表缓存失败");
                throw;
            }
        }

        /// <summary>
        /// 清除所有商品列表缓存（排除首页缓存）
        /// </summary>
        public Task ClearCacheAsync()
        {
            // 先获取首页缓存键，确保它在活动键列表中
            var homePageCacheKey = StoreOrderCacheKeys.GetHomePageCacheKey();
            var keysToClear = StoreOrderCacheKeys.ActiveKeys.ToList();

            // 排除首页缓存，只清除其他缓存
            var keysToRemove = keysToClear.Where(k => k != homePageCacheKey).ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }

            // 清除活动键列表，但保留首页缓存键
            StoreOrderCacheKeys.ClearActiveKeys();
            // 重新添加首页缓存键到活动键列表
            // GetHomePageCacheKey 会重新生成相同的键（因为参数相同），并自动添加到 _activeKeys
            _ = StoreOrderCacheKeys.GetHomePageCacheKey();

            _logger.LogInformation(
                "已清除 {Count} 个商品列表缓存（首页缓存已保留）",
                keysToRemove.Count
            );

            return Task.CompletedTask;
        }
    }
}

