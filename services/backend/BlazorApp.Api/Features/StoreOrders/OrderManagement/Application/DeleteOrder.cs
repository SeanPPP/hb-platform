using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal sealed record DeleteOrderCommand(string? OrderGuid);

internal sealed class DeleteOrderValidator
{
    internal StoreOrderManagementValidationResult<DeleteOrderInput> Validate(
        DeleteOrderCommand command
    )
    {
        return StoreOrderManagementValidationResult<DeleteOrderInput>.Valid(
            new DeleteOrderInput(command.OrderGuid ?? string.Empty)
        );
    }
}

internal sealed class DeleteOrderHandler(
    DeleteOrderValidator validator,
    IStoreOrderHeaderCommandStore commandStore,
    ILogger<DeleteOrderHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(DeleteOrderCommand command)
    {
        var validation = validator.Validate(command);
        try
        {
            return StoreOrderManagementResponseMapper.Map(
                await commandStore.DeleteOrderAsync(validation.Value!)
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DeleteOrderAsync failed");
            return new ApiResponse<bool> { Success = false, Message = ex.Message };
        }
    }
}
