using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal sealed record RemoveOrderLineCommand(RemoveOrderLineDto? Request);

internal sealed class RemoveOrderLineValidator
{
    internal StoreOrderManagementValidationResult<RemoveOrderLineInput> Validate(
        RemoveOrderLineCommand command
    )
    {
        if (command.Request == null)
        {
            return StoreOrderManagementValidationResult<RemoveOrderLineInput>.Invalid(
                "Request is required"
            );
        }

        return StoreOrderManagementValidationResult<RemoveOrderLineInput>.Valid(
            new RemoveOrderLineInput(
                command.Request.OrderGUID,
                command.Request.DetailGUID
            )
        );
    }
}

internal sealed class RemoveOrderLineHandler(
    RemoveOrderLineValidator validator,
    IStoreOrderLineCommandStore commandStore,
    ILogger<RemoveOrderLineHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(RemoveOrderLineCommand command)
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
                await commandStore.RemoveOrderLineAsync(validation.Value!)
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RemoveOrderLineAsync failed");
            return new ApiResponse<bool> { Success = false, Message = ex.Message };
        }
    }
}
