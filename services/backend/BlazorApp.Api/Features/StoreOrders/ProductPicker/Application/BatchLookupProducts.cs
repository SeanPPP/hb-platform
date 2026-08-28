using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;

internal sealed record BatchLookupProductsQuery(StoreOrderBatchLookupRequestDto? Request);

internal sealed class BatchLookupProductsValidator
{
    internal ProductPickerValidationResult<ProductPickerBatchLookupInput> Validate(
        BatchLookupProductsQuery query
    )
    {
        var codes = ProductPickerRules.NormalizeBatchLookupCodes(
            query.Request?.Codes ?? new List<string>()
        );
        if (codes.Count > ProductPickerRules.BatchLookupMaximumCodes)
        {
            return ProductPickerValidationResult<ProductPickerBatchLookupInput>.Invalid(
                $"一次最多查询 {ProductPickerRules.BatchLookupMaximumCodes} 个商品编码"
            );
        }

        return ProductPickerValidationResult<ProductPickerBatchLookupInput>.Valid(
            new ProductPickerBatchLookupInput(codes)
        );
    }
}

internal sealed class BatchLookupProductsHandler(
    BatchLookupProductsValidator validator,
    ProductPickerBatchQueryStore queryStore,
    ILogger<BatchLookupProductsHandler> logger
)
{
    internal async Task<ApiResponse<List<StoreOrderBatchLookupItemDto>>> HandleAsync(
        BatchLookupProductsQuery query
    )
    {
        var validation = validator.Validate(query);
        if (!validation.IsValid)
        {
            return new ApiResponse<List<StoreOrderBatchLookupItemDto>>
            {
                Success = false,
                Message = validation.ErrorMessage!,
            };
        }

        var input = validation.Value!;
        if (input.Codes.Count == 0)
        {
            return new ApiResponse<List<StoreOrderBatchLookupItemDto>>
            {
                Success = true,
                Data = new List<StoreOrderBatchLookupItemDto>(),
            };
        }

        try
        {
            var products = await queryStore.LookupAsync(input);
            return new ApiResponse<List<StoreOrderBatchLookupItemDto>>
            {
                Success = true,
                Data = ProductPickerRules.BuildBatchLookupResults(
                    input.Codes,
                    products
                ),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BatchLookupProductsAsync failed");
            return new ApiResponse<List<StoreOrderBatchLookupItemDto>>
            {
                Success = false,
                Message = ex.Message,
            };
        }
    }
}
