using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Queries;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Infrastructure;

internal sealed class SqlSugarStoreOrderLifecyclePersistence(SqlSugarContext context)
    : IStoreOrderLifecycleQueryHandler,
        IStoreOrderLifecycleCommandStore
{
    private readonly ISqlSugarClient _db = context.Db;

    public async Task<StoreOrderLifecycleSnapshot?> HandleAsync(
        GetStoreOrderLifecycleQuery query
    )
    {
        // Query 自身不开事务；批量命令调用时复用命令已经建立的唯一事务边界。
        var order = await _db.Queryable<WareHouseOrder>()
            .Where(candidate => candidate.OrderGUID == query.OrderGuid && !candidate.IsDeleted)
            .FirstAsync();
        return order == null
            ? null
            : new StoreOrderLifecycleSnapshot(order.OrderGUID, order.FlowStatus);
    }

    public async Task<IReadOnlyList<StoreOrderLifecycleSnapshot>> HandleAsync(
        GetStoreOrderLifecyclesQuery query
    )
    {
        var orderGuids = query.OrderGuids.ToList();
        var orders = await _db.Queryable<WareHouseOrder>()
            .Where(candidate => orderGuids.Contains(candidate.OrderGUID) && !candidate.IsDeleted)
            .ToListAsync();
        return orders
            .Select(order => new StoreOrderLifecycleSnapshot(order.OrderGUID, order.FlowStatus))
            .ToList();
    }

    public Task<int> CompareExchangeStatusAsync(
        string orderGuid,
        int? expectedStatus,
        int targetStatus,
        DateTime updatedAt,
        string updatedBy
    )
    {
        return _db.Updateable<WareHouseOrder>()
            .SetColumns(order => new WareHouseOrder
            {
                FlowStatus = targetStatus,
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy,
            })
            .Where(order =>
                order.OrderGUID == orderGuid
                && !order.IsDeleted
                && order.FlowStatus == expectedStatus
            )
            .ExecuteCommandAsync();
    }

    public Task<int> CompareExchangeStatusGroupAsync(
        IReadOnlyList<string> orderGuids,
        int? expectedStatus,
        int targetStatus,
        DateTime updatedAt,
        string updatedBy
    )
    {
        var groupOrderGuids = orderGuids.ToList();
        return _db.Updateable<WareHouseOrder>()
            .SetColumns(order => new WareHouseOrder
            {
                FlowStatus = targetStatus,
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy,
            })
            .Where(order =>
                groupOrderGuids.Contains(order.OrderGUID)
                && !order.IsDeleted
                && order.FlowStatus == expectedStatus
            )
            .ExecuteCommandAsync();
    }

    public async Task<StoreOrderLifecycleTransactionResult> ExecuteInTransactionAsync(
        Func<Task> command
    )
    {
        var transaction = await _db.Ado.UseTranAsync(command);
        return new StoreOrderLifecycleTransactionResult(
            transaction.IsSuccess,
            transaction.ErrorException
        );
    }
}
