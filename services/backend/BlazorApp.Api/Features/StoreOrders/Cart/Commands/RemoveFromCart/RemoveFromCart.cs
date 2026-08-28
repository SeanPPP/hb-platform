using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Commands.RemoveFromCart;

internal sealed record RemoveFromCartCommand(RemoveFromCartRequestDto? Request);

internal sealed class RemoveFromCartValidator
{
    internal StoreOrderCartValidationFailure? Validate(RemoveFromCartCommand command)
    {
        if (command.Request == null || string.IsNullOrWhiteSpace(command.Request.StoreCode))
        {
            return new StoreOrderCartValidationFailure("StoreCode is required");
        }

        return string.IsNullOrWhiteSpace(command.Request.DetailGUID)
            ? new StoreOrderCartValidationFailure("DetailGUID is required")
            : null;
    }
}

internal sealed class RemoveFromCartHandler(
    RemoveFromCartValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartCommandCoordinator coordinator,
    IStoreOrderCartCommandStore commandStore,
    ILogger<RemoveFromCartHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(RemoveFromCartCommand command)
    {
        var validationFailure = validator.Validate(command);
        if (validationFailure != null)
        {
            return StoreOrderCartRules.ValidationFailure<bool>(validationFailure.Value);
        }

        var request = command.Request!;
        try
        {
            var scope = ownerScope.Resolve(request.StoreCode);
            return await coordinator.ExecuteAsync(scope, async () =>
            {
                var removed = await commandStore.RemoveAsync(
                    scope,
                    request.DetailGUID.Trim()
                );
                return removed
                    ? new ApiResponse<bool> { Success = true, Data = true }
                    : new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Cart item not found",
                    };
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "RemoveFromCartAsync failed");
            return new ApiResponse<bool>
            {
                Success = false,
                Message = exception.Message,
            };
        }
    }
}
