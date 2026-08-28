using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;

internal sealed class SqlSugarStoreOrderHeaderCommandStore(
    SqlSugarContext context,
    StoreOrderTransactionExecutor transactionExecutor,
    StoreOrderManagementPersistence persistence,
    IStoreOrderActorContext actorContext
) : IStoreOrderHeaderCommandStore
{
    private readonly ISqlSugarClient _db = context.Db;

    public Task<StoreOrderManagementResult<bool>> UpdateOrderHeaderAsync(
        UpdateOrderHeaderInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetEditableOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail(
                    "Order not found or not editable"
                );
            }

            order.Remarks = input.Remarks;
            order.ShippingFee = input.ShippingFee;
            if (input.OrderDate.HasValue)
            {
                order.OrderDate = input.OrderDate.Value;
            }

            if (
                !string.IsNullOrEmpty(input.StoreCode)
                && order.StoreCode != input.StoreCode
            )
            {
                order.StoreCode = input.StoreCode;
                await _db.Updateable<WareHouseOrderDetails>()
                    .SetColumns(detail => detail.StoreCode == input.StoreCode)
                    .Where(detail => detail.OrderGUID == input.OrderGuid)
                    .ExecuteCommandAsync();
            }

            order.UpdatedAt = DateTime.Now;
            order.UpdatedBy = actorContext.ActorName;
            await _db.Updateable(order)
                .UpdateColumns(item => new
                {
                    item.Remarks,
                    item.ShippingFee,
                    item.OrderDate,
                    item.StoreCode,
                    item.UpdatedAt,
                    item.UpdatedBy,
                })
                .ExecuteCommandAsync();

            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<bool>> UpdateOrderOutboundDateAsync(
        UpdateOrderOutboundDateInput input
    )
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail("Order not found");
            }

            order.OutboundDate = input.OutboundDate;
            if (input.CompleteOrder)
            {
                if (order.FlowStatus != 1 && order.FlowStatus != 3)
                {
                    return StoreOrderManagementResult<bool>.Fail(
                        "只有已提交或配货中状态的订单才能标记为完成"
                    );
                }

                order.FlowStatus = 2;
            }

            order.UpdatedAt = DateTime.Now;
            order.UpdatedBy = actorContext.ActorName;
            await _db.Updateable(order)
                .UpdateColumns(item => new
                {
                    item.OutboundDate,
                    item.FlowStatus,
                    item.UpdatedAt,
                    item.UpdatedBy,
                })
                .ExecuteCommandAsync();

            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }

    public Task<StoreOrderManagementResult<bool>> DeleteOrderAsync(DeleteOrderInput input)
    {
        return transactionExecutor.ExecuteAsync(async () =>
        {
            var order = await persistence.GetOrderAsync(input.OrderGuid);
            if (order == null)
            {
                return StoreOrderManagementResult<bool>.Fail("Order not found");
            }

            order.IsDeleted = true;
            order.UpdatedBy = actorContext.ActorName;
            order.UpdatedAt = DateTime.Now;
            await _db.Updateable(order).ExecuteCommandAsync();
            await _db.Updateable<WareHouseOrderDetails>()
                .SetColumns(detail => new WareHouseOrderDetails
                {
                    IsDeleted = true,
                    UpdatedBy = actorContext.ActorName,
                    UpdatedAt = DateTime.Now,
                })
                .Where(detail => detail.OrderGUID == input.OrderGuid)
                .ExecuteCommandAsync();

            return StoreOrderManagementResult<bool>.Ok(true);
        });
    }
}
