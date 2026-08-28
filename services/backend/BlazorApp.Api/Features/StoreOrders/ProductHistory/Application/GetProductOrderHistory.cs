using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;

internal sealed record GetProductOrderHistoryQuery(
    StoreOrderProductOrderHistoryRequestDto Request
);

internal sealed class GetProductOrderHistoryValidator
{
    internal ProductOrderHistoryQueryInput Validate(GetProductOrderHistoryQuery query)
    {
        return new ProductOrderHistoryQueryInput(
            query.Request.StoreCode?.Trim() ?? string.Empty,
            query.Request.ProductCode?.Trim() ?? string.Empty,
            ProductHistoryRules.NormalizePageNumber(query.Request.PageNumber),
            ProductHistoryRules.NormalizePageSize(
                query.Request.PageSize,
                ProductHistoryRules.ProductOrderHistoryDefaultPageSize
            )
        );
    }
}

internal sealed class GetProductOrderHistoryHandler(
    GetProductOrderHistoryValidator validator,
    IProductHistoryQueryStore queryStore,
    ILogger<GetProductOrderHistoryHandler> logger
)
{
    internal async Task<
        ApiResponse<PagedListReactDto<StoreOrderProductOrderHistoryItemDto>>
    > HandleAsync(GetProductOrderHistoryQuery query)
    {
        var input = validator.Validate(query);
        var emptyResult = ProductHistoryRules.CreateOrderHistoryResult(input);
        if (
            string.IsNullOrWhiteSpace(input.StoreCode)
            || string.IsNullOrWhiteSpace(input.ProductCode)
        )
        {
            return new ApiResponse<PagedListReactDto<StoreOrderProductOrderHistoryItemDto>>
            {
                Success = true,
                Data = emptyResult,
            };
        }

        try
        {
            var result = await queryStore.GetProductOrderHistoryAsync(input);
            return new ApiResponse<PagedListReactDto<StoreOrderProductOrderHistoryItemDto>>
            {
                Success = true,
                Data = result,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "GetProductOrderHistoryAsync failed storeCode={StoreCode} productCode={ProductCode}",
                input.StoreCode,
                input.ProductCode
            );
            throw;
        }
    }
}
