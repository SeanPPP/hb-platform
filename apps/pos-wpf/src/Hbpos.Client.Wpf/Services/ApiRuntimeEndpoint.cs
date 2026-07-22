using System.Net.Http;

namespace Hbpos.Client.Wpf.Services;

public sealed class ApiRuntimeEndpointState
{
    private readonly object _gate = new();
    private readonly Uri _startupAddress;
    private Uri _currentAddress;
    private CancellationTokenSource _generationCancellation = new();
    private TaskCompletionSource? _drainedRequests;
    private long _transitionSequence;
    private long _activeRequestCount;
    private ApiEndpointTransition? _transition;
    private long _version;

    public ApiRuntimeEndpointState(string initialAddress)
    {
        _startupAddress = new Uri(ApiServerSettingsService.NormalizeAddress(initialAddress), UriKind.Absolute);
        _currentAddress = _startupAddress;
    }

    public Uri StartupAddress => _startupAddress;

    public Uri CurrentAddress => Volatile.Read(ref _currentAddress);

    public long Version => Interlocked.Read(ref _version);

    public bool Switch(string address)
    {
        var target = new Uri(ApiServerSettingsService.NormalizeAddress(address), UriKind.Absolute);
        if (string.Equals(CurrentAddress.AbsoluteUri, target.AbsoluteUri, StringComparison.Ordinal))
        {
            return false;
        }

        var transition = BeginTransitionAsync(target.AbsoluteUri, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Commit(transition);
        return true;
    }

    public async Task<ApiEndpointTransition> BeginTransitionAsync(
        string targetAddress,
        CancellationToken cancellationToken)
    {
        var target = new Uri(ApiServerSettingsService.NormalizeAddress(targetAddress), UriKind.Absolute);
        ApiEndpointTransition transition;
        Task drained;
        CancellationTokenSource previousGeneration;
        lock (_gate)
        {
            if (_transition is not null)
            {
                throw new ApiEndpointTransitionException("API 服务器正在切换。");
            }

            if (string.Equals(_currentAddress.AbsoluteUri, target.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new ArgumentException("目标 API 服务器与当前服务器相同。", nameof(targetAddress));
            }

            transition = new ApiEndpointTransition(++_transitionSequence, _currentAddress, target);
            _transition = transition;
            previousGeneration = _generationCancellation;
            _drainedRequests = _activeRequestCount == 0
                ? null
                : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drained = _drainedRequests?.Task ?? Task.CompletedTask;
        }

        // Begin 阶段先封闭新请求并取消旧代，再等待所有 handler 租约真正退出。
        previousGeneration.Cancel();
        try
        {
            await drained.WaitAsync(cancellationToken);
            return transition;
        }
        catch
        {
            Abort(transition);
            throw;
        }
    }

    public void Publish(ApiEndpointTransition transition)
    {
        lock (_gate)
        {
            ValidateTransition(transition);
            Volatile.Write(ref _currentAddress, transition.TargetAddress);
            Interlocked.Increment(ref _version);
        }
    }

    public void Complete(ApiEndpointTransition transition)
    {
        lock (_gate)
        {
            ValidateTransition(transition);
            _generationCancellation.Dispose();
            _generationCancellation = new CancellationTokenSource();
            _transition = null;
            _drainedRequests = null;
        }
    }

    public void Commit(ApiEndpointTransition transition)
    {
        Publish(transition);
        Complete(transition);
    }

    public void Abort(ApiEndpointTransition transition)
    {
        lock (_gate)
        {
            ValidateTransition(transition);
            Volatile.Write(ref _currentAddress, transition.PreviousAddress);
            _generationCancellation.Dispose();
            _generationCancellation = new CancellationTokenSource();
            _transition = null;
            _drainedRequests = null;
        }
    }

    internal ApiRuntimeEndpointLease? AcquireRequestLease()
    {
        lock (_gate)
        {
            if (_transition is not null)
            {
                return null;
            }

            _activeRequestCount++;
            return new ApiRuntimeEndpointLease(
                new ApiRuntimeEndpointSnapshot(_currentAddress, _generationCancellation.Token),
                ReleaseRequestLease);
        }
    }

    private void ReleaseRequestLease()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            _activeRequestCount--;
            if (_activeRequestCount == 0 && _transition is not null)
            {
                drained = _drainedRequests;
            }
        }

        drained?.TrySetResult();
    }

    private void ValidateTransition(ApiEndpointTransition transition)
    {
        if (_transition is null || _transition.Id != transition.Id)
        {
            throw new ApiEndpointTransitionException("API 服务器切换令牌无效或已结束。");
        }
    }
}

public sealed record ApiEndpointTransition(long Id, Uri PreviousAddress, Uri TargetAddress);

public sealed class ApiEndpointTransitionException(string message) : InvalidOperationException(message);

internal sealed record ApiRuntimeEndpointSnapshot(Uri Address, CancellationToken CancellationToken);

internal sealed class ApiRuntimeEndpointLease(
    ApiRuntimeEndpointSnapshot snapshot,
    Action release) : IDisposable
{
    private int _disposed;

    public ApiRuntimeEndpointSnapshot Snapshot { get; } = snapshot;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            release();
        }
    }
}

public sealed class ApiRuntimeEndpointHandler(ApiRuntimeEndpointState endpointState) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var lease = endpointState.AcquireRequestLease();
        if (lease is null)
        {
            // 切换窗口内封闭新请求，并统一为端点代际取消，避免正常协调流程抛出业务异常。
            return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
        }

        var endpoint = lease.Snapshot;
        var requestUri = request.RequestUri;
        if (requestUri is not null &&
            requestUri.IsAbsoluteUri &&
            TryGetStartupRelativePath(requestUri, endpointState.StartupAddress, out var relativePath))
        {
            request.RequestUri = new Uri(endpoint.Address, relativePath);
        }

        return SendForGenerationAsync(request, endpoint.CancellationToken, cancellationToken, lease);
    }

    private async Task<HttpResponseMessage> SendForGenerationAsync(
        HttpRequestMessage request,
        CancellationToken endpointCancellationToken,
        CancellationToken callerCancellationToken,
        ApiRuntimeEndpointLease lease)
    {
        using (lease)
        using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                   endpointCancellationToken,
                   callerCancellationToken))
        {
            return await base.SendAsync(request, linkedCancellation.Token);
        }
    }

    private static bool TryGetStartupRelativePath(Uri requestUri, Uri startupAddress, out string relativePath)
    {
        relativePath = string.Empty;
        if (!string.Equals(requestUri.Scheme, startupAddress.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(requestUri.Host, startupAddress.Host, StringComparison.OrdinalIgnoreCase) ||
            requestUri.Port != startupAddress.Port ||
            !requestUri.AbsolutePath.StartsWith(startupAddress.AbsolutePath, StringComparison.Ordinal))
        {
            return false;
        }

        relativePath = requestUri.AbsoluteUri[startupAddress.AbsoluteUri.Length..];
        return true;
    }
}
