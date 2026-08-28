using BlazorApp.Api.Features.StoreOrders.Invoice.Domain;
using BlazorApp.Api.Features.StoreOrders.Invoice.Application.Ports;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Invoice.Application;

internal sealed record GetStoreOrderInvoiceDetailQuery(string OrderGuid);

internal sealed class GetStoreOrderInvoiceDetailValidator
{
    internal StoreOrderInvoiceDetailValidationResult Validate(
        GetStoreOrderInvoiceDetailQuery query
    )
    {
        if (string.IsNullOrWhiteSpace(query.OrderGuid))
        {
            return StoreOrderInvoiceDetailValidationResult.Invalid("Order not found");
        }

        return StoreOrderInvoiceDetailValidationResult.Valid(query.OrderGuid.Trim());
    }
}

internal sealed class GetStoreOrderInvoiceDetailHandler(
    GetStoreOrderInvoiceDetailValidator validator,
    IStoreOrderInvoiceDetailQueryStore queryStore
)
{
    internal async Task<ApiResponse<StoreOrderCartDto?>> HandleAsync(
        GetStoreOrderInvoiceDetailQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var validation = validator.Validate(query);
        if (!validation.IsValid || validation.OrderGuid == null)
        {
            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = false,
                Message = validation.ErrorMessage ?? "Order not found",
            };
        }

        var readResult = await queryStore.GetAsync(
            validation.OrderGuid,
            cancellationToken
        );
        if (!readResult.Success || readResult.Detail == null)
        {
            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = false,
                Message = readResult.ErrorMessage ?? "Order not found",
            };
        }

        return new ApiResponse<StoreOrderCartDto?>
        {
            Success = true,
            Data = StoreOrderInvoiceDetailRules.ToDto(readResult.Detail),
        };
    }
}
