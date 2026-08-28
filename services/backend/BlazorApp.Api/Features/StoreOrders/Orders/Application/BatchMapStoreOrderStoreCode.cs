using BlazorApp.Api.Features.StoreOrders.Orders.Domain;
using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record BatchMapStoreOrderStoreCodeCommand(
    BatchMapStoreOrderStoreCodeDto? Request
);

internal sealed class BatchMapStoreOrderStoreCodeValidator
{
    internal StoreOrderOrdersValidationResult<BatchMapStoreOrderStoreCodeInput> Validate(
        BatchMapStoreOrderStoreCodeCommand command
    )
    {
        return StoreOrderOrdersRules.NormalizeMappings(command.Request);
    }
}

internal sealed class BatchMapStoreOrderStoreCodeHandler(
    BatchMapStoreOrderStoreCodeValidator validator,
    StoreOrderCommandStore commandStore,
    ILogger<BatchMapStoreOrderStoreCodeHandler> logger
)
{
    internal async Task<ApiResponse<BatchMapStoreOrderStoreCodeResultDto>> HandleAsync(
        BatchMapStoreOrderStoreCodeCommand command
    )
    {
        var validation = validator.Validate(command);
        if (!validation.IsValid)
        {
            return ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.Error(
                validation.ErrorMessage!
            );
        }

        try
        {
            var result = await commandStore.BatchMapStoreOrderStoreCodeAsync(
                validation.Value!
            );
            return result.Success
                ? ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.OK(
                    result.Data!,
                    $"已修复 {result.Data!.UpdatedCount} 张订单"
                )
                : ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.Error(
                    result.ErrorMessage!,
                    result.ErrorCode
                );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BatchMapStoreOrderStoreCodeAsync failed");
            return ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.Error(ex.Message);
        }
    }
}
