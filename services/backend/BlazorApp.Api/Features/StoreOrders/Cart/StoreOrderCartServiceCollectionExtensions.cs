using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.AddToCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.ClearCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.RemoveFromCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.ScanLookupAndAddToCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.UpdateCartItem;
using BlazorApp.Api.Features.StoreOrders.Cart.Common;
using BlazorApp.Api.Features.StoreOrders.Cart.Infrastructure;
using BlazorApp.Api.Features.StoreOrders.Cart.Queries;
using BlazorApp.Api.Features.StoreOrders.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorApp.Api.Features.StoreOrders.Cart;

internal static class StoreOrderCartServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderCartSlice(
        this IServiceCollection services
    )
    {
        services.AddStoreOrderCommon();

        services.TryAddScoped<IStoreOrderCartOwnerScope, StoreOrderCartOwnerScope>();
        services.TryAddScoped<
            IStoreOrderCartCommandCoordinator,
            StoreOrderCartCommandCoordinator
        >();
        services.TryAddScoped<SqlSugarStoreOrderCartStore>();
        services.TryAddScoped<IStoreOrderCartQueryStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlSugarStoreOrderCartStore>()
        );
        services.TryAddScoped<IStoreOrderCartCommandStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlSugarStoreOrderCartStore>()
        );
        services.TryAddScoped<IStoreOrderCartPlacementPort>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlSugarStoreOrderCartStore>()
        );
        services.TryAddScoped<GetActiveCartValidator>();
        services.TryAddScoped<GetActiveCartHandler>();
        services.TryAddScoped<GetActiveCartSummaryValidator>();
        services.TryAddScoped<GetActiveCartSummaryHandler>();
        services.TryAddScoped<AddToCartValidator>();
        services.TryAddScoped<AddToCartHandler>();
        services.TryAddScoped<UpdateCartItemValidator>();
        services.TryAddScoped<UpdateCartItemHandler>();
        services.TryAddScoped<RemoveFromCartValidator>();
        services.TryAddScoped<RemoveFromCartHandler>();
        services.TryAddScoped<ClearCartValidator>();
        services.TryAddScoped<ClearCartHandler>();
        services.TryAddScoped<ScanLookupAndAddToCartValidator>();
        services.TryAddScoped<ScanLookupAndAddToCartHandler>();
        services.TryAddScoped<IStoreOrderCartSlice, StoreOrderCartSlice>();
        return services;
    }
}
