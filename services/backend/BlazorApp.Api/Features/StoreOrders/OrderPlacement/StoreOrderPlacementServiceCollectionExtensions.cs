using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.CopyOrder;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.CreateOrder;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.SubmitOrder;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement;

internal static class StoreOrderPlacementServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderPlacementSlice(
        this IServiceCollection services
    )
    {
        services.AddStoreOrderCommon();

        services.TryAddScoped<
            IStoreOrderPlacementGateCoordinator,
            StoreOrderPlacementGateCoordinator
        >();
        services.TryAddScoped<
            IStoreOrderPlacementExecutionContext,
            StoreOrderPlacementExecutionContext
        >();
        services.TryAddScoped<
            IStoreOrderPlacementOrderStore,
            SqlSugarStoreOrderPlacementStore
        >();
        services.TryAddScoped<SubmitOrderValidator>();
        services.TryAddScoped<SubmitOrderHandler>();
        services.TryAddScoped<CreateOrderValidator>();
        services.TryAddScoped<CreateOrderHandler>();
        services.TryAddScoped<CopyOrderValidator>();
        services.TryAddScoped<CopyOrderHandler>();
        services.TryAddScoped<IStoreOrderPlacementSlice, StoreOrderPlacementSlice>();
        return services;
    }
}
