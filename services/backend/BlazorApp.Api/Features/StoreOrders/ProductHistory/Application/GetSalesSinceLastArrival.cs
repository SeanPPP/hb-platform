using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;

internal sealed record GetSalesSinceLastArrivalQuery(
    StoreOrderSalesSinceLastArrivalRequestDto Request
);

internal sealed class GetSalesSinceLastArrivalValidator
{
    internal SalesSinceLastArrivalQueryInput Validate(GetSalesSinceLastArrivalQuery query)
    {
        return new SalesSinceLastArrivalQueryInput(
            query.Request.StoreCode?.Trim() ?? string.Empty,
            query.Request.ProductCode?.Trim() ?? string.Empty,
            ProductHistoryRules.NormalizePageNumber(query.Request.PageNumber),
            ProductHistoryRules.SalesSinceLastArrivalPageSize
        );
    }
}

internal sealed class GetSalesSinceLastArrivalHandler(
    GetSalesSinceLastArrivalValidator validator,
    IProductHistoryQueryStore queryStore,
    ILogger<GetSalesSinceLastArrivalHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderSalesSinceLastArrivalResultDto>> HandleAsync(
        GetSalesSinceLastArrivalQuery query
    )
    {
        try
        {
            var input = validator.Validate(query);
            if (
                string.IsNullOrWhiteSpace(input.StoreCode)
                || string.IsNullOrWhiteSpace(input.ProductCode)
            )
            {
                var unavailable = ProductHistoryRules.CreateSalesResult(input);
                unavailable.IsAvailable = false;
                return new ApiResponse<StoreOrderSalesSinceLastArrivalResultDto>
                {
                    Success = true,
                    Data = unavailable,
                };
            }

            var result = await queryStore.GetSalesSinceLastArrivalAsync(input);
            return new ApiResponse<StoreOrderSalesSinceLastArrivalResultDto>
            {
                Success = true,
                Data = result,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetSalesSinceLastArrivalAsync failed");
            throw;
        }
    }
}
