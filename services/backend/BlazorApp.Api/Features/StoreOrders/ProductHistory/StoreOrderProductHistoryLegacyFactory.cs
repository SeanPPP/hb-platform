using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory;

internal static class StoreOrderProductHistoryLegacyFactory
{
    internal static IStoreOrderProductHistorySlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider? timeProvider = null
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var salesHistoryQueryStore = new ProductSalesHistoryQueryStore(
            context,
            NullLogger<ProductSalesHistoryQueryStore>.Instance,
            timeProvider
        );
        var dynamicDataQueryStore = new ProductsDynamicDataQueryStore(
            context,
            actorContext,
            salesHistoryQueryStore,
            NullLogger<ProductsDynamicDataQueryStore>.Instance
        );
        var orderHistoryQueryStore = new ProductOrderHistoryQueryStore(
            context,
            salesHistoryQueryStore
        );
        var queryStore = new ProductHistoryQueryStore(
            dynamicDataQueryStore,
            orderHistoryQueryStore,
            salesHistoryQueryStore
        );

        return new StoreOrderProductHistorySlice(
            new GetProductsDynamicDataHandler(
                new GetProductsDynamicDataValidator(),
                queryStore,
                NullLogger<GetProductsDynamicDataHandler>.Instance
            ),
            new GetProductOrderHistoryHandler(
                new GetProductOrderHistoryValidator(),
                queryStore,
                NullLogger<GetProductOrderHistoryHandler>.Instance
            ),
            new GetProductActivityHistoryHandler(
                new GetProductActivityHistoryValidator(),
                queryStore,
                NullLogger<GetProductActivityHistoryHandler>.Instance
            ),
            new GetSalesSinceLastArrivalHandler(
                new GetSalesSinceLastArrivalValidator(),
                queryStore,
                NullLogger<GetSalesSinceLastArrivalHandler>.Instance
            ),
            new GetSalesSinceLastArrivalSummaryHandler(
                new GetSalesSinceLastArrivalSummaryValidator(),
                queryStore,
                NullLogger<GetSalesSinceLastArrivalSummaryHandler>.Instance
            )
        );
    }
}
