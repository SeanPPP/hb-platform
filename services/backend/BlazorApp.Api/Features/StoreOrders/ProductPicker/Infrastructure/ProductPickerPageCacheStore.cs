using BlazorApp.Api.Cache;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;

internal sealed class ProductPickerPageCacheStore(
    IMemoryCache cache,
    IProductPickerLocationLookup locationLookup,
    ILogger<ProductPickerPageCacheStore> logger
)
{
    internal bool TryGet(
        StoreOrderFilterDto filter,
        out PagedListReactDto<StoreOrderProductDto>? result
    )
    {
        result = null;
        if (!ShouldCache(filter))
        {
            return false;
        }

        var cacheKey = CreateCacheKey(filter);
        if (!cache.TryGetValue(cacheKey, out result) || result is null)
        {
            logger.LogDebug("缓存未命中，从服务获取商品列表: {CacheKey}", cacheKey);
            return false;
        }

        logger.LogDebug("从缓存获取商品列表: {CacheKey}", cacheKey);
        return true;
    }

    internal void Set(
        StoreOrderFilterDto filter,
        PagedListReactDto<StoreOrderProductDto> result
    )
    {
        if (!ShouldCache(filter))
        {
            return;
        }

        var cacheKey = CreateCacheKey(filter);
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(ProductPickerRules.HomePageCacheDuration)
            .SetPriority(CacheItemPriority.Normal);
        cache.Set(cacheKey, result, options);
        logger.LogDebug(
            "商品列表已缓存: {CacheKey}, 过期时间: {Expiration}",
            cacheKey,
            DateTime.Now.Add(ProductPickerRules.HomePageCacheDuration)
        );
    }

    private string CreateCacheKey(StoreOrderFilterDto filter)
    {
        return StoreOrderCacheKeys.Products(filter, locationLookup.IsEnabled);
    }

    private static bool ShouldCache(StoreOrderFilterDto filter)
    {
        return !filter.ExcludeExistingWarehouseProducts
            && string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID)
            && string.IsNullOrWhiteSpace(filter.SupplierCode);
    }
}
