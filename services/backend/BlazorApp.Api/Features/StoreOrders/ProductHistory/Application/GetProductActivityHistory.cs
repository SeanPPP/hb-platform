using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;

internal sealed record GetProductActivityHistoryQuery(
    StoreOrderProductActivityHistoryRequestDto Request
);

internal sealed class GetProductActivityHistoryValidator
{
    internal ProductActivityHistoryQueryInput Validate(GetProductActivityHistoryQuery query)
    {
        return new ProductActivityHistoryQueryInput(
            query.Request.StoreCode?.Trim() ?? string.Empty,
            query.Request.ProductCode?.Trim() ?? string.Empty,
            ProductHistoryRules.NormalizePageNumber(query.Request.PageNumber),
            ProductHistoryRules.NormalizePageSize(
                query.Request.PageSize,
                ProductHistoryRules.ProductActivityHistoryDefaultPageSize
            ),
            ProductHistoryRules.NormalizeActivityRecordType(query.Request.RecordType)
        );
    }
}

internal sealed class GetProductActivityHistoryHandler(
    GetProductActivityHistoryValidator validator,
    IProductHistoryQueryStore queryStore,
    ILogger<GetProductActivityHistoryHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderProductActivityHistoryResultDto>> HandleAsync(
        GetProductActivityHistoryQuery query
    )
    {
        var input = validator.Validate(query);
        var emptyResult = ProductHistoryRules.CreateActivityHistoryResult(input);
        if (
            string.IsNullOrWhiteSpace(input.StoreCode)
            || string.IsNullOrWhiteSpace(input.ProductCode)
        )
        {
            return new ApiResponse<StoreOrderProductActivityHistoryResultDto>
            {
                Success = true,
                Data = emptyResult,
            };
        }

        try
        {
            var result = await queryStore.GetProductActivityHistoryAsync(input);
            return new ApiResponse<StoreOrderProductActivityHistoryResultDto>
            {
                Success = true,
                Data = result,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "GetProductActivityHistoryAsync failed storeCode={StoreCode} productCode={ProductCode} recordType={RecordType}",
                input.StoreCode,
                input.ProductCode,
                input.RecordType
            );
            throw;
        }
    }
}
