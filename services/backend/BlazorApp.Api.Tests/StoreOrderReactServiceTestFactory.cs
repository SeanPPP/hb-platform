using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Cart;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance;
using BlazorApp.Api.Features.StoreOrders.Lifecycle;
using BlazorApp.Api.Features.StoreOrders.OrderManagement;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement;
using BlazorApp.Api.Features.StoreOrders.Orders;
using BlazorApp.Api.Features.StoreOrders.PasteReplace;
using BlazorApp.Api.Features.StoreOrders.ProductHistory;
using BlazorApp.Api.Features.StoreOrders.ProductPicker;
using BlazorApp.Api.Features.StoreOrders.Sync;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 旧服务级测试专用组合根。生产 façade 只接收窄切片并负责委派。
/// </summary>
internal static class StoreOrderReactServiceTestFactory
{
    internal static StoreOrderReactService Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IOrderNumberGenerator orderNumberGenerator,
        IConfiguration configuration,
        IMapper mapper,
        IStoreOrderLocationProductLookupService locationProductLookupService,
        IWarehouseProductChangeHistoryService changeHistoryService,
        TimeProvider? timeProvider = null,
        IStoreOrderImportPriceVarianceSlice? importPriceVarianceSlice = null,
        IMemoryCache? memoryCache = null,
        Func<ISqlSugarClient>? createHqConnection = null
    )
    {
        var hqConnectionFactory = createHqConnection
            ?? (() => HqSqlSugarContext.CreateConcurrentConnection(configuration));
        var productPickerSlice = StoreOrderProductPickerLegacyFactory.Create(
            context,
            httpContextAccessor,
            locationProductLookupService,
            memoryCache ?? new MemoryCache(new MemoryCacheOptions())
        );
        var cartComposition = StoreOrderCartLegacyFactory.CreateComposition(
            context,
            httpContextAccessor,
            productPickerSlice
        );
        var orderPlacementSlice = StoreOrderPlacementLegacyFactory.Create(
            context,
            httpContextAccessor,
            orderNumberGenerator,
            cartComposition.OwnerScope,
            cartComposition.CommandCoordinator,
            cartComposition.PlacementPort
        );

        return new StoreOrderReactService(
            productPickerSlice,
            cartComposition.Slice,
            orderPlacementSlice,
            StoreOrderProductHistoryLegacyFactory.Create(
                context,
                httpContextAccessor,
                timeProvider
            ),
            StoreOrderOrdersLegacyFactory.Create(
                context,
                httpContextAccessor,
                hqConnectionFactory
            ),
            importPriceVarianceSlice
                ?? StoreOrderImportPriceVarianceLegacyFactory.Create(
                    context,
                    httpContextAccessor,
                    changeHistoryService
                ),
            StoreOrderOrderManagementLegacyFactory.Create(
                context,
                httpContextAccessor,
                changeHistoryService
            ),
            StoreOrderPasteReplaceLegacyFactory.Create(context, httpContextAccessor),
            StoreOrderSyncLegacyFactory.Create(context, mapper, hqConnectionFactory),
            StoreOrderLifecycleLegacyFactory.Create(context, httpContextAccessor)
        );
    }
}
