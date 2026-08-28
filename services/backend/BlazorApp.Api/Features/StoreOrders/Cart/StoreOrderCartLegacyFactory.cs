using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.AddToCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.ClearCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.RemoveFromCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.ScanLookupAndAddToCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.UpdateCartItem;
using BlazorApp.Api.Features.StoreOrders.Cart.Common;
using BlazorApp.Api.Features.StoreOrders.Cart.Infrastructure;
using BlazorApp.Api.Features.StoreOrders.Cart.Queries;
using BlazorApp.Api.Features.StoreOrders.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.Cart;

internal sealed record StoreOrderCartLegacyComposition(
    IStoreOrderCartSlice Slice,
    IStoreOrderCartOwnerScope OwnerScope,
    IStoreOrderCartCommandCoordinator CommandCoordinator,
    IStoreOrderCartPlacementPort PlacementPort
);

internal static class StoreOrderCartLegacyFactory
{
    internal static IStoreOrderCartSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IStoreOrderCartProductLookup productLookup
    )
    {
        return CreateComposition(context, httpContextAccessor, productLookup).Slice;
    }

    internal static StoreOrderCartLegacyComposition CreateComposition(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IStoreOrderCartProductLookup productLookup
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var ownerScope = new StoreOrderCartOwnerScope(actorContext);
        var coordinator = new StoreOrderCartCommandCoordinator(context);
        var store = new SqlSugarStoreOrderCartStore(context, actorContext);

        var slice = new StoreOrderCartSlice(
            new GetActiveCartHandler(
                new GetActiveCartValidator(),
                ownerScope,
                store,
                NullLogger<GetActiveCartHandler>.Instance
            ),
            new GetActiveCartSummaryHandler(
                new GetActiveCartSummaryValidator(),
                ownerScope,
                store,
                NullLogger<GetActiveCartSummaryHandler>.Instance
            ),
            new AddToCartHandler(
                new AddToCartValidator(),
                ownerScope,
                coordinator,
                store,
                store,
                NullLogger<AddToCartHandler>.Instance
            ),
            new UpdateCartItemHandler(
                new UpdateCartItemValidator(),
                ownerScope,
                coordinator,
                store,
                store,
                NullLogger<UpdateCartItemHandler>.Instance
            ),
            new RemoveFromCartHandler(
                new RemoveFromCartValidator(),
                ownerScope,
                coordinator,
                store,
                NullLogger<RemoveFromCartHandler>.Instance
            ),
            new ClearCartHandler(
                new ClearCartValidator(),
                ownerScope,
                coordinator,
                store,
                NullLogger<ClearCartHandler>.Instance
            ),
            new ScanLookupAndAddToCartHandler(
                new ScanLookupAndAddToCartValidator(),
                productLookup,
                ownerScope,
                coordinator,
                store,
                store,
                NullLogger<ScanLookupAndAddToCartHandler>.Instance
            )
        );

        return new StoreOrderCartLegacyComposition(
            slice,
            ownerScope,
            coordinator,
            store
        );
    }
}
