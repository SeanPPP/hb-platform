namespace Hbpos.Client.Wpf.Services;

public sealed record LinklyFallbackPromptRequest(
    LinklyConnectionMode FailedMode,
    LinklyConnectionMode NextMode,
    string? FailureMessage,
    IReadOnlyList<string> AttemptedModes);

public interface ILinklyFallbackPromptService
{
    Task<bool> ConfirmFallbackAsync(
        LinklyFallbackPromptRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILinklyFallbackPromptCoordinator : ILinklyFallbackPromptService
{
    void SetPromptHandler(Func<LinklyFallbackPromptRequest, CancellationToken, Task<bool>>? handler);
}

public sealed class LinklyFallbackPromptCoordinator : ILinklyFallbackPromptCoordinator
{
    private Func<LinklyFallbackPromptRequest, CancellationToken, Task<bool>>? _handler;

    public void SetPromptHandler(Func<LinklyFallbackPromptRequest, CancellationToken, Task<bool>>? handler)
    {
        // 只有当前付款页可以接管 fallback 决策；无人接管时默认拒绝，避免静默切换刷卡通道。
        Volatile.Write(ref _handler, handler);
    }

    public async Task<bool> ConfirmFallbackAsync(
        LinklyFallbackPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        var handler = Volatile.Read(ref _handler);
        if (handler is null)
        {
            return false;
        }

        try
        {
            return await handler(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
