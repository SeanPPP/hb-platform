using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.BatchUpdateOrderStatus;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.CompleteOrder;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.StartPicking;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.UpdateOrderStatus;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle;

internal static class StoreOrderLifecycleLegacyFactory
{
    internal static IStoreOrderLifecycleSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var persistence = new SqlSugarStoreOrderLifecyclePersistence(context);
        var executionContext = new StoreOrderLifecycleExecutionContext(actorContext);

        return new StoreOrderLifecycleSlice(
            new CompleteOrderHandler(
                persistence,
                persistence,
                executionContext,
                new CompleteOrderValidator(),
                NullLogger<CompleteOrderHandler>.Instance
            ),
            new StartPickingHandler(
                persistence,
                persistence,
                executionContext,
                new StartPickingValidator(),
                NullLogger<StartPickingHandler>.Instance
            ),
            new UpdateOrderStatusHandler(
                persistence,
                persistence,
                executionContext,
                new UpdateOrderStatusValidator(),
                NullLogger<UpdateOrderStatusHandler>.Instance
            ),
            new BatchUpdateOrderStatusHandler(
                persistence,
                persistence,
                executionContext,
                new BatchUpdateOrderStatusValidator(),
                NullLogger<BatchUpdateOrderStatusHandler>.Instance
            )
        );
    }
}
