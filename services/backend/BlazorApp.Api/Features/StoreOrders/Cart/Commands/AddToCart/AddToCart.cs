using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Commands.AddToCart;

internal sealed record AddToCartCommand(
    AddToCartRequestDto? Request,
    StoreOrderCartResponseShape ResponseShape,
    StoreOrderProductDto? KnownProduct = null
);

internal sealed class AddToCartValidator
{
    internal StoreOrderCartValidationFailure? Validate(AddToCartCommand command)
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

internal sealed class AddToCartHandler(
    AddToCartValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartCommandCoordinator coordinator,
    IStoreOrderCartCommandStore commandStore,
    IStoreOrderCartQueryStore queryStore,
    ILogger<AddToCartHandler> logger
)
{
    internal Task<ApiResponse<StoreOrderCartDto?>> HandleFullAsync(
        AddToCartRequestDto request
    )
    {
        return HandleAsync(
            new AddToCartCommand(request, StoreOrderCartResponseShape.Full),
            async write => new ApiResponse<StoreOrderCartDto?>
            {
                Success = true,
                Data = await queryStore.GetFullAsync(
                    ownerScope.Resolve(write.StoreCode)
                ),
            }
        );
    }

    internal Task<ApiResponse<StoreOrderCartMutationResultDto?>> HandleMutationAsync(
        AddToCartRequestDto request
    )
    {
        return HandleMutationAsync(request, null);
    }

    internal Task<ApiResponse<StoreOrderCartMutationResultDto?>> HandleMutationAsync(
        AddToCartRequestDto request,
        StoreOrderProductDto? knownProduct
    )
    {
        return HandleAsync(
            new AddToCartCommand(
                request,
                StoreOrderCartResponseShape.Mutation,
                knownProduct
            ),
            async write => new ApiResponse<StoreOrderCartMutationResultDto?>
            {
                Success = true,
                Data = await queryStore.GetMutationResultAsync(write),
            }
        );
    }

    private async Task<ApiResponse<T>> HandleAsync<T>(
        AddToCartCommand command,
        Func<StoreOrderCartMutationWrite, Task<ApiResponse<T>>> responseFactory
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
                var outcome = await commandStore.AddAsync(
                    scope,
                    request.ProductCode.Trim(),
                    request.Quantity,
                    command.KnownProduct,
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

                return await responseFactory(outcome.Write);
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "AddToCartAsync failed");
            return new ApiResponse<T>
            {
                Success = false,
                Message = exception.Message,
            };
        }
    }
}
