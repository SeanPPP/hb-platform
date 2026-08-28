using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal sealed record RefreshOrderLineImportPricesCommand(
    RefreshStoreOrderImportPricesDto? Request
);

internal sealed class RefreshOrderLineImportPricesValidator
{
    internal StoreOrderManagementValidationResult<RefreshOrderLineImportPricesInput> Validate(
        RefreshOrderLineImportPricesCommand command
    )
    {
        var orderGuid = command.Request?.OrderGUID?.Trim();
        if (string.IsNullOrWhiteSpace(orderGuid))
        {
            return StoreOrderManagementValidationResult<RefreshOrderLineImportPricesInput>.Invalid(
                "OrderGUID is required"
            );
        }

        var detailGuids = (command.Request?.DetailGUIDs ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return StoreOrderManagementValidationResult<RefreshOrderLineImportPricesInput>.Valid(
            new RefreshOrderLineImportPricesInput(orderGuid, detailGuids)
        );
    }
}

internal sealed class RefreshOrderLineImportPricesHandler(
    RefreshOrderLineImportPricesValidator validator,
    IStoreOrderLineCommandStore commandStore,
    ILogger<RefreshOrderLineImportPricesHandler> logger
)
{
    internal async Task<ApiResponse<RefreshStoreOrderImportPricesResultDto>> HandleAsync(
        RefreshOrderLineImportPricesCommand command
    )
    {
        var validation = validator.Validate(command);
        if (!validation.IsValid)
        {
            return StoreOrderManagementResponseMapper.ValidationError<RefreshStoreOrderImportPricesResultDto>(
                validation.ErrorMessage!
            );
        }

        try
        {
            var result = await commandStore.RefreshOrderLineImportPricesAsync(
                validation.Value!
            );
            if (!result.Success)
            {
                return StoreOrderManagementResponseMapper.ValidationError<RefreshStoreOrderImportPricesResultDto>(
                    result.ErrorMessage!
                );
            }

            var data = result.Data!;
            return ApiResponse<RefreshStoreOrderImportPricesResultDto>.OK(
                new RefreshStoreOrderImportPricesResultDto
                {
                    UpdatedCount = data.UpdatedCount,
                    UnchangedCount = data.UnchangedCount,
                    SkippedCount = data.SkippedCount,
                    MissingWarehousePriceCount = data.MissingWarehousePriceCount,
                }
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RefreshOrderLineImportPricesAsync failed");
            return new ApiResponse<RefreshStoreOrderImportPricesResultDto>
            {
                Success = false,
                Message = ex.Message,
            };
        }
    }
}
