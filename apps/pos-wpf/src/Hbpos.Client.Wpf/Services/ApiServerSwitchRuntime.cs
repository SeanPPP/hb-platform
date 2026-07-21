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
    ClientLogOutboxWriter logOutboxWriter,
    OperationAuditUploadService operationAuditUploadService,
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

    public Task<ApiServerSwitchSafetySnapshot> GetSafetySnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payment = _getPaymentState();
        return Task.FromResult(CreateSnapshot(payment));
    }

    public Task<object> PrepareAsync(string targetAddress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<object>(targetAddress);
    }

    public async Task<object> BeginTransitionAsync(
        string targetAddress,
        object preparedSwitch,
        CancellationToken cancellationToken)
    {
        _ = preparedSwitch as string ?? throw new ArgumentException("服务器切换准备令牌无效。", nameof(preparedSwitch));
        _setSwitching(true);
        ApiEndpointTransition? endpointTransition = null;
        try
        {
            endpointTransition = await endpointState.BeginTransitionAsync(targetAddress, cancellationToken);
            // HTTP 和界面入口封闭后，先保证内存审计全部落入固定 outbox。
            await logOutboxWriter.WaitForOperationAuditFlushAsync(cancellationToken);
            return new ApiServerRuntimeTransition(endpointTransition);
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

    public Task<ApiServerSwitchSafetySnapshot> GetFinalSafetySnapshotAsync(
        object transition,
        CancellationToken cancellationToken)
    {
        _ = GetTransition(transition);
        cancellationToken.ThrowIfCancellationRequested();
        var payment = _getPaymentState();
        return Task.FromResult(CreateSnapshot(payment));
    }

    public bool Commit(object transition)
    {
        var runtimeTransition = GetTransition(transition);
        // 只在审计写入边界锁内发布 HTTP 端点，不再因新审计事件拒绝切换。
        logOutboxWriter.PublishWithinOperationAuditBoundary(
            () => endpointState.Publish(runtimeTransition.EndpointTransition));
        endpointState.Complete(runtimeTransition.EndpointTransition);
        return true;
    }

    public void Abort(object transition)
    {
        var runtimeTransition = GetTransition(transition);
        endpointState.Abort(runtimeTransition.EndpointTransition);
        _setSwitching(false);
    }

    public async Task PostCommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 端点发布后永久清除旧身份，再由新端点重新建立终端和收银员会话。
            operationAuthorizationService.Cancel();
            operationAuthorizationService.RevokeAll();
            cashierSessionContext.Clear();
            await _postCommitAsync(cancellationToken);
            operationAuditUploadService.RequestUpload();
        }
        finally
        {
            _setSwitching(false);
        }
    }

    private ApiServerSwitchSafetySnapshot CreateSnapshot(ApiServerPaymentSafetyState payment)
    {
        return new ApiServerSwitchSafetySnapshot(
            cart.Lines.Count,
            payment.IsCardPaymentInProgress,
            payment.IsPaymentInteractionLocked,
            payment.PaymentTenderCount,
            PendingSyncCount: 0,
            FailedSyncCount: 0,
            SyncingCount: 0,
            PendingOperationAuditCount: 0);
    }

    private static ApiServerRuntimeTransition GetTransition(object transition)
    {
        return transition as ApiServerRuntimeTransition ??
            throw new ArgumentException("服务器切换令牌无效。", nameof(transition));
    }

    private sealed record ApiServerRuntimeTransition(ApiEndpointTransition EndpointTransition);
}
