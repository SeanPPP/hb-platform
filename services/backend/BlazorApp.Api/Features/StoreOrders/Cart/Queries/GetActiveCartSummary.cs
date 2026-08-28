using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Queries;

internal sealed record GetActiveCartSummaryQuery(string? StoreCode);

internal sealed class GetActiveCartSummaryValidator
{
    internal StoreOrderCartValidationFailure? Validate(GetActiveCartSummaryQuery query)
    {
        return string.IsNullOrWhiteSpace(query.StoreCode)
            ? new StoreOrderCartValidationFailure("StoreCode is required")
            : null;
    }
}

internal sealed class GetActiveCartSummaryHandler(
    GetActiveCartSummaryValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartQueryStore queryStore,
    ILogger<GetActiveCartSummaryHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderCartDto?>> HandleAsync(
        GetActiveCartSummaryQuery query
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
                Data = await queryStore.GetSummaryAsync(scope),
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "GetActiveCartSummaryAsync failed");
            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = false,
                Message = exception.Message,
            };
        }
    }
}
