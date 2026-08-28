using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Domain;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.CreateOrder;

internal sealed record CreateOrderCommand(CreateStoreOrderDto? Request);

internal sealed class CreateOrderValidator
{
    internal StoreOrderPlacementValidationFailure? Validate(CreateOrderCommand command)
    {
        return command.Request == null || string.IsNullOrWhiteSpace(command.Request.StoreCode)
            ? new StoreOrderPlacementValidationFailure("StoreCode is required")
            : null;
    }
}

internal sealed class CreateOrderHandler(
    CreateOrderValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderPlacementGateCoordinator gateCoordinator,
    IStoreOrderPlacementOrderStore orderStore,
    IStoreOrderPlacementExecutionContext executionContext,
    IOrderNumberGenerator orderNumberGenerator,
    ILogger<CreateOrderHandler> logger
)
{
    internal async Task<ApiResponse<string>> HandleAsync(CreateOrderCommand command)
    {
        var validationFailure = validator.Validate(command);
        if (validationFailure != null)
        {
            return StoreOrderPlacementResponses.ValidationFailure<string>(
                validationFailure.Value
            );
        }

        var request = command.Request!;
        var storeCode = request.StoreCode.Trim();
        var bypassPreorderGate = request.BypassPreorderGate
            || ownerScope.IsWarehouseStaffOnly
            || await executionContext.CanBypassPreorderCompletionAsync();
        try
        {
            return await gateCoordinator.ExecuteWithProcessGateAsync(
                storeCode,
                bypassPreorderGate,
                "React.CreateOrder",
                gateContext => orderStore.ExecuteInTransactionAsync(async () =>
                {
                    var gateDecision = await gateCoordinator.IsBlockedInsideTransactionAsync(
                        gateContext,
                        storeCode,
                        "React.CreateOrder"
                    );
                    if (gateDecision.IsBlocked)
                    {
                        return StoreOrderPlacementResponses.PreorderRequired<string>(
                            "请先完成当前有效的 Preorder，再创建普通订单",
                            gateDecision.Details
                        );
                    }

                    var orderGuid = await orderStore.InsertCreatedOrderAsync(
                        storeCode,
                        request.Remarks,
                        await orderNumberGenerator.GetNextOrderNoAsync(),
                        executionContext.LocalNow,
                        executionContext.ActorName
                    );
                    return new ApiResponse<string>
                    {
                        Success = true,
                        Data = orderGuid,
                    };
                })
            );
        }
        catch (PreorderBusinessException exception)
        {
            logger.LogWarning(exception, "CreateOrder Preorder gate unavailable");
            return new ApiResponse<string>
            {
                Success = false,
                ErrorCode = exception.ErrorCode,
                Message = exception.Message,
                Details = exception.Details,
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CreateOrderAsync failed");
            return new ApiResponse<string>
            {
                Success = false,
                Message = "订单创建失败，请稍后重试",
            };
        }
    }
}
