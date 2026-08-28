using BlazorApp.Api.Features.StoreOrders.Cart.Commands.AddToCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.ClearCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.RemoveFromCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.ScanLookupAndAddToCart;
using BlazorApp.Api.Features.StoreOrders.Cart.Commands.UpdateCartItem;
using BlazorApp.Api.Features.StoreOrders.Cart.Queries;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart;

public interface IStoreOrderCartSlice
{
    Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartAsync(string storeCode);

    Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartSummaryAsync(string storeCode);

    Task<ApiResponse<StoreOrderCartDto?>> AddToCartAsync(AddToCartRequestDto request);

    Task<ApiResponse<StoreOrderCartMutationResultDto?>> AddToCartMutationAsync(
        AddToCartRequestDto request
    );

    Task<ApiResponse<StoreOrderCartDto?>> UpdateCartItemAsync(
        AddToCartRequestDto request
    );

    Task<ApiResponse<StoreOrderCartMutationResultDto?>> UpdateCartItemMutationAsync(
        AddToCartRequestDto request
    );

    Task<ApiResponse<bool>> RemoveFromCartAsync(RemoveFromCartRequestDto request);

    Task<ApiResponse<StoreOrderCartDto?>> ClearCartAsync(string storeCode);

    Task<ApiResponse<StoreOrderScanLookupAddResultDto>> ScanLookupAndAddToCartMutationAsync(
        StoreOrderScanLookupAddRequestDto request
    );
}

internal sealed class StoreOrderCartSlice(
    GetActiveCartHandler getActiveCartHandler,
    GetActiveCartSummaryHandler getActiveCartSummaryHandler,
    AddToCartHandler addToCartHandler,
    UpdateCartItemHandler updateCartItemHandler,
    RemoveFromCartHandler removeFromCartHandler,
    ClearCartHandler clearCartHandler,
    ScanLookupAndAddToCartHandler scanLookupAndAddToCartHandler
) : IStoreOrderCartSlice
{
    public Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartAsync(string storeCode)
    {
        return getActiveCartHandler.HandleAsync(new GetActiveCartQuery(storeCode));
    }

    public Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartSummaryAsync(
        string storeCode
    )
    {
        return getActiveCartSummaryHandler.HandleAsync(
            new GetActiveCartSummaryQuery(storeCode)
        );
    }

    public Task<ApiResponse<StoreOrderCartDto?>> AddToCartAsync(
        AddToCartRequestDto request
    )
    {
        return addToCartHandler.HandleFullAsync(request);
    }

    public Task<ApiResponse<StoreOrderCartMutationResultDto?>> AddToCartMutationAsync(
        AddToCartRequestDto request
    )
    {
        return addToCartHandler.HandleMutationAsync(request);
    }

    public Task<ApiResponse<StoreOrderCartDto?>> UpdateCartItemAsync(
        AddToCartRequestDto request
    )
    {
        return updateCartItemHandler.HandleFullAsync(request);
    }

    public Task<ApiResponse<StoreOrderCartMutationResultDto?>> UpdateCartItemMutationAsync(
        AddToCartRequestDto request
    )
    {
        return updateCartItemHandler.HandleMutationAsync(request);
    }

    public Task<ApiResponse<bool>> RemoveFromCartAsync(RemoveFromCartRequestDto request)
    {
        return removeFromCartHandler.HandleAsync(new RemoveFromCartCommand(request));
    }

    public Task<ApiResponse<StoreOrderCartDto?>> ClearCartAsync(string storeCode)
    {
        return clearCartHandler.HandleAsync(new ClearCartCommand(storeCode));
    }

    public Task<ApiResponse<StoreOrderScanLookupAddResultDto>> ScanLookupAndAddToCartMutationAsync(
        StoreOrderScanLookupAddRequestDto request
    )
    {
        return scanLookupAndAddToCartHandler.HandleAsync(
            new ScanLookupAndAddToCartCommand(request)
        );
    }
}
