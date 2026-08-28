using BlazorApp.Api.Features.StoreOrders.Orders.Domain;
using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record GetOrderDetailQuery(
    string OrderGuid,
    StoreOrderDetailQueryDto? Request
);

internal sealed class GetOrderDetailValidator
{
    internal StoreOrderDetailInput Validate(GetOrderDetailQuery query)
    {
        return new StoreOrderDetailInput(
            query.OrderGuid,
            StoreOrderOrdersRules.NormalizeDetailQuery(query.Request),
            LoadAllItems: false
        );
    }
}

internal sealed class GetOrderDetailHandler(
    GetOrderDetailValidator validator,
    StoreOrderDetailQueryStore queryStore
)
{
    internal Task<ApiResponse<StoreOrderDetailDto?>> HandleAsync(GetOrderDetailQuery query)
    {
        return queryStore.GetAsync(validator.Validate(query));
    }
}
