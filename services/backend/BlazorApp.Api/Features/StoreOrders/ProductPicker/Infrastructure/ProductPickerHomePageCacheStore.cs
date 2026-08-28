using BlazorApp.Api.Cache;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;

internal sealed class ProductPickerHomePageCacheStore(IMemoryCache cache)
{
    internal string SetLightweightWarmUp(
        int pageSize,
        PagedListReactDto<StoreOrderProductDto> result
    )
    {
        var cacheKey = StoreOrderCacheKeys.GetHomePageWarmUpCacheKey(pageSize);
        cache.Set(cacheKey, result, CreateOptions());
        return cacheKey;
    }

    internal string SetAccurateHomePage(
        int pageSize,
        PagedListReactDto<StoreOrderProductDto> result
    )
    {
        // 后台预热没有用户 Claims，固定写普通范围，不能污染可解析货位的缓存分区。
        var cacheKey = StoreOrderCacheKeys.Products(
            ProductPickerRules.CreateDefaultHomePageFilter(pageSize),
            locationLookupEnabled: false
        );
        cache.Set(cacheKey, result, CreateOptions());
        return cacheKey;
    }

    private static MemoryCacheEntryOptions CreateOptions()
    {
        return new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(ProductPickerRules.HomePageCacheDuration)
            .SetPriority(CacheItemPriority.High);
    }
}
