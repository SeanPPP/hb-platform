using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record GetUsedBranchesQuery;

internal sealed class GetUsedBranchesValidator
{
    internal void Validate(GetUsedBranchesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
    }
}

internal sealed class GetUsedBranchesHandler(
    GetUsedBranchesValidator validator,
    StoreOrderLookupQueryStore queryStore,
    ILogger<GetUsedBranchesHandler> logger
)
{
    internal async Task<ApiResponse<List<BranchDto>>> HandleAsync(GetUsedBranchesQuery query)
    {
        validator.Validate(query);
        try
        {
            return new ApiResponse<List<BranchDto>>
            {
                Success = true,
                Data = await queryStore.GetUsedBranchesAsync(),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetUsedBranchesAsync failed");
            return new ApiResponse<List<BranchDto>>
            {
                Success = false,
                Message = ex.Message,
            };
        }
    }
}
