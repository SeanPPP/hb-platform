using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record GetUnmatchedStoreOrderGroupsQuery;

internal sealed class GetUnmatchedStoreOrderGroupsValidator
{
    internal void Validate(GetUnmatchedStoreOrderGroupsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
    }
}

internal sealed class GetUnmatchedStoreOrderGroupsHandler(
    GetUnmatchedStoreOrderGroupsValidator validator,
    StoreOrderLookupQueryStore queryStore,
    ILogger<GetUnmatchedStoreOrderGroupsHandler> logger
)
{
    internal async Task<ApiResponse<List<UnmatchedStoreOrderGroupDto>>> HandleAsync(
        GetUnmatchedStoreOrderGroupsQuery query
    )
    {
        validator.Validate(query);
        try
        {
            return ApiResponse<List<UnmatchedStoreOrderGroupDto>>.OK(
                await queryStore.GetUnmatchedGroupsAsync()
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetUnmatchedStoreOrderGroupsAsync failed");
            return ApiResponse<List<UnmatchedStoreOrderGroupDto>>.Error(ex.Message);
        }
    }
}
