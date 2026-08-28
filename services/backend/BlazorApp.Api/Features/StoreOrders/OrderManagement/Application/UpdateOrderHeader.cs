using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal sealed record UpdateOrderHeaderCommand(UpdateOrderHeaderDto? Request);

internal sealed class UpdateOrderHeaderValidator
{
    internal StoreOrderManagementValidationResult<UpdateOrderHeaderInput> Validate(
        UpdateOrderHeaderCommand command
    )
    {
        if (command.Request == null)
        {
            return StoreOrderManagementValidationResult<UpdateOrderHeaderInput>.Invalid(
                "Request is required"
            );
        }

        return StoreOrderManagementValidationResult<UpdateOrderHeaderInput>.Valid(
            new UpdateOrderHeaderInput(
                command.Request.OrderGuid,
                command.Request.Remarks,
                command.Request.ShippingFee,
                command.Request.OrderDate,
                command.Request.StoreCode
            )
        );
    }
}

internal sealed class UpdateOrderHeaderHandler(
    UpdateOrderHeaderValidator validator,
    IStoreOrderHeaderCommandStore commandStore,
    ILogger<UpdateOrderHeaderHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(UpdateOrderHeaderCommand command)
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
                await commandStore.UpdateOrderHeaderAsync(validation.Value!)
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateOrderHeaderAsync failed");
            return new ApiResponse<bool> { Success = false, Message = ex.Message };
        }
    }
}
