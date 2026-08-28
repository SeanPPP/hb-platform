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

        return StoreOrderManagementValidationResult<UpdateProductStatusInput>.Valid(
            new UpdateProductStatusInput(productCode, command.Request!.IsActive)
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
