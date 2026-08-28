using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;
using BlazorApp.Api.Interfaces.React;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker;

internal static class StoreOrderProductPickerLegacyFactory
{
    internal static IStoreOrderProductPickerSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IStoreOrderLocationProductLookupService locationProductLookupService,
        IMemoryCache memoryCache
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var productEnricher = new ProductPickerProductEnricher(context);
        var locationLookup = new ProductPickerLocationLookup(
            actorContext,
            locationProductLookupService
        );
        var pageQueryStore = new ProductPickerPageQueryStore(
            context,
            productEnricher,
            locationLookup,
            NullLogger<ProductPickerPageQueryStore>.Instance
        );
        var batchQueryStore = new ProductPickerBatchQueryStore(context);
        var scanQueryStore = new ProductPickerScanQueryStore(
            context,
            productEnricher,
            locationLookup
        );
        var pageCacheStore = new ProductPickerPageCacheStore(
            memoryCache,
            locationLookup,
            NullLogger<ProductPickerPageCacheStore>.Instance
        );
        var cacheStore = new ProductPickerHomePageCacheStore(memoryCache);
        var warmUpPageHandler = new GetHomePageWarmUpPageHandler(
            new GetHomePageWarmUpPageValidator(),
            pageQueryStore
        );
        var cachePageHandler = new GetHomePageCachePageHandler(
            new GetHomePageCachePageValidator(),
            pageQueryStore
        );

        return new StoreOrderProductPickerSlice(
            new GetProductPickerPageHandler(
                new GetProductPickerPageValidator(),
                pageQueryStore,
                pageCacheStore
            ),
            new BatchLookupProductsHandler(
                new BatchLookupProductsValidator(),
                batchQueryStore,
                NullLogger<BatchLookupProductsHandler>.Instance
            ),
            new ScanLookupProductsHandler(
                new ScanLookupProductsValidator(),
                scanQueryStore,
                httpContextAccessor,
                NullLogger<ScanLookupProductsHandler>.Instance
            ),
            warmUpPageHandler,
            cachePageHandler,
            new WarmUpProductPickerHomePageHandler(
                new WarmUpProductPickerHomePageValidator(),
                warmUpPageHandler,
                cachePageHandler,
                cacheStore,
                NullLogger<WarmUpProductPickerHomePageHandler>.Instance
            )
        );
    }
}
