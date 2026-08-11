namespace Hbpos.Client.Wpf.Services;

/// <summary>
/// 串行化共享挂单的发布与远端取消，避免后台发布请求和删除取消请求交错，
/// 导致服务端在本地删除后仍留下可领取挂单。
/// </summary>
public interface ISharedHeldOrderPublicationGate
{
    Task<T> RunExclusiveAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);
}

public sealed class SharedHeldOrderPublicationGate : ISharedHeldOrderPublicationGate, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunExclusiveAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
