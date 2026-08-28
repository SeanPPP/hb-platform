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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders;

internal sealed record StoreOrderLegacyFacadeSlices(
    IStoreOrderProductPickerSlice ProductPicker,
    IStoreOrderCartSlice Cart,
    IStoreOrderPlacementSlice OrderPlacement,
    IStoreOrderProductHistorySlice ProductHistory,
    IStoreOrderOrdersSlice Orders,
    IStoreOrderImportPriceVarianceSlice ImportPriceVariance,
    IStoreOrderOrderManagementSlice OrderManagement,
    IStoreOrderPasteReplaceExecutor PasteReplace,
    IStoreOrderMissingOrdersSyncExecutor Sync,
    IStoreOrderLifecycleSlice Lifecycle
);

/// <summary>
/// 旧公开构造函数的唯一组合根。这里只连接窄切片，不承载 SQL、事务、锁或业务判断。
/// </summary>
internal static class StoreOrderLegacyFacadeComposition
{
    internal static StoreOrderLegacyFacadeSlices Create(
        SqlSugarContext context,
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IOrderNumberGenerator orderNumberGenerator,
        IConfiguration configuration,
        IMapper mapper,
        IInvoiceEmailService invoiceEmailService,
        IStoreOrderLocationProductLookupService locationProductLookupService,
        IWarehouseProductChangeHistoryService changeHistoryService,
        TimeProvider? timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(invoiceEmailService);

        var memoryCache = httpContextAccessor.HttpContext?.RequestServices
            .GetService<IMemoryCache>()
            ?? new MemoryCache(new MemoryCacheOptions());
        Func<ISqlSugarClient> createHqConnection = () =>
            HqSqlSugarContext.CreateConcurrentConnection(configuration);
        var productPicker = StoreOrderProductPickerLegacyFactory.Create(
            context,
            httpContextAccessor,
            locationProductLookupService,
            memoryCache
        );
        var cart = StoreOrderCartLegacyFactory.CreateComposition(
            context,
            httpContextAccessor,
            productPicker
        );

        return new StoreOrderLegacyFacadeSlices(
            productPicker,
            cart.Slice,
            StoreOrderPlacementLegacyFactory.Create(
                context,
                httpContextAccessor,
                orderNumberGenerator,
                cart.OwnerScope,
                cart.CommandCoordinator,
                cart.PlacementPort
            ),
            StoreOrderProductHistoryLegacyFactory.Create(
                context,
                httpContextAccessor,
                timeProvider
            ),
            StoreOrderOrdersLegacyFactory.Create(
                context,
                httpContextAccessor,
                createHqConnection
            ),
            StoreOrderImportPriceVarianceLegacyFactory.Create(
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
            StoreOrderSyncLegacyFactory.Create(context, mapper, createHqConnection),
            StoreOrderLifecycleLegacyFactory.Create(context, httpContextAccessor)
        );
    }
}
