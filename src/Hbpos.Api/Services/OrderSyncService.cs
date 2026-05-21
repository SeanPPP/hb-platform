using BlazorApp.Shared.Models.POSM;
using Hbpos.Api.Data;
using Hbpos.Contracts.Orders;

namespace Hbpos.Api.Services;

public interface IOrderSyncService
{
    Task<OrderSyncResponse> SyncAsync(OrderSyncRequest request, CancellationToken cancellationToken);
}

public sealed class OrderSyncService(
    IOrderRepository repository,
    IOrderSyncPlanner planner) : IOrderSyncService
{
    public async Task<OrderSyncResponse> SyncAsync(
        OrderSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrderGuid, cancellationToken))
        {
            return new OrderSyncResponse(request.OrderGuid, true, true, "AlreadySynced");
        }

        var plan = planner.CreatePlan(request);
        await repository.InsertAsync(plan, cancellationToken);

        return new OrderSyncResponse(request.OrderGuid, true, false, "Synced");
    }
}

public interface IOrderRepository
{
    Task<bool> ExistsAsync(Guid orderGuid, CancellationToken cancellationToken);

    Task InsertAsync(OrderSyncPlan plan, CancellationToken cancellationToken);
}

public sealed class SqlSugarOrderRepository(HbposSqlSugarContext dbContext) : IOrderRepository
{
    public async Task<bool> ExistsAsync(Guid orderGuid, CancellationToken cancellationToken)
    {
        var orderGuidText = orderGuid.ToString("D");
        return await dbContext.PosmDb.Queryable<SalesOrder>()
            .AnyAsync(x => x.OrderGuid == orderGuidText, cancellationToken);
    }

    public async Task InsertAsync(OrderSyncPlan plan, CancellationToken cancellationToken)
    {
        var db = dbContext.PosmDb;
        await db.Ado.BeginTranAsync();
        try
        {
            var existing = await db.Queryable<SalesOrder>()
                .AnyAsync(x => x.OrderGuid == plan.Order.OrderGuid, cancellationToken);

            if (existing)
            {
                await db.Ado.CommitTranAsync();
                return;
            }

            await db.Insertable(plan.Order).ExecuteCommandAsync(cancellationToken);
            if (plan.Lines.Count > 0)
            {
                await db.Insertable(plan.Lines.ToList()).ExecuteCommandAsync(cancellationToken);
            }

            if (plan.Payments.Count > 0)
            {
                await db.Insertable(plan.Payments.ToList()).ExecuteCommandAsync(cancellationToken);
            }

            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }
}
