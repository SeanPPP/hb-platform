using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;

internal sealed record UpdateImportPriceVarianceWarehouseImportPriceCommand(
    StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto? Request
);

internal sealed class UpdateImportPriceVarianceWarehouseImportPriceValidator
{
    internal ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceInput> Validate(
        UpdateImportPriceVarianceWarehouseImportPriceCommand command
    )
    {
        var productCode = command.Request?.ProductCode?.Trim();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceInput>.Invalid(
                "商品编码不能为空"
            );
        }

        var requestedPrice = command.Request?.WarehouseImportPrice;
        if (!requestedPrice.HasValue)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceInput>.Invalid(
                "仓库进货价格不能为空"
            );
        }

        if (requestedPrice.Value < 0)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceInput>.Invalid(
                "仓库进货价格不能小于 0"
            );
        }

        return ImportPriceVarianceValidationResult<ImportPriceVarianceWarehouseImportPriceInput>.Valid(
            new ImportPriceVarianceWarehouseImportPriceInput(
                productCode,
                ImportPriceVarianceRules.NormalizePrice(requestedPrice.Value)
            )
        );
    }
}

internal sealed class UpdateImportPriceVarianceWarehouseImportPriceHandler(
    UpdateImportPriceVarianceWarehouseImportPriceValidator validator,
    ImportPriceVarianceCommandStore commandStore,
    IStoreOrderProductCostCoordinator productCostCoordinator,
    ILogger<UpdateImportPriceVarianceWarehouseImportPriceHandler> logger
)
{
    internal async Task<
        ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
    > HandleAsync(UpdateImportPriceVarianceWarehouseImportPriceCommand command)
    {
        var validation = validator.Validate(command);
        if (!validation.IsValid)
        {
            return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
            {
                Success = false,
                Message = validation.ErrorMessage!,
            };
        }

        var input = validation.Value!;
        try
        {
            var result = await commandStore.UpdateWarehouseImportPriceAsync(input);
            if (!result.Success)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
                {
                    Success = false,
                    Message = result.ErrorMessage!,
                };
            }

            return ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>.OK(
                result.Data!
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "UpdateImportPriceVarianceWarehouseImportPriceAsync failed for {ProductCode}",
                input.ProductCode
            );
            if (productCostCoordinator.IsBusyConflict(ex))
            {
                return ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    productCostCoordinator.BusyErrorCode
                );
            }

            return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
            {
                Success = false,
                Message = "保存仓库进货价格失败",
            };
        }
    }
}
