using BlazorApp.Api.Features.StoreOrders.Orders.Domain;
using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record GetOrderDetailFullQuery(string OrderGuid);

internal sealed class GetOrderDetailFullValidator
{
    internal StoreOrderDetailInput Validate(GetOrderDetailFullQuery query)
    {
        return new StoreOrderDetailInput(
            query.OrderGuid,
            StoreOrderOrdersRules.NormalizeDetailQuery(null),
            LoadAllItems: true
        );
    }
}

internal sealed class GetOrderDetailFullHandler(
    GetOrderDetailFullValidator validator,
    StoreOrderDetailQueryStore queryStore
)
{
    internal async Task<ApiResponse<StoreOrderCartDto?>> HandleAsync(
        GetOrderDetailFullQuery query
    )
    {
        var result = await queryStore.GetAsync(validator.Validate(query));
        return StoreOrderOrdersRules.ToFullDetailResponse(result);
    }
}
