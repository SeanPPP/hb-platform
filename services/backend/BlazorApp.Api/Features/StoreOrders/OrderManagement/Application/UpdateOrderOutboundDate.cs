using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal sealed record UpdateOrderOutboundDateCommand(UpdateOrderOutboundDateDto? Request);

internal sealed class UpdateOrderOutboundDateValidator
{
    internal StoreOrderManagementValidationResult<UpdateOrderOutboundDateInput> Validate(
        UpdateOrderOutboundDateCommand command
    )
    {
        if (command.Request == null)
        {
            return StoreOrderManagementValidationResult<UpdateOrderOutboundDateInput>.Invalid(
                "Request is required"
            );
        }

        return StoreOrderManagementValidationResult<UpdateOrderOutboundDateInput>.Valid(
            new UpdateOrderOutboundDateInput(
                command.Request.OrderGuid,
                command.Request.OutboundDate,
                command.Request.CompleteOrder
            )
        );
    }
}

internal sealed class UpdateOrderOutboundDateHandler(
    UpdateOrderOutboundDateValidator validator,
    IStoreOrderHeaderCommandStore commandStore,
    ILogger<UpdateOrderOutboundDateHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(
        UpdateOrderOutboundDateCommand command
    )
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
                await commandStore.UpdateOrderOutboundDateAsync(validation.Value!)
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateOrderOutboundDateAsync failed");
            return new ApiResponse<bool> { Success = false, Message = ex.Message };
        }
    }
}
