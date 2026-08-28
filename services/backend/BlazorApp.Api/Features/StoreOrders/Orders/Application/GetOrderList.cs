using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record GetOrderListQuery(StoreOrderListFilterDto? Request);

internal sealed class GetOrderListValidator
{
    internal StoreOrderListFilterDto Validate(GetOrderListQuery query)
    {
        return query.Request ?? new StoreOrderListFilterDto();
    }
}

internal sealed class GetOrderListHandler(
    GetOrderListValidator validator,
    StoreOrderListQueryStore queryStore,
    ILogger<GetOrderListHandler> logger
)
{
    internal async Task<PagedListReactDto<StoreOrderListItemDto>> HandleAsync(
        GetOrderListQuery query
    )
    {
        var filter = validator.Validate(query);
        try
        {
            return await queryStore.GetAsync(filter);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetOrderListAsync failed");
            throw;
        }
    }
}
