using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Commands.ScanLookupAndAddToCart;

internal sealed record ScanLookupAndAddToCartCommand(
    StoreOrderScanLookupAddRequestDto? Request
);

internal sealed class ScanLookupAndAddToCartValidator
{
    internal StoreOrderCartValidationFailure? Validate(
        ScanLookupAndAddToCartCommand command
    )
    {
        if (command.Request == null || string.IsNullOrWhiteSpace(command.Request.StoreCode))
        {
            return new StoreOrderCartValidationFailure("StoreCode is required");
        }

        return string.IsNullOrWhiteSpace(command.Request.Barcode)
            ? new StoreOrderCartValidationFailure("Barcode is required.")
            : null;
    }
}

internal sealed class ScanLookupAndAddToCartHandler(
    ScanLookupAndAddToCartValidator validator,
    IStoreOrderCartProductLookup productLookup,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartCommandCoordinator coordinator,
    IStoreOrderCartCommandStore commandStore,
    IStoreOrderCartQueryStore queryStore,
    ILogger<ScanLookupAndAddToCartHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderScanLookupAddResultDto>> HandleAsync(
        ScanLookupAndAddToCartCommand command
    )
    {
        var validationFailure = validator.Validate(command);
        if (validationFailure != null)
        {
            return StoreOrderCartRules.ValidationFailure<StoreOrderScanLookupAddResultDto>(
                validationFailure.Value
            );
        }

        var request = command.Request!;
        var barcode = request.Barcode.Trim();
        try
        {
            var lookupResponse = await productLookup.LookupAsync(
                new StoreOrderScanLookupRequestDto
                {
                    StoreCode = request.StoreCode,
                    Barcode = barcode,
                }
            );
            if (!lookupResponse.Success)
            {
                return new ApiResponse<StoreOrderScanLookupAddResultDto>
                {
                    Success = false,
                    Message = lookupResponse.Message,
                    ErrorCode = lookupResponse.ErrorCode,
                    Details = lookupResponse.Details,
                };
            }

            var lookup = lookupResponse.Data ?? new StoreOrderScanLookupResultDto
            {
                Barcode = barcode,
            };
            var response = new StoreOrderScanLookupAddResultDto
            {
                Barcode = barcode,
                MatchType = lookup.MatchType,
                Items = lookup.Items,
                Added = false,
                Cart = null,
            };
            if (lookup.Items.Count != 1)
            {
                return ApiResponse<StoreOrderScanLookupAddResultDto>.OK(response);
            }

            var product = lookup.Items[0];
            var quantity = request.Quantity.GetValueOrDefault();
            if (quantity <= 0)
            {
                quantity = product.MinOrderQuantity > 0
                    ? product.MinOrderQuantity
                    : 1;
            }

            var scope = ownerScope.Resolve(request.StoreCode);
            return await coordinator.ExecuteAsync(scope, async () =>
            {
                var outcome = await commandStore.AddAsync(
                    scope,
                    product.ProductCode,
                    quantity,
                    product,
                    omitNonPositiveNewDetail: true
                );
                if (!outcome.Success || outcome.Write == null)
                {
                    return new ApiResponse<StoreOrderScanLookupAddResultDto>
                    {
                        Success = false,
                        Message = outcome.Message ?? "商品不存在",
                    };
                }

                response.Added = true;
                response.Cart = await queryStore.GetMutationResultAsync(outcome.Write);
                return ApiResponse<StoreOrderScanLookupAddResultDto>.OK(response);
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "ScanLookupAndAddToCartMutationAsync failed for store {StoreCode}",
                request.StoreCode
            );
            return new ApiResponse<StoreOrderScanLookupAddResultDto>
            {
                Success = false,
                Message = exception.Message,
            };
        }
    }
}
