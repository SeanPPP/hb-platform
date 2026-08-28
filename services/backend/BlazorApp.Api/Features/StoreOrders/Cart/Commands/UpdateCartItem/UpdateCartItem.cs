using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Commands.UpdateCartItem;

internal sealed record UpdateCartItemCommand(
    AddToCartRequestDto? Request,
    StoreOrderCartResponseShape ResponseShape
);

internal sealed class UpdateCartItemValidator
{
    internal StoreOrderCartValidationFailure? Validate(UpdateCartItemCommand command)
    {
        if (command.Request == null || string.IsNullOrWhiteSpace(command.Request.StoreCode))
        {
            return new StoreOrderCartValidationFailure("StoreCode is required");
        }

        return string.IsNullOrWhiteSpace(command.Request.ProductCode)
            ? new StoreOrderCartValidationFailure("ProductCode is required")
            : null;
    }
}

internal sealed class UpdateCartItemHandler(
    UpdateCartItemValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartCommandCoordinator coordinator,
    IStoreOrderCartCommandStore commandStore,
    IStoreOrderCartQueryStore queryStore,
    ILogger<UpdateCartItemHandler> logger
)
{
    internal Task<ApiResponse<StoreOrderCartDto?>> HandleFullAsync(
        AddToCartRequestDto request
    )
    {
        return HandleAsync(
            new UpdateCartItemCommand(request, StoreOrderCartResponseShape.Full),
            async (scope, write) => new ApiResponse<StoreOrderCartDto?>
            {
                Success = true,
                Data = await queryStore.GetFullAsync(scope),
            }
        );
    }

    internal Task<ApiResponse<StoreOrderCartMutationResultDto?>> HandleMutationAsync(
        AddToCartRequestDto request
    )
    {
        return HandleAsync(
            new UpdateCartItemCommand(request, StoreOrderCartResponseShape.Mutation),
            async (scope, write) => new ApiResponse<StoreOrderCartMutationResultDto?>
            {
                Success = true,
                Data = await queryStore.GetMutationResultAsync(write),
            }
        );
    }

    private async Task<ApiResponse<T>> HandleAsync<T>(
        UpdateCartItemCommand command,
        Func<StoreOrderCartScope, StoreOrderCartMutationWrite, Task<ApiResponse<T>>> responseFactory
    )
    {
        var validationFailure = validator.Validate(command);
        if (validationFailure != null)
        {
            return StoreOrderCartRules.ValidationFailure<T>(validationFailure.Value);
        }

        var request = command.Request!;
        try
        {
            var scope = ownerScope.Resolve(request.StoreCode);
            return await coordinator.ExecuteAsync(scope, async () =>
            {
                var outcome = await commandStore.SetQuantityAsync(
                    scope,
                    request.ProductCode.Trim(),
                    request.Quantity,
                    command.ResponseShape == StoreOrderCartResponseShape.Mutation
                );
                if (!outcome.Success || outcome.Write == null)
                {
                    return new ApiResponse<T>
                    {
                        Success = false,
                        Message = outcome.Message ?? "商品不存在",
                    };
                }

                return await responseFactory(scope, outcome.Write);
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "UpdateCartItemAsync failed");
            return new ApiResponse<T>
            {
                Success = false,
                Message = exception.Message,
            };
        }
    }
}
