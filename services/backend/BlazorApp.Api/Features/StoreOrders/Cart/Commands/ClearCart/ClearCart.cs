using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Commands.ClearCart;

internal sealed record ClearCartCommand(string? StoreCode);

internal sealed class ClearCartValidator
{
    internal StoreOrderCartValidationFailure? Validate(ClearCartCommand command)
    {
        return string.IsNullOrWhiteSpace(command.StoreCode)
            ? new StoreOrderCartValidationFailure("StoreCode is required")
            : null;
    }
}

internal sealed class ClearCartHandler(
    ClearCartValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartCommandCoordinator coordinator,
    IStoreOrderCartCommandStore commandStore,
    ILogger<ClearCartHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderCartDto?>> HandleAsync(
        ClearCartCommand command
    )
    {
        var validationFailure = validator.Validate(command);
        if (validationFailure != null)
        {
            return StoreOrderCartRules.ValidationFailure<StoreOrderCartDto?>(
                validationFailure.Value
            );
        }

        try
        {
            var scope = ownerScope.Resolve(command.StoreCode);
            return await coordinator.ExecuteAsync(scope, async () =>
            {
                var outcome = await commandStore.ClearAsync(scope);
                return new ApiResponse<StoreOrderCartDto?>
                {
                    Success = true,
                    Data = null,
                    Message = outcome.CartExisted
                        ? "Cart cleared successfully"
                        : "Cart is already empty",
                };
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "ClearCartAsync failed for store {StoreCode}",
                command.StoreCode
            );
            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = false,
                Message = "Failed to clear cart",
            };
        }
    }
}
