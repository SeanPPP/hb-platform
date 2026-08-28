using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record GetOrderDetailProductCodesQuery(string OrderGuid);

internal sealed class GetOrderDetailProductCodesValidator
{
    internal string Validate(GetOrderDetailProductCodesQuery query)
    {
        return query.OrderGuid;
    }
}

internal sealed class GetOrderDetailProductCodesHandler(
    GetOrderDetailProductCodesValidator validator,
    StoreOrderDetailQueryStore queryStore
)
{
    internal Task<ApiResponse<List<string>>> HandleAsync(
        GetOrderDetailProductCodesQuery query
    )
    {
        return queryStore.GetProductCodesAsync(validator.Validate(query));
    }
}
