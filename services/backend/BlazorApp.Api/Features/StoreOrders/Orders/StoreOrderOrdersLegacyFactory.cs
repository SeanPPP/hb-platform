using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Orders.Application;
using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Orders;

internal static class StoreOrderOrdersLegacyFactory
{
    internal static IStoreOrderOrdersSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration
    )
    {
        return Create(
            context,
            httpContextAccessor,
            () => HqSqlSugarContext.CreateConcurrentConnection(configuration)
        );
    }

    internal static IStoreOrderOrdersSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        Func<ISqlSugarClient> createHqConnection
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var accessScope = new StoreOrderAccessScope(context, actorContext);
        var storeIdentityReader = new StoreOrderStoreIdentityReader(context);
        var listQueryStore = new StoreOrderListQueryStore(
            context,
            accessScope,
            actorContext
        );
        var detailQueryStore = new StoreOrderDetailQueryStore(context, accessScope);
        var lookupQueryStore = new StoreOrderLookupQueryStore(
            context,
            storeIdentityReader,
            new DelegateHqConnectionFactory(createHqConnection),
            NullLogger<StoreOrderLookupQueryStore>.Instance
        );
        var commandStore = new StoreOrderCommandStore(
            context,
            actorContext,
            storeIdentityReader
        );

        return new StoreOrderOrdersSlice(
            new GetOrderListHandler(
                new GetOrderListValidator(),
                listQueryStore,
                NullLogger<GetOrderListHandler>.Instance
            ),
            new GetOrderDetailHandler(
                new GetOrderDetailValidator(),
                detailQueryStore
            ),
            new GetOrderDetailFullHandler(
                new GetOrderDetailFullValidator(),
                detailQueryStore
            ),
            new GetOrderDetailProductCodesHandler(
                new GetOrderDetailProductCodesValidator(),
                detailQueryStore
            ),
            new UpdateStoreContactHandler(
                new UpdateStoreContactValidator(),
                commandStore
            ),
            new GetUsedBranchesHandler(
                new GetUsedBranchesValidator(),
                lookupQueryStore,
                NullLogger<GetUsedBranchesHandler>.Instance
            ),
            new GetUnmatchedStoreOrderGroupsHandler(
                new GetUnmatchedStoreOrderGroupsValidator(),
                lookupQueryStore,
                NullLogger<GetUnmatchedStoreOrderGroupsHandler>.Instance
            ),
            new BatchMapStoreOrderStoreCodeHandler(
                new BatchMapStoreOrderStoreCodeValidator(),
                commandStore,
                NullLogger<BatchMapStoreOrderStoreCodeHandler>.Instance
            )
        );
    }

    private sealed class DelegateHqConnectionFactory(
        Func<ISqlSugarClient> createHqConnection
    ) : IStoreOrderOrdersHqConnectionFactory
    {
        public ISqlSugarClient Create() => createHqConnection();
    }
}
