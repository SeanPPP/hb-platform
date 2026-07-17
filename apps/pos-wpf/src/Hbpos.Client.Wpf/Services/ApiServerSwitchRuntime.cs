using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public sealed record ApiServerPaymentSafetyState(
    bool IsCardPaymentInProgress,
    bool IsPaymentInteractionLocked,
    int PaymentTenderCount)
{
    public static ApiServerPaymentSafetyState Safe { get; } = new(false, false, 0);
}

internal sealed class ApiServerSwitchRuntime(
    PosCartService cart,
    ISyncQueueRepository syncQueueRepository,
    ClientLogOutboxStore logOutboxStore,
    ClientLogOutboxWriter logOutboxWriter,
    LocalSqliteStore localStore,
    ApiRuntimeEndpointState endpointState,
    IOperationAuthorizationService operationAuthorizationService,
    ICashierSessionContext cashierSessionContext) : IApiServerSwitchRuntime
{
    private Func<ApiServerPaymentSafetyState> _getPaymentState = () => ApiServerPaymentSafetyState.Safe;
    private Func<CancellationToken, Task> _postCommitAsync = _ => Task.CompletedTask;
    private Action<bool> _setSwitching = _ => { };

    public void ConfigureShell(
        Func<ApiServerPaymentSafetyState> getPaymentState,
        Func<CancellationToken, Task> postCommitAsync,
        Action<bool> setSwitching)
    {
        _getPaymentState = getPaymentState ?? throw new ArgumentNullException(nameof(getPaymentState));
        _postCommitAsync = postCommitAsync ?? throw new ArgumentNullException(nameof(postCommitAsync));
        _setSwitching = setSwitching ?? throw new ArgumentNullException(nameof(setSwitching));
    }

    public async Task<ApiServerSwitchSafetySnapshot> GetSafetySnapshotAsync(CancellationToken cancellationToken)
    {
        var payment = _getPaymentState();
        var sync = await syncQueueRepository.GetOverviewAsync(cancellationToken);
        var pendingAudits = await GetPendingAuditCountAsync(cancellationToken);
        return CreateSnapshot(payment, sync.PendingCount, sync.FailedCount, sync.SyncingCount, pendingAudits);
    }

    public async Task<object> PrepareAsync(string targetAddress, CancellationToken cancellationToken)
    {
        var prepared = await localStore.PrepareSwitchAsync(targetAddress, cancellationToken);
        // 先在目标分区完成建表和迁移，全部成功后才允许进入发布边界。
        var targetStore = new LocalSqliteStore(prepared.TargetDatabasePath);
        await new LocalSchemaService(targetStore).InitializeAsync(cancellationToken);
        return prepared;
    }

    public async Task<object> BeginTransitionAsync(
        string targetAddress,
        object preparedSwitch,
        CancellationToken cancellationToken)
    {
        var prepared = GetPreparedSwitch(preparedSwitch);
        _setSwitching(true);
        ApiEndpointTransition? endpointTransition = null;
        try
        {
            endpointTransition = await endpointState.BeginTransitionAsync(targetAddress, cancellationToken);
            var auditRevision = logOutboxWriter.CaptureOperationAuditRevision();
            // HTTP 和界面入口封闭后，先保证内存审计全部落入 outbox，再封闭本地数据库。
            await logOutboxWriter.WaitForOperationAuditFlushAsync(cancellationToken);
            var databaseTransition = await localStore.BeginTransitionAsync(prepared, cancellationToken);
            return new ApiServerRuntimeTransition(endpointTransition, databaseTransition, auditRevision);
        }
        catch
        {
            if (endpointTransition is not null)
            {
                endpointState.Abort(endpointTransition);
            }

            _setSwitching(false);
            throw;
        }
    }

    public async Task<ApiServerSwitchSafetySnapshot> GetFinalSafetySnapshotAsync(
        object transition,
        CancellationToken cancellationToken)
    {
        var runtimeTransition = GetTransition(transition);
        var payment = _getPaymentState();
        await using var connection = await localStore.OpenTransitionConnectionAsync(
            runtimeTransition.DatabaseTransition,
            cancellationToken);
        var (pending, failed, syncing) = await ReadSyncCountsAsync(connection, cancellationToken);
        var pendingAudits = await GetPendingAuditCountAsync(cancellationToken);
        return CreateSnapshot(payment, pending, failed, syncing, pendingAudits);
    }

    public bool Commit(object transition)
    {
        var runtimeTransition = GetTransition(transition);
        var published = logOutboxWriter.TryPublishForOperationAuditRevision(
            runtimeTransition.AuditRevision,
            () =>
            {
                // 两个目标值在审计 revision 锁内同步发布，Record 无法落在旧库与新端点之间。
                localStore.Publish(runtimeTransition.DatabaseTransition);
                endpointState.Publish(runtimeTransition.EndpointTransition);
            });
        if (!published)
        {
            return false;
        }

        endpointState.Complete(runtimeTransition.EndpointTransition);
        localStore.Complete(runtimeTransition.DatabaseTransition);
        return true;
    }

    public void Abort(object transition)
    {
        var runtimeTransition = GetTransition(transition);
        try
        {
            localStore.Abort(runtimeTransition.DatabaseTransition);
        }
        finally
        {
            endpointState.Abort(runtimeTransition.EndpointTransition);
            _setSwitching(false);
        }
    }

    public async Task PostCommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 提交后永久清除旧服务器身份和临时提权，失败时也不允许恢复旧会话。
            operationAuthorizationService.Cancel();
            operationAuthorizationService.RevokeAll();
            cashierSessionContext.Clear();
            await _postCommitAsync(cancellationToken);
        }
        finally
        {
            _setSwitching(false);
        }
    }

    private async Task<int> GetPendingAuditCountAsync(CancellationToken cancellationToken)
    {
        var persisted = await logOutboxStore.CountPendingAsync(
            ClientLogOutboxKind.OperationAudit,
            cancellationToken);
        return checked(persisted + (int)logOutboxWriter.PendingOperationAuditPersistenceCount);
    }

    private ApiServerSwitchSafetySnapshot CreateSnapshot(
        ApiServerPaymentSafetyState payment,
        int pending,
        int failed,
        int syncing,
        int pendingAudits)
    {
        return new ApiServerSwitchSafetySnapshot(
            cart.Lines.Count,
            payment.IsCardPaymentInProgress,
            payment.IsPaymentInteractionLocked,
            payment.PaymentTenderCount,
            pending,
            failed,
            syncing,
            pendingAudits);
    }

    private static async Task<(int Pending, int Failed, int Syncing)> ReadSyncCountsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END),
                SUM(CASE WHEN Status = 'Syncing' THEN 1 ELSE 0 END)
            FROM SyncQueue;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0, 0);
        }

        return (
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt32(2));
    }

    private static LocalDatabaseSwitch GetPreparedSwitch(object preparedSwitch)
    {
        return preparedSwitch as LocalDatabaseSwitch ??
            throw new ArgumentException("数据库切换准备令牌无效。", nameof(preparedSwitch));
    }

    private static ApiServerRuntimeTransition GetTransition(object transition)
    {
        return transition as ApiServerRuntimeTransition ??
            throw new ArgumentException("服务器切换令牌无效。", nameof(transition));
    }

    private sealed record ApiServerRuntimeTransition(
        ApiEndpointTransition EndpointTransition,
        LocalDatabaseTransition DatabaseTransition,
        long AuditRevision);
}
