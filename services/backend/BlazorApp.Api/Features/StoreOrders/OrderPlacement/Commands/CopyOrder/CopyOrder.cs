using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Domain;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.CopyOrder;

internal sealed record CopyOrderCommand(CopyOrderDto? Request);

internal sealed class CopyOrderValidator
{
    internal StoreOrderPlacementValidationFailure? Validate(CopyOrderCommand command)
    {
        if (
            command.Request == null
            || string.IsNullOrWhiteSpace(command.Request.TargetStoreCode)
        )
        {
            return new StoreOrderPlacementValidationFailure("TargetStoreCode is required");
        }

        return string.IsNullOrWhiteSpace(command.Request.SourceOrderGUID)
            ? new StoreOrderPlacementValidationFailure("SourceOrderGUID is required")
            : null;
    }
}

internal sealed class CopyOrderHandler(
    CopyOrderValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderPlacementGateCoordinator gateCoordinator,
    IStoreOrderPlacementOrderStore orderStore,
    IStoreOrderPlacementExecutionContext executionContext,
    IOrderNumberGenerator orderNumberGenerator,
    ILogger<CopyOrderHandler> logger
)
{
    internal async Task<ApiResponse<CopyOrderResultDto>> HandleAsync(
        CopyOrderCommand command
    )
    {
        var validationFailure = validator.Validate(command);
        if (validationFailure != null)
        {
            return StoreOrderPlacementResponses.ValidationFailure<CopyOrderResultDto>(
                validationFailure.Value
            );
        }

        var request = command.Request!;
        var targetStoreCode = request.TargetStoreCode.Trim();
        var bypassPreorderGate = request.BypassPreorderGate
            || ownerScope.IsWarehouseStaffOnly
            || await executionContext.CanBypassPreorderCompletionAsync();
        try
        {
            return await gateCoordinator.ExecuteWithProcessGateAsync(
                targetStoreCode,
                bypassPreorderGate,
                "React.CopyOrder",
                gateContext => orderStore.ExecuteInTransactionAsync(async () =>
                {
                    var gateDecision = await gateCoordinator.IsBlockedInsideTransactionAsync(
                        gateContext,
                        targetStoreCode,
                        "React.CopyOrder"
                    );
                    if (gateDecision.IsBlocked)
                    {
                        return StoreOrderPlacementResponses.PreorderRequired<CopyOrderResultDto>(
                            "请先完成当前有效的 Preorder，再复制普通订单",
                            gateDecision.Details
                        );
                    }

                    var source = await orderStore.GetCopySourceAsync(
                        request.SourceOrderGUID.Trim()
                    );
                    if (source == null)
                    {
                        return new ApiResponse<CopyOrderResultDto>
                        {
                            Success = false,
                            Message = "Source order not found",
                        };
                    }

                    if (source.Details.Count == 0)
                    {
                        return new ApiResponse<CopyOrderResultDto>
                        {
                            Success = false,
                            Message = "Source order has no items",
                        };
                    }

                    var result = await orderStore.InsertCopiedOrderAsync(
                        source,
                        targetStoreCode,
                        request.CopyOrderQuantity,
                        request.CopyAllocQuantity,
                        await orderNumberGenerator.GetNextOrderNoAsync(),
                        executionContext.LocalNow,
                        executionContext.ActorName
                    );
                    return new ApiResponse<CopyOrderResultDto>
                    {
                        Success = true,
                        Data = result,
                    };
                })
            );
        }
        catch (PreorderBusinessException exception)
        {
            logger.LogWarning(exception, "CopyOrder Preorder gate unavailable");
            return new ApiResponse<CopyOrderResultDto>
            {
                Success = false,
                ErrorCode = exception.ErrorCode,
                Message = exception.Message,
                Details = exception.Details,
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CopyOrderAsync failed");
            return new ApiResponse<CopyOrderResultDto>
            {
                Success = false,
                Message = "订单复制失败，请稍后重试",
            };
        }
    }
}
