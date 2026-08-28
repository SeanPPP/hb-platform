using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;

internal sealed record GetSalesSinceLastArrivalSummaryQuery(
    StoreOrderSalesSinceLastArrivalSummaryRequestDto Request
);

internal sealed class GetSalesSinceLastArrivalSummaryValidator
{
    internal SalesSinceLastArrivalSummaryQueryInput Validate(
        GetSalesSinceLastArrivalSummaryQuery query
    )
    {
        return new SalesSinceLastArrivalSummaryQueryInput(
            query.Request.StoreCode,
            ProductHistoryRules.NormalizeProductCodes(query.Request.ProductCodes)
        );
    }
}

internal sealed class GetSalesSinceLastArrivalSummaryHandler(
    GetSalesSinceLastArrivalSummaryValidator validator,
    IProductHistoryQueryStore queryStore,
    ILogger<GetSalesSinceLastArrivalSummaryHandler> logger
)
{
    internal async Task<
        ApiResponse<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>>
    > HandleAsync(GetSalesSinceLastArrivalSummaryQuery query)
    {
        try
        {
            var input = validator.Validate(query);
            if (
                input.ProductCodes.Count
                > ProductHistoryRules.SalesStatisticsMaxProductCodesPerCutoffGroup
            )
            {
                return new ApiResponse<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>>
                {
                    Success = false,
                    Message = "商品数量不能超过500",
                };
            }

            var emptyResult = ProductHistoryRules.CreateSalesSummaryResult(input.ProductCodes);
            if (emptyResult.Count == 0)
            {
                return ApiResponse<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>>.OK(
                    emptyResult
                );
            }

            var result = await queryStore.GetSalesSinceLastArrivalSummaryAsync(input);
            return ApiResponse<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>>.OK(result);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "GetSalesSinceLastArrivalSummaryAsync failed storeCode={StoreCode} requestCount={RequestCount}",
                query.Request.StoreCode,
                query.Request.ProductCodes?.Count ?? 0
            );
            throw;
        }
    }
}
