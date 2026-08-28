using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Queries;

internal sealed record GetActiveCartQuery(string? StoreCode);

internal sealed class GetActiveCartValidator
{
    internal StoreOrderCartValidationFailure? Validate(GetActiveCartQuery query)
    {
        return string.IsNullOrWhiteSpace(query.StoreCode)
            ? new StoreOrderCartValidationFailure("StoreCode is required")
            : null;
    }
}

internal sealed class GetActiveCartHandler(
    GetActiveCartValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartQueryStore queryStore,
    ILogger<GetActiveCartHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderCartDto?>> HandleAsync(
        GetActiveCartQuery query
    )
    {
        var validationFailure = validator.Validate(query);
        if (validationFailure != null)
        {
            return StoreOrderCartRules.ValidationFailure<StoreOrderCartDto?>(
                validationFailure.Value
            );
        }

        try
        {
            var scope = ownerScope.Resolve(query.StoreCode);
            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = true,
                Data = await queryStore.GetFullAsync(scope),
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "GetActiveCartAsync failed");
            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = false,
                Message = exception.Message,
            };
        }
    }
}
