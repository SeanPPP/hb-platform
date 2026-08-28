using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.CopyOrder;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.CreateOrder;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.SubmitOrder;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Infrastructure;
using BlazorApp.Api.Interfaces.React;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement;

internal static class StoreOrderPlacementLegacyFactory
{
    internal static IStoreOrderPlacementSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IOrderNumberGenerator orderNumberGenerator,
        IStoreOrderCartOwnerScope ownerScope,
        IStoreOrderCartCommandCoordinator cartCoordinator,
        IStoreOrderCartPlacementPort cartPort
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var gateCoordinator = new StoreOrderPlacementGateCoordinator(
            context,
            NullLogger<StoreOrderPlacementGateCoordinator>.Instance
        );
        var executionContext = new StoreOrderPlacementExecutionContext(actorContext);
        var orderStore = new SqlSugarStoreOrderPlacementStore(context);

        return new StoreOrderPlacementSlice(
            new SubmitOrderHandler(
                new SubmitOrderValidator(),
                ownerScope,
                cartCoordinator,
                cartPort,
                gateCoordinator,
                executionContext,
                orderNumberGenerator,
                NullLogger<SubmitOrderHandler>.Instance
            ),
            new CreateOrderHandler(
                new CreateOrderValidator(),
                ownerScope,
                gateCoordinator,
                orderStore,
                executionContext,
                orderNumberGenerator,
                NullLogger<CreateOrderHandler>.Instance
            ),
            new CopyOrderHandler(
                new CopyOrderValidator(),
                ownerScope,
                gateCoordinator,
                orderStore,
                executionContext,
                orderNumberGenerator,
                NullLogger<CopyOrderHandler>.Instance
            )
        );
    }
}
