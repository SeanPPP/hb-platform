using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;

internal sealed record GetImportPriceVarianceDetailsQuery(
    StoreOrderImportPriceVarianceDetailQueryDto? Request
);

internal sealed class GetImportPriceVarianceDetailsValidator
{
    internal ImportPriceVarianceDetailQueryInput Validate(
        GetImportPriceVarianceDetailsQuery query
    )
    {
        var request = query.Request ?? new StoreOrderImportPriceVarianceDetailQueryDto();
        return new ImportPriceVarianceDetailQueryInput(
            request,
            ImportPriceVarianceRules.NormalizePage(request.PageNumber, request.PageSize),
            ImportPriceVarianceRules.NormalizeRequestedStoreCodes(request),
            request.ProductCode?.Trim()
        );
    }
}

internal sealed class GetImportPriceVarianceDetailsHandler(
    GetImportPriceVarianceDetailsValidator validator,
    IStoreOrderAccessScope accessScope,
    ImportPriceVarianceQueryStore queryStore,
    ILogger<GetImportPriceVarianceDetailsHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>> HandleAsync(
        GetImportPriceVarianceDetailsQuery query
    )
    {
        var input = validator.Validate(query);
        if (string.IsNullOrWhiteSpace(input.ProductCode))
        {
            return ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>.OK(
                ImportPriceVarianceRules.CreateEmptyDetailResult(input.Page)
            );
        }

        try
        {
            var accessibleStoreCodes = await accessScope.GetAccessibleStoreCodesAsync();
            var storeSelection = ImportPriceVarianceRules.ApplyAccessScope(
                input.RequestedStoreCodes,
                accessibleStoreCodes
            );
            if (storeSelection.NoAccessibleStores)
            {
                return ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>.OK(
                    ImportPriceVarianceRules.CreateEmptyDetailResult(input.Page)
                );
            }

            var result = await queryStore.GetDetailsAsync(input, storeSelection.StoreCodes);
            return ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>.OK(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetImportPriceVarianceDetailsAsync failed");
            return new ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>
            {
                Success = false,
                Message = ex.Message,
                Data = ImportPriceVarianceRules.CreateEmptyDetailResult(input.Page),
            };
        }
    }
}
