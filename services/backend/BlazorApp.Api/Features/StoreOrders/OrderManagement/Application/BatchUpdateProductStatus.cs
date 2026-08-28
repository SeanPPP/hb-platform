using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal sealed record BatchUpdateProductStatusCommand(BatchUpdateProductStatusDto? Request);

internal sealed class BatchUpdateProductStatusValidator
{
    internal StoreOrderManagementValidationResult<BatchUpdateProductStatusInput> Validate(
        BatchUpdateProductStatusCommand command
    )
    {
        if (command.Request == null)
        {
            return StoreOrderManagementValidationResult<BatchUpdateProductStatusInput>.Invalid(
                "Request is required"
            );
        }

        var productCodes = (command.Request.ProductCodes ?? new List<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return StoreOrderManagementValidationResult<BatchUpdateProductStatusInput>.Valid(
            new BatchUpdateProductStatusInput(
                productCodes,
                command.Request.IsActive
            )
        );
    }
}

internal sealed class BatchUpdateProductStatusHandler(
    BatchUpdateProductStatusValidator validator,
    IStoreOrderProductStatusCommandStore commandStore,
    ILogger<BatchUpdateProductStatusHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(
        BatchUpdateProductStatusCommand command
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
                await commandStore.BatchUpdateProductStatusAsync(validation.Value!)
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BatchUpdateProductStatusAsync failed");
            return new ApiResponse<bool> { Success = false, Message = ex.Message };
        }
    }
}
