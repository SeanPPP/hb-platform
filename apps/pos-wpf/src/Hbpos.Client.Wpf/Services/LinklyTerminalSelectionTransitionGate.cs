namespace Hbpos.Client.Wpf.Services;

public interface ILinklyTerminalSelectionTransitionGate
{
    ValueTask<IAsyncDisposable> EnterFinancialOperationAsync(CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable?> TryEnterAssignmentAsync(CancellationToken cancellationToken = default);
}

public sealed class LinklyTerminalSelectionTransitionGate : ILinklyTerminalSelectionTransitionGate, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IAsyncDisposable> EnterFinancialOperationAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    public async ValueTask<IAsyncDisposable?> TryEnterAssignmentAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 分配操作不等待正在执行的金融请求，避免设置页看似卡死；金融请求会等待正在提交的分配。
        return await _gate.WaitAsync(0, cancellationToken)
            ? new Releaser(_gate)
            : null;
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
