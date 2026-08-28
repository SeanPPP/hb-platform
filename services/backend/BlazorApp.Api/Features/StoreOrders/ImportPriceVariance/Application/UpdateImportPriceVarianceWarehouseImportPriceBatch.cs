using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;

internal sealed record UpdateImportPriceVarianceWarehouseImportPriceBatchCommand(
    StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto? Request
);

internal sealed class UpdateImportPriceVarianceWarehouseImportPriceBatchValidator
{
    internal ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceBatchInput> Validate(
        UpdateImportPriceVarianceWarehouseImportPriceBatchCommand command
    )
    {
        var productCodes = ImportPriceVarianceRules.NormalizeSelectedProductCodes(
            command.Request?.ProductCodes ?? new List<string>()
        );
        if (productCodes.Count == 0)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceBatchInput>.Invalid(
                "请选择商品"
            );
        }

        if (productCodes.Count > ImportPriceVarianceRules.WarehouseImportPriceBatchLimit)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceBatchInput>.Invalid(
                $"一次最多选择 {ImportPriceVarianceRules.WarehouseImportPriceBatchLimit} 个商品"
            );
        }

        var requestedPrice = command.Request?.WarehouseImportPrice;
        if (!requestedPrice.HasValue)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceBatchInput>.Invalid(
                "仓库进货价格不能为空"
            );
        }

        if (requestedPrice.Value < 0)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceBatchInput>.Invalid(
                "仓库进货价格不能小于 0"
            );
        }

        return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceBatchInput>.Valid(
            new ImportPriceVarianceWarehouseImportPriceBatchInput(
                productCodes,
                ImportPriceVarianceRules.NormalizePrice(requestedPrice.Value)
            )
        );
    }
}

internal sealed class UpdateImportPriceVarianceWarehouseImportPriceBatchHandler(
    UpdateImportPriceVarianceWarehouseImportPriceBatchValidator validator,
    ImportPriceVarianceCommandStore commandStore,
    IStoreOrderProductCostCoordinator productCostCoordinator,
    ILogger<UpdateImportPriceVarianceWarehouseImportPriceBatchHandler> logger
)
{
    internal async Task<
        ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
    > HandleAsync(UpdateImportPriceVarianceWarehouseImportPriceBatchCommand command)
    {
        var validation = validator.Validate(command);
        if (!validation.IsValid)
        {
            return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
            {
                Success = false,
                Message = validation.ErrorMessage!,
            };
        }

        var input = validation.Value!;
        try
        {
            var result = await commandStore.UpdateWarehouseImportPriceBatchAsync(input);
            if (!result.Success)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
                {
                    Success = false,
                    Message = result.ErrorMessage!,
                };
            }

            return ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>.OK(
                result.Data!
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "UpdateImportPriceVarianceWarehouseImportPriceBatchAsync failed for {ProductCodes}",
                string.Join(", ", input.ProductCodes)
            );
            if (productCostCoordinator.IsBusyConflict(ex))
            {
                return ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    productCostCoordinator.BusyErrorCode
                );
            }

            return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
            {
                Success = false,
                Message = "批量保存仓库进货价格失败",
            };
        }
    }
}
