using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Api.Interfaces.React;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement;

internal static class StoreOrderOrderManagementLegacyFactory
{
    internal static IStoreOrderOrderManagementSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IWarehouseProductChangeHistoryService changeHistoryService
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var productCostCoordinator = new StoreOrderProductCostCoordinator(context);
        var transactionExecutor = new StoreOrderTransactionExecutor(context);
        var persistence = new StoreOrderManagementPersistence(
            context,
            actorContext,
            productCostCoordinator
        );
        var lineStore = new SqlSugarStoreOrderLineCommandStore(
            context,
            transactionExecutor,
            persistence,
            actorContext,
            productCostCoordinator,
            changeHistoryService
        );
        var headerStore = new SqlSugarStoreOrderHeaderCommandStore(
            context,
            transactionExecutor,
            persistence,
            actorContext
        );
        var productStatusStore = new SqlSugarStoreOrderProductStatusCommandStore(
            context,
            transactionExecutor,
            actorContext,
            changeHistoryService
        );

        return new StoreOrderOrderManagementSlice(
            new AddOrderLineHandler(
                new AddOrderLineValidator(),
                lineStore,
                NullLogger<AddOrderLineHandler>.Instance
            ),
            new BatchAddOrderLineHandler(
                new BatchAddOrderLineValidator(),
                lineStore,
                NullLogger<BatchAddOrderLineHandler>.Instance
            ),
            new UpdateOrderLineHandler(
                new UpdateOrderLineValidator(),
                lineStore,
                productCostCoordinator,
                NullLogger<UpdateOrderLineHandler>.Instance
            ),
            new RemoveOrderLineHandler(
                new RemoveOrderLineValidator(),
                lineStore,
                NullLogger<RemoveOrderLineHandler>.Instance
            ),
            new BatchUpdateOrderLineHandler(
                new BatchUpdateOrderLineValidator(),
                lineStore,
                productCostCoordinator,
                NullLogger<BatchUpdateOrderLineHandler>.Instance
            ),
            new RefreshOrderLineImportPricesHandler(
                new RefreshOrderLineImportPricesValidator(),
                lineStore,
                NullLogger<RefreshOrderLineImportPricesHandler>.Instance
            ),
            new UpdateOrderHeaderHandler(
                new UpdateOrderHeaderValidator(),
                headerStore,
                NullLogger<UpdateOrderHeaderHandler>.Instance
            ),
            new UpdateOrderOutboundDateHandler(
                new UpdateOrderOutboundDateValidator(),
                headerStore,
                NullLogger<UpdateOrderOutboundDateHandler>.Instance
            ),
            new DeleteOrderHandler(
                new DeleteOrderValidator(),
                headerStore,
                NullLogger<DeleteOrderHandler>.Instance
            ),
            new UpdateProductStatusHandler(
                new UpdateProductStatusValidator(),
                productStatusStore,
                NullLogger<UpdateProductStatusHandler>.Instance
            ),
            new BatchUpdateProductStatusHandler(
                new BatchUpdateProductStatusValidator(),
                productStatusStore,
                NullLogger<BatchUpdateProductStatusHandler>.Instance
            )
        );
    }
}
