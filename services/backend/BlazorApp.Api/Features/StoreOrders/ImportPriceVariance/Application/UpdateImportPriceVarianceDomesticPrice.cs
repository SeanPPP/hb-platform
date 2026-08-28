using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;

internal sealed record UpdateImportPriceVarianceDomesticPriceCommand(
    StoreOrderImportPriceVarianceDomesticPriceUpdateDto? Request
);

internal sealed class UpdateImportPriceVarianceDomesticPriceValidator
{
    internal ImportPriceVarianceValidationResult<ImportPriceVarianceDomesticPriceInput> Validate(
        UpdateImportPriceVarianceDomesticPriceCommand command
    )
    {
        var productCode = command.Request?.ProductCode?.Trim();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceDomesticPriceInput>.Invalid(
                "商品编码不能为空"
            );
        }

        if (!command.Request!.DomesticPrice.HasValue)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceDomesticPriceInput>.Invalid(
                "国内价格不能为空"
            );
        }

        if (command.Request.DomesticPrice.Value < 0)
        {
            return ImportPriceVarianceValidationResult<ImportPriceVarianceDomesticPriceInput>.Invalid(
                "国内价格不能小于 0"
            );
        }

        return ImportPriceVarianceValidationResult<ImportPriceVarianceDomesticPriceInput>.Valid(
            new ImportPriceVarianceDomesticPriceInput(
                productCode,
                ImportPriceVarianceRules.NormalizePrice(command.Request.DomesticPrice.Value)
            )
        );
    }
}

internal sealed class UpdateImportPriceVarianceDomesticPriceHandler(
    UpdateImportPriceVarianceDomesticPriceValidator validator,
    ImportPriceVarianceCommandStore commandStore,
    ILogger<UpdateImportPriceVarianceDomesticPriceHandler> logger
)
{
    internal async Task<
        ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
    > HandleAsync(UpdateImportPriceVarianceDomesticPriceCommand command)
    {
        var validation = validator.Validate(command);
        if (!validation.IsValid)
        {
            return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
            {
                Success = false,
                Message = validation.ErrorMessage!,
            };
        }

        var input = validation.Value!;
        try
        {
            var result = await commandStore.UpdateDomesticPriceAsync(input);
            if (!result.Success)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
                {
                    Success = false,
                    Message = result.ErrorMessage!,
                };
            }

            return ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>.OK(
                result.Data!
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "UpdateImportPriceVarianceDomesticPriceAsync failed for {ProductCode}",
                input.ProductCode
            );
            return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
            {
                Success = false,
                Message = "保存国内价格失败",
            };
        }
    }
}
