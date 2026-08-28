using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;

internal sealed class StoreOrderTransactionExecutor(SqlSugarContext context)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<StoreOrderManagementResult<T>> ExecuteAsync<T>(
        Func<Task<StoreOrderManagementResult<T>>> command
    )
    {
        var transactionStarted = false;
        try
        {
            await _db.Ado.BeginTranAsync();
            transactionStarted = true;

            var result = await command();
            if (result.Success)
            {
                await _db.Ado.CommitTranAsync();
            }
            else
            {
                await _db.Ado.RollbackTranAsync();
            }

            transactionStarted = false;
            return result;
        }
        catch
        {
            if (transactionStarted)
            {
                await _db.Ado.RollbackTranAsync();
            }

            throw;
        }
    }
}
