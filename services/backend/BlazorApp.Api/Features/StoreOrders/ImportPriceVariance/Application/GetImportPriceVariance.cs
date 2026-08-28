using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;

internal sealed record GetImportPriceVarianceQuery(
    StoreOrderImportPriceVarianceQueryDto? Request
);

internal sealed class GetImportPriceVarianceValidator
{
    internal ImportPriceVarianceQueryInput Validate(GetImportPriceVarianceQuery query)
    {
        var request = query.Request ?? new StoreOrderImportPriceVarianceQueryDto();
        return new ImportPriceVarianceQueryInput(
            request,
            ImportPriceVarianceRules.NormalizePage(request.PageNumber, request.PageSize),
            ImportPriceVarianceRules.NormalizeRequestedStoreCodes(request)
        );
    }
}

internal sealed class GetImportPriceVarianceHandler(
    GetImportPriceVarianceValidator validator,
    IStoreOrderAccessScope accessScope,
    ImportPriceVarianceQueryStore queryStore,
    ILogger<GetImportPriceVarianceHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderImportPriceVarianceResultDto>> HandleAsync(
        GetImportPriceVarianceQuery query
    )
    {
        var input = validator.Validate(query);

        try
        {
            var accessibleStoreCodes = await accessScope.GetAccessibleStoreCodesAsync();
            var storeSelection = ImportPriceVarianceRules.ApplyAccessScope(
                input.RequestedStoreCodes,
                accessibleStoreCodes
            );
            if (storeSelection.NoAccessibleStores)
            {
                return ApiResponse<StoreOrderImportPriceVarianceResultDto>.OK(
                    ImportPriceVarianceRules.CreateEmptyResult(input.Page)
                );
            }

            var result = await queryStore.GetSummaryAsync(input, storeSelection.StoreCodes);
            return ApiResponse<StoreOrderImportPriceVarianceResultDto>.OK(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetImportPriceVarianceAsync failed");
            return new ApiResponse<StoreOrderImportPriceVarianceResultDto>
            {
                Success = false,
                Message = ex.Message,
                Data = ImportPriceVarianceRules.CreateEmptyResult(input.Page),
            };
        }
    }
}
