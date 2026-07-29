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

    private async Task ReplaySupervisorAuditAsync(CancellationToken cancellationToken)
    {
        if (supervisorAuditReplay is not null)
        {
            await supervisorAuditReplay.ReplayPendingAsync(cancellationToken);
        }
    }
}
