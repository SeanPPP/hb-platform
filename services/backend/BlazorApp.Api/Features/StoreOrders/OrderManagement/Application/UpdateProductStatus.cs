using BlazorApp.Api.Features.SupplyNotices;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal sealed record UpdateProductStatusCommand(UpdateProductStatusDto? Request);

internal sealed class UpdateProductStatusValidator
{
    internal StoreOrderManagementValidationResult<UpdateProductStatusInput> Validate(
        UpdateProductStatusCommand command
    )
    {
        var productCode = command.Request?.ProductCode?.Trim();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return StoreOrderManagementValidationResult<UpdateProductStatusInput>.Invalid(
                "Product code is required"
            );
        }

        // 供货说明只在下架时有意义；录入有误在进入事务前拒绝。
        NormalizedSupplyNotice? supplyNotice = null;
        if (!command.Request!.IsActive && command.Request.SupplyNotice != null)
        {
            var (normalizedNotice, noticeError) = WarehouseProductSupplyNoticeRules.Normalize(
                command.Request.SupplyNotice
            );
            if (noticeError != null)
            {
                return StoreOrderManagementValidationResult<UpdateProductStatusInput>.Invalid(
                    noticeError
                );
            }
            supplyNotice = normalizedNotice;
        }

        return StoreOrderManagementValidationResult<UpdateProductStatusInput>.Valid(
            new UpdateProductStatusInput(productCode, command.Request.IsActive, supplyNotice)
        );
    }
}

internal sealed class UpdateProductStatusHandler(
    UpdateProductStatusValidator validator,
    IStoreOrderProductStatusCommandStore commandStore,
    ILogger<UpdateProductStatusHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(UpdateProductStatusCommand command)
    {
        var validation = validator.Validate(command);
        if (!validation.IsValid)
        {
            return StoreOrderManagementResponseMapper.ValidationError<bool>(
                validation.ErrorMessage!
            );
        }

        try
        {
            return StoreOrderManagementResponseMapper.Map(
                await commandStore.UpdateProductStatusAsync(validation.Value!)
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateProductStatusAsync failed");
            return new ApiResponse<bool> { Success = false, Message = ex.Message };
        }
    }
}
