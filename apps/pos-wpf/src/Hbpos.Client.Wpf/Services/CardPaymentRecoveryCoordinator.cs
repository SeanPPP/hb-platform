using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

public sealed class CardPaymentRecoveryCoordinator(
    ICardTerminalSettingsProvider settingsProvider,
    CardPaymentRecoveryService linklyRecoveryService,
    ISquarePaymentRecoveryService squareRecoveryService,
    FinancialSupervisorAuditReplayService? supervisorAuditReplay = null) : ICardPaymentRecoveryService
{
    public async Task<CardPaymentRecoveryResult> RecoverLatestAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        await ReplaySupervisorAuditAsync(cancellationToken);
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        return settings.Processor switch
        {
            CardProcessorKind.Linkly => await linklyRecoveryService.RecoverLatestAsync(cart, session, cancellationToken),
            CardProcessorKind.Square => await squareRecoveryService.RecoverLatestAsync(cart, session, cancellationToken),
            _ => CardPaymentRecoveryResult.None
        };
    }

    public async Task<CardPaymentRecoveryResult> RecoverActiveSessionAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        await ReplaySupervisorAuditAsync(cancellationToken);
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        return settings.Processor switch
        {
            CardProcessorKind.Linkly => await linklyRecoveryService.RecoverActiveSessionAsync(cart, session, cancellationToken),
            _ => CardPaymentRecoveryResult.None
        };
    }

    public async Task<CardPaymentRecoveryResult> ManuallyClearActiveSessionAsync(
        string sessionId,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        return settings.Processor == CardProcessorKind.Linkly
            ? await linklyRecoveryService.ManuallyClearActiveSessionAsync(sessionId, session, cancellationToken)
            : CardPaymentRecoveryResult.None;
    }

    public Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(
        CardRefundSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        return resolution.Processor switch
        {
            CardProcessorKind.Linkly => linklyRecoveryService.ResolveRefundAsync(
                resolution,
                cart,
                session,
                cancellationToken),
            CardProcessorKind.Square => squareRecoveryService.ResolveRefundAsync(
                resolution,
                cart,
                session,
                cancellationToken),
            _ => Task.FromResult(new CardRefundSupervisorResolutionResult(
                false,
                "The refund processor is not supported."))
        };
    }

    public Task<CardPaymentSupervisorResolutionResult> ResolvePaymentAsync(
        CardPaymentSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        return resolution.Processor == CardProcessorKind.Linkly
            ? linklyRecoveryService.ResolvePaymentAsync(
                resolution,
                cart,
                session,
                cancellationToken)
            : Task.FromResult(new CardPaymentSupervisorResolutionResult(
                false,
                "The payment processor is not supported.",
                LockRetained: true));
    }

    public async Task<IReadOnlyList<CardRecoveryQueueItem>> ListOpenAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        await ReplaySupervisorAuditAsync(cancellationToken);
        // 双 provider 队列：同时列出 Linkly 与 Square 的未结 attempt，全局按更新时间排序，
        // 使配置切换后另一 provider 的历史异常仍然可见。
        var linklyItems = await linklyRecoveryService.ListOpenAsync(session, cancellationToken);
        var squareItems = await squareRecoveryService.ListOpenAsync(session, cancellationToken);
        return linklyItems
            .Concat(squareItems)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .ToArray();
    }

    public async Task<CardPaymentRecoveryResult> RecoverAsync(
        CardRecoveryAttemptKey key,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        await ReplaySupervisorAuditAsync(cancellationToken);
        return key.Processor switch
        {
            CardProcessorKind.Linkly => await linklyRecoveryService.RecoverAttemptAsync(
                key.AttemptGuid,
                cart,
                session,
                cancellationToken),
            CardProcessorKind.Square => await squareRecoveryService.RecoverAttemptAsync(
                key.AttemptGuid,
                cart,
                session,
                cancellationToken),
            _ => CardPaymentRecoveryResult.None
        };
    }

    public async Task<CardRecoveryResolutionResult> ResolveAsync(
        CardRecoveryAttemptKey key,
        CardRecoverySupervisorDecision decision,
        string reason,
        string? evidence,
        string? reference,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        await ReplaySupervisorAuditAsync(cancellationToken);
        return key.Processor switch
        {
            CardProcessorKind.Linkly => await linklyRecoveryService.ResolveAttemptAsync(
                key.AttemptGuid,
                decision,
                reason,
                evidence,
                reference,
                cart,
                session,
                cancellationToken),
            CardProcessorKind.Square => await squareRecoveryService.ResolveAttemptAsync(
                key.AttemptGuid,
                decision,
                reason,
                evidence,
                reference,
                cart,
                session,
                cancellationToken),
            _ => new CardRecoveryResolutionResult(
                false,
                "The provider is not supported.",
                LockRetained: true)
        };
    }

    private async Task ReplaySupervisorAuditAsync(CancellationToken cancellationToken)
    {
        if (supervisorAuditReplay is not null)
        {
            await supervisorAuditReplay.ReplayPendingAsync(cancellationToken);
        }
    }
}
