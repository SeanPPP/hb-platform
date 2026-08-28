using System.Collections.Concurrent;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Common;

internal sealed class StoreOrderCartCommandCoordinator(SqlSugarContext context)
    : IStoreOrderCartCommandCoordinator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new();
    private static readonly AsyncLocal<HashSet<string>?> HeldProcessLocks = new();
    private readonly ISqlSugarClient _db = context.Db;

    public async Task<ApiResponse<T>> ExecuteAsync<T>(
        StoreOrderCartScope scope,
        Func<Task<ApiResponse<T>>> command
    )
    {
        var lockKey = StoreOrderCartRules.NormalizeLockKey(scope);
        if (HeldProcessLocks.Value?.Contains(lockKey) == true)
        {
            return await command();
        }

        var gate = ProcessLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        var previousLocks = HeldProcessLocks.Value;
        var currentLocks = previousLocks == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(previousLocks, StringComparer.OrdinalIgnoreCase);
        currentLocks.Add(lockKey);
        HeldProcessLocks.Value = currentLocks;

        try
        {
            var transactionStarted = false;
            try
            {
                await _db.Ado.BeginTranAsync();
                transactionStarted = true;
                await AcquireDatabaseLockAsync(scope);

                var response = await command();
                if (!response.Success)
                {
                    // 业务失败与异常同样回滚，避免留下先创建的空购物车或半成品明细。
                    await _db.Ado.RollbackTranAsync();
                    transactionStarted = false;
                    return response;
                }

                await _db.Ado.CommitTranAsync();
                transactionStarted = false;
                return response;
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
        finally
        {
            HeldProcessLocks.Value = previousLocks;
            gate.Release();
        }
    }

    private async Task AcquireDatabaseLockAsync(StoreOrderCartScope scope)
    {
        if (_db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return;
        }

        var resource = $"StoreOrderCart:{StoreOrderCartRules.NormalizeLockKey(scope)}";
        var lockResult = await _db.Ado.SqlQuerySingleAsync<int>(
            """
            DECLARE @Result INT;
            EXEC @Result = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 2000;
            SELECT @Result;
            """,
            new SugarParameter("@Resource", resource)
        );

        if (lockResult < 0)
        {
            throw new InvalidOperationException("购物车正在更新，请稍后重试");
        }
    }
}
