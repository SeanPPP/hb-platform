using System.IO;
using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Services;

public interface ISquarePaymentRecoveryService
{
    Task<CardPaymentRecoveryResult> RecoverLatestAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(
        CardRefundSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CardRefundSupervisorResolutionResult(
            false,
            "Square refund supervisor resolution is unavailable."));

    Task<IReadOnlyList<CardRecoveryQueueItem>> ListOpenAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<CardPaymentRecoveryResult> RecoverAttemptAsync(
        Guid attemptGuid,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<CardRecoveryResolutionResult> ResolveAttemptAsync(
        Guid attemptGuid,
        CardRecoverySupervisorDecision decision,
        string reason,
        string? evidence,
        string? reference,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default);
}

public sealed class SquarePaymentRecoveryService(
    ILocalSquarePaymentAttemptRepository attemptRepository,
    ICardTerminalSettingsProvider settingsProvider,
    ISquareTerminalPaymentClient squareTerminalPaymentClient,
    CashCheckoutService checkout,
    ILocalOrderRepository orderRepository,
    ILocalizationService? localization = null,
    FinancialSupervisorAuditReplayService? supervisorAuditReplay = null,
    ISharedHeldOrderRepository? sharedHeldOrderRepository = null) : ISquarePaymentRecoveryService
{
    private const string AutomaticCanceledClaimCode = "SQUARE_AUTO_CANCELED_CART_RESTORE";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISharedHeldOrderPaymentSourceResolver? _heldOrderPaymentSourceResolver =
        sharedHeldOrderRepository is null
            ? null
            : new SharedHeldOrderPaymentSourceResolver(
                sharedHeldOrderRepository,
                new SharedHeldOrderReverseMapper());

    private async Task<LocalHeldOrderCompletionContext?> TryResolveHeldOrderAsync(
        PosSessionState session,
        PosCartSnapshot cartSnapshot,
        CancellationToken cancellationToken)
    {
        if (_heldOrderPaymentSourceResolver is null)
        {
            // 来源解析器缺失时，已显式绑定共享 claim 的购物车绝不能静默降级为普通订单。
            if (cartSnapshot.SharedHeldOrderClaimId is not null)
            {
                throw new InvalidDataException(
                    "共享挂单购物车 binding 但来源解析器不可用，拒绝降级为普通订单。");
            }

            return null;
        }

        // 恢复路径必须使用 payment draft 中冻结的 CartSnapshot，不能用当前 UI 购物车。
        return await _heldOrderPaymentSourceResolver.TryResolveAsync(
            session,
            cartSnapshot,
            cancellationToken);
    }

    public async Task<CardPaymentRecoveryResult> RecoverLatestAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        if (settings.Processor != CardProcessorKind.Square)
        {
            return CardPaymentRecoveryResult.None;
        }

        var storeCode = session.StoreCode;
        var deviceCode = session.DeviceCode;
        var environment = settings.Environment.ToString();
        var refundAttempts = await RunLocalStoreAsync(
            () => attemptRepository.GetOpenRefundAttemptsAsync(
                storeCode,
                deviceCode,
                environment,
                cancellationToken),
            cancellationToken);
        var refundAttempt = refundAttempts.FirstOrDefault();
        if (refundAttempt is not null)
        {
            return await RecoverRefundAttemptAsync(
                cart,
                session,
                settings,
                refundAttempt,
                cancellationToken);
        }

        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetLatestOpenSaleAttemptForTerminalAsync(
                storeCode,
                deviceCode,
                environment,
                cancellationToken),
            cancellationToken);
        if (attempt is null)
        {
            return CardPaymentRecoveryResult.None;
        }

        return await RecoverSaleAttemptAsync(cart, session, settings, attempt, cancellationToken);
    }

    public async Task<IReadOnlyList<CardRecoveryQueueItem>> ListOpenAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        var attempts = await RunLocalStoreAsync(
            () => attemptRepository.GetOpenAttemptsAsync(
                session.StoreCode,
                session.DeviceCode,
                settings.Environment.ToString(),
                cancellationToken),
            cancellationToken);
        return attempts
            .Select(attempt => new CardRecoveryQueueItem(
                CardProcessorKind.Square,
                attempt.AttemptGuid,
                attempt.OperationKind,
                attempt.Amount,
                attempt.StoreCode,
                attempt.DeviceCode,
                attempt.CashierId,
                attempt.Environment,
                attempt.Status.ToString(),
                attempt.CreatedAt,
                attempt.UpdatedAt,
                attempt.OrderDraftJson,
                null,
                null,
                attempt.CheckoutId,
                attempt.ResponseCode,
                attempt.ResponseText,
                null,
                attempt.PaymentId,
                attempt.OperationGuid))
            .ToArray();
    }

    public async Task<CardPaymentRecoveryResult> RecoverAttemptAsync(
        Guid attemptGuid,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(attemptGuid, cancellationToken),
            cancellationToken);
        if (attempt is null ||
            !string.Equals(attempt.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.Environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return CardPaymentRecoveryResult.None;
        }

        // 终态 attempt 不可再恢复；双保险之一，避免 Abandoned 等终态被重复恢复。
        if (IsTerminalSquareStatus(attempt.Status))
        {
            return CardPaymentRecoveryResult.None;
        }

        if (string.Equals(attempt.OperationKind, "Refund", StringComparison.OrdinalIgnoreCase))
        {
            return await RecoverRefundAttemptAsync(cart, session, settings, attempt, cancellationToken);
        }

        return await RecoverSaleAttemptAsync(cart, session, settings, attempt, cancellationToken);
    }

    public async Task<CardRecoveryResolutionResult> ResolveAttemptAsync(
        Guid attemptGuid,
        CardRecoverySupervisorDecision decision,
        string reason,
        string? evidence,
        string? reference,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(attemptGuid, cancellationToken),
            cancellationToken);
        if (attempt is null ||
            !string.Equals(attempt.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.Environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return new CardRecoveryResolutionResult(
                false,
                "The unresolved attempt no longer matches this terminal and cannot be changed.");
        }

        if (string.Equals(attempt.OperationKind, "Refund", StringComparison.OrdinalIgnoreCase))
        {
            var refundResult = await ResolveRefundAsync(
                new CardRefundSupervisorResolution(
                    attempt.AttemptGuid,
                    CardProcessorKind.Square,
                    MapRefundDecision(decision),
                    reason,
                    evidence,
                    reference),
                cart,
                session,
                cancellationToken);
            return new CardRecoveryResolutionResult(
                refundResult.Succeeded,
                refundResult.Message,
                refundResult.RecoveryResult,
                refundResult.RetryAllowed,
                refundResult.LockRetained);
        }

        return await ResolveSquareSaleAsync(
            attempt,
            decision,
            reason,
            evidence,
            reference,
            cart,
            session,
            cancellationToken);
    }

    private async Task<CardRecoveryResolutionResult> ResolveSquareSaleAsync(
        LocalSquarePaymentAttempt attempt,
        CardRecoverySupervisorDecision decision,
        string reason,
        string? evidence,
        string? reference,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken)
    {
        var draft = TryDeserializeDraft(attempt);
        if (attempt.Status == LocalSquarePaymentAttemptStatus.PaymentVerified)
        {
            if (draft is null ||
                string.IsNullOrWhiteSpace(attempt.PaymentId) ||
                string.IsNullOrWhiteSpace(attempt.PaymentStatus))
            {
                return new CardRecoveryResolutionResult(
                    false,
                    "The verified Square payment evidence is incomplete and cannot be changed.",
                    LockRetained: true);
            }

            // 已验证付款是资金终态：任何后续主管动作都只能幂等完成原订单，不能清空真实付款证据。
            var verified = await CompleteVerifiedAttemptAsync(
                attempt,
                draft,
                attempt.PaymentId,
                attempt.PaymentStatus,
                cardBrand: null,
                maskedCardNumber: null,
                authCode: null,
                cancellationToken);
            var verifiedSucceeded = verified.Outcome == CardPaymentRecoveryOutcome.OrderCompleted;
            return new CardRecoveryResolutionResult(
                verifiedSucceeded,
                verified.Message,
                verified,
                LockRetained: !verifiedSucceeded || verified.HasPostCommitWarning);
        }

        if (IsTerminalSquareStatus(attempt.Status))
        {
            return new CardRecoveryResolutionResult(
                false,
                "The Square payment is already final and cannot be changed.");
        }

        var authorizer = OperationAuthorizationScope.CurrentAuthorizingSession ?? session.CashierSession;
        if (!CardPaymentSupervisorResolutionRules.TryNormalize(
                new CardPaymentSupervisorResolution(
                    attempt.AttemptGuid,
                    CardProcessorKind.Square,
                    decision switch
                    {
                        CardRecoverySupervisorDecision.ConfirmProcessed => CardPaymentSupervisorDecision.ConfirmPaid,
                        CardRecoverySupervisorDecision.ConfirmNotProcessed => CardPaymentSupervisorDecision.ConfirmNotPaid,
                        _ => CardPaymentSupervisorDecision.ContinueWaiting
                    },
                    reason,
                    authorizer?.CashierId ?? session.CashierId,
                    authorizer?.UserGuid ?? session.CashierSession?.UserGuid,
                    authorizer?.CashierName ?? session.CashierName,
                    evidence,
                    reference),
                out var normalized,
                out var validationError))
        {
            return new CardRecoveryResolutionResult(
                false,
                validationError,
                LockRetained: true);
        }

        reason = normalized.Reason;
        evidence = normalized.Evidence;
        reference = normalized.PaymentReference;

        if (decision == CardRecoverySupervisorDecision.ConfirmProcessed && draft is null)
        {
            return new CardRecoveryResolutionResult(
                false,
                "The Square payment draft is incomplete and cannot be completed.",
                LockRetained: true);
        }

        if (decision == CardRecoverySupervisorDecision.ConfirmNotProcessed)
        {
            if (draft is null)
            {
                return new CardRecoveryResolutionResult(
                    false,
                    "The Square payment draft is invalid and cannot be restored.",
                    LockRetained: true);
            }

            if (!cart.IsEmpty)
            {
                return new CardRecoveryResolutionResult(
                    false,
                    "Suspend or clear the current cart before restoring this Square payment so it cannot be overwritten.",
                    LockRetained: true);
            }
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        var journal = BuildSquareSaleSupervisorJournal(
            attempt,
            decision,
            reason,
            evidence,
            reference,
            session,
            resolvedAt);
        var applied = await RunLocalStoreAsync(
            () => attemptRepository.ResolvePaymentWithJournalAsync(
                new SquarePaymentResolution(
                    attempt.AttemptGuid,
                    decision,
                    reason,
                    evidence,
                    reference,
                    attempt.Status,
                    attempt.UpdatedAt,
                    resolvedAt),
                journal,
                CancellationToken.None),
            CancellationToken.None);
        if (!applied)
        {
            return new CardRecoveryResolutionResult(
                false,
                "The Square payment state changed before the supervisor decision was saved. Run recovery again.",
                LockRetained: true);
        }

        if (supervisorAuditReplay is not null)
        {
            await supervisorAuditReplay.PersistAfterCommitAsync(journal, CancellationToken.None);
        }

        var updatedAttempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(attempt.AttemptGuid, CancellationToken.None),
            CancellationToken.None) ?? attempt;

        if (decision == CardRecoverySupervisorDecision.ContinueWaiting)
        {
            return new CardRecoveryResolutionResult(
                true,
                T("cardRecovery.square.supervisorWaiting", "The Square payment remains locked. Run recovery again after the bank result is available."),
                LockRetained: true);
        }

        if (decision == CardRecoverySupervisorDecision.ConfirmNotProcessed)
        {
            var restored = await RecoverSupervisorNotPaidSaleAsync(cart, updatedAttempt, cancellationToken);
            var restoredSucceeded = restored.Outcome == CardPaymentRecoveryOutcome.DraftRestored;
            return new CardRecoveryResolutionResult(
                restoredSucceeded,
                restored.Message,
                restored,
                RetryAllowed: restoredSucceeded,
                LockRetained: !restoredSucceeded);
        }

        // ConfirmProcessed：用持久化完整 draft 独立完成订单，绝不触碰当前活动新购物车。
        var completed = await CompleteVerifiedAttemptAsync(
            updatedAttempt,
            draft!,
            updatedAttempt.PaymentId ?? ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
            updatedAttempt.PaymentStatus ?? ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
            cardBrand: null,
            maskedCardNumber: null,
            authCode: null,
            cancellationToken);
        var completedSucceeded = completed.Outcome == CardPaymentRecoveryOutcome.OrderCompleted;
        return new CardRecoveryResolutionResult(
            completedSucceeded,
            completed.Message,
            completed,
            LockRetained: !completedSucceeded || completed.HasPostCommitWarning);
    }

    private static bool IsSupervisorNotPaidSale(LocalSquarePaymentAttempt attempt) =>
        string.Equals(attempt.OperationKind, "Sale", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(attempt.ResponseCode, ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, StringComparison.Ordinal);

    private static bool IsTerminalSquareStatus(LocalSquarePaymentAttemptStatus status) =>
        status is LocalSquarePaymentAttemptStatus.Canceled or
            LocalSquarePaymentAttemptStatus.TimedOut or
            LocalSquarePaymentAttemptStatus.Failed or
            LocalSquarePaymentAttemptStatus.OrderCompleted or
            LocalSquarePaymentAttemptStatus.Abandoned;

    private async Task<CardPaymentRecoveryResult> RecoverSupervisorNotPaidSaleAsync(
        PosCartService cart,
        LocalSquarePaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (!cart.IsEmpty)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.notPaidCartNotEmpty", "The previous Square payment was confirmed not paid, but the current cart is not empty. Clear the current cart before restoring the original order."));
        }

        var deserializedDraft = TryDeserializeDraft(attempt);
        if (deserializedDraft is null)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.notPaidDraftInvalid", "The Square payment draft is invalid and cannot be restored."));
        }

        ValidatedSquareRecoveryDraft validatedDraft;
        try
        {
            // 在触碰活动购物车前物化并验证全部必需嵌套字段、快照和 tender 计算。
            validatedDraft = ValidateAndMaterializeDraft(deserializedDraft);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"not-paid draft snapshot invalid attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.notPaidRestoreFailed", "The previous Square payment could not be restored. Run recovery again."));
        }

        var draft = validatedDraft.Draft;

        // 验证通过后恢复到真实购物车；事件 handler 异常同样不得让 attempt 提前终态。
        try
        {
            cart.RestoreSnapshot(draft.CartSnapshot);
        }
        catch (Exception ex)
        {
            RollbackSupervisorNotPaidCart(cart, attempt.AttemptGuid);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            ConsoleLog.Write(
                "SquareRecovery",
                $"not-paid restore failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.notPaidRestoreFailed", "The previous Square payment could not be restored. Run recovery again."));
        }

        bool terminated;
        try
        {
            terminated = await RunLocalStoreAsync(
                () => attemptRepository.TryTerminalizeNotPaidAsync(
                    attempt.AttemptGuid,
                    attempt.Status,
                    attempt.UpdatedAt,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"not-paid terminalize failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            terminated = false;
        }

        if (!terminated)
        {
            // 调用前已强制空购物车；CAS=false 或数据库异常时只撤销本次恢复，避免旧快照冒充可重试订单。
            RollbackSupervisorNotPaidCart(cart, attempt.AttemptGuid);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.notPaidTerminalizeFailed", "The previous Square payment could not be finalized. Run recovery again."));
        }

        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.DraftRestored,
            T("cardRecovery.square.notPaidRetryAllowed", "The bank confirmed that no payment was processed. The original order is ready to retry with the same operation."),
            TenderedAmount: validatedDraft.CurrentTenderTotal,
            RestoredTenders: draft.CurrentTenders);
    }

    private static void RollbackSupervisorNotPaidCart(PosCartService cart, Guid attemptGuid)
    {
        try
        {
            cart.Clear();
        }
        catch (Exception ex)
        {
            // Clear 会先清空内部状态再通知订阅者；回滚通知即使抛 OCE 也不能遮蔽已完成的内部清空。
            ConsoleLog.Write(
                "SquareRecovery",
                $"not-paid cart rollback notification failed attemptGuid={attemptGuid} error={ex.GetType().Name}");
        }
    }

    private static CardPaymentOrderDraft? TryDeserializeDraft(LocalSquarePaymentAttempt attempt)
    {
        try
        {
            return DeserializeDraft(attempt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static LocalFinancialSupervisorResolution BuildSquareSaleSupervisorJournal(
        LocalSquarePaymentAttempt attempt,
        CardRecoverySupervisorDecision decision,
        string reason,
        string? evidence,
        string? reference,
        PosSessionState session,
        DateTimeOffset resolvedAt)
    {
        var authorizer = OperationAuthorizationScope.CurrentAuthorizingSession ?? session.CashierSession;
        var operatorCashierId = authorizer?.CashierId ?? session.CashierId;
        var operatorUserGuid = authorizer?.UserGuid ?? session.CashierSession?.UserGuid;
        var operatorName = authorizer?.CashierName ?? session.CashierName;
        var resolutionGuid = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var auditEvent = new OperationAuditEventDto
        {
            EventId = auditEventId,
            OccurredAtUtc = resolvedAt,
            OperationType = "CARD_PAYMENT_SUPERVISOR_RESOLUTION",
            Outcome = "Succeeded",
            CashierId = operatorCashierId,
            UserGuid = operatorUserGuid,
            CashierName = operatorName,
            StoreCode = attempt.StoreCode,
            DeviceCode = attempt.DeviceCode,
            CorrelationId = attempt.AttemptGuid.ToString("D"),
            PaymentMethod = CardProcessorKind.Square.ToString(),
            ReasonCode = decision.ToString(),
            SafeMessage = reason,
            PaymentAmount = Math.Abs(attempt.Amount),
            Properties = new Dictionary<string, string?>
            {
                ["attemptGuid"] = attempt.AttemptGuid.ToString("D"),
                ["operationGuid"] = attempt.OperationGuid?.ToString("D"),
                ["checkoutId"] = attempt.CheckoutId,
                ["evidence"] = evidence,
                ["financialReference"] = reference
            }
        };
        return new LocalFinancialSupervisorResolution(
            resolutionGuid,
            LocalFinancialSupervisorResolutionTarget.ActiveSession,
            CardProcessorKind.Square.ToString(),
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.AttemptGuid,
            null,
            attempt.OperationGuid,
            attempt.CheckoutId ?? attempt.IdempotencyKey,
            decision.ToString(),
            operatorCashierId,
            operatorUserGuid,
            operatorName,
            reason,
            evidence,
            reference,
            null,
            resolvedAt,
            auditEventId,
            JsonSerializer.Serialize(auditEvent, JsonOptions));
    }

    private static CardRefundSupervisorDecision MapRefundDecision(CardRecoverySupervisorDecision decision) => decision switch
    {
        CardRecoverySupervisorDecision.ConfirmProcessed => CardRefundSupervisorDecision.ConfirmRefunded,
        CardRecoverySupervisorDecision.ConfirmNotProcessed => CardRefundSupervisorDecision.ConfirmNotRefunded,
        _ => CardRefundSupervisorDecision.ContinueWaiting
    };

    private async Task<CardPaymentRecoveryResult> RecoverRefundAttemptAsync(
        PosCartService cart,
        PosSessionState session,
        CardTerminalSettings settings,
        LocalSquarePaymentAttempt refundAttempt,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                refundAttempt.ResponseCode,
                CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
                StringComparison.Ordinal))
        {
            return await CompleteSupervisorConfirmedRefundAsync(
                cart,
                session,
                refundAttempt,
                cancellationToken);
        }

        if (refundAttempt.Status == LocalSquarePaymentAttemptStatus.Pending &&
            string.Equals(
                refundAttempt.ResponseCode,
                CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
                StringComparison.Ordinal))
        {
            return RestoreSupervisorApprovedRetry(cart, refundAttempt, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(refundAttempt.PaymentId) &&
            !string.IsNullOrWhiteSpace(refundAttempt.SubmissionToken))
        {
            var automaticRecovery = await TryRecoverSquareRefundAsync(
                cart,
                session,
                settings,
                refundAttempt,
                cancellationToken);
            if (automaticRecovery is not null)
            {
                return automaticRecovery;
            }
        }

        if (refundAttempt.Status == LocalSquarePaymentAttemptStatus.Pending)
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkRecoveringAsync(
                    refundAttempt.AttemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
        }

        ConsoleLog.Write(
            "SquareRecovery",
            $"open refund requires reconciliation attemptGuid={refundAttempt.AttemptGuid} idempotencyKey={refundAttempt.IdempotencyKey} amount={refundAttempt.Amount:0.00}");
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.refund.requiresReview", "A previous card refund is still unresolved. Do not refund again; ask a supervisor to reconcile Square and the original sale."),
            DialogDetails: BuildRefundDialogDetails(refundAttempt),
            RefundDetails: BuildRefundDetails(refundAttempt));
    }

    private async Task<CardPaymentRecoveryResult> RecoverSaleAttemptAsync(
        PosCartService cart,
        PosSessionState session,
        CardTerminalSettings settings,
        LocalSquarePaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Status == LocalSquarePaymentAttemptStatus.PaymentVerified ||
            IsSupervisorPaidSale(attempt))
        {
            var verifiedDraft = TryDeserializeDraft(attempt);
            if (verifiedDraft is null ||
                string.IsNullOrWhiteSpace(attempt.PaymentId) ||
                string.IsNullOrWhiteSpace(attempt.PaymentStatus))
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    UnknownResultMessage());
            }

            // 已验证或主管确认付款必须先于 MarkRecovering 幂等完成；仓储会拒绝覆盖主管结案。
            return await CompleteVerifiedAttemptAsync(
                attempt,
                verifiedDraft,
                attempt.PaymentId,
                attempt.PaymentStatus,
                cardBrand: null,
                maskedCardNumber: null,
                authCode: null,
                cancellationToken);
        }

        // 主管确认未付款：在 MarkRecovering/远端 checkout 查询之前恢复并终态化，
        // 避免缺 CheckoutId 遮蔽该主管状态导致永久 Unknown。
        if (IsSupervisorNotPaidSale(attempt))
        {
            return await RecoverSupervisorNotPaidSaleAsync(cart, attempt, cancellationToken);
        }

        try
        {
            // 自动取消 claim 已经是开放态 Recovering；重启接管时保留其版本，避免在恢复 cart 前丢失 CAS 所有权。
            if (!IsAutomaticCanceledClaim(attempt))
            {
                await RunLocalStoreAsync(
                    () => attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken),
                    cancellationToken);
                attempt = await RunLocalStoreAsync(
                    () => attemptRepository.GetAttemptAsync(attempt.AttemptGuid, cancellationToken),
                    cancellationToken) ?? attempt;
            }
        }
        catch (InvalidOperationException)
        {
            var concurrentAttempt = await RunLocalStoreAsync(
                () => attemptRepository.GetAttemptAsync(attempt.AttemptGuid, cancellationToken),
                cancellationToken);
            if (concurrentAttempt is null ||
                concurrentAttempt.Status != LocalSquarePaymentAttemptStatus.PaymentVerified &&
                !IsSupervisorPaidSale(concurrentAttempt) &&
                !IsSupervisorNotPaidSale(concurrentAttempt) &&
                !IsTerminalSquareStatus(concurrentAttempt.Status))
            {
                // 只把真实 guarded update 竞态降级为重读；参数或存储错误保持原异常。
                throw;
            }

            attempt = concurrentAttempt;
        }
        if (attempt.Status == LocalSquarePaymentAttemptStatus.PaymentVerified ||
            IsSupervisorPaidSale(attempt))
        {
            var verifiedDraft = TryDeserializeDraft(attempt);
            if (verifiedDraft is null ||
                string.IsNullOrWhiteSpace(attempt.PaymentId) ||
                string.IsNullOrWhiteSpace(attempt.PaymentStatus))
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    UnknownResultMessage());
            }

            return await CompleteVerifiedAttemptAsync(
                attempt,
                verifiedDraft,
                attempt.PaymentId,
                attempt.PaymentStatus,
                cardBrand: null,
                maskedCardNumber: null,
                authCode: null,
                cancellationToken);
        }

        if (IsSupervisorNotPaidSale(attempt))
        {
            return await RecoverSupervisorNotPaidSaleAsync(cart, attempt, cancellationToken);
        }

        if (IsTerminalSquareStatus(attempt.Status))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.stateChanged", "The Square payment state changed while the bank result was being checked. Run recovery again."));
        }

        var checkingMessage = Format("cardRecovery.square.checking", "A previous Square card transaction for {0:C2} was in progress before the POS closed. Checking the card terminal status.", attempt.Amount);

        if (string.IsNullOrWhiteSpace(attempt.CheckoutId))
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"checkout id missing; payment remains locked attemptGuid={attempt.AttemptGuid}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T(
                    "cardRecovery.square.missingCheckoutId",
                    "The previous Square payment result is unknown because its checkout reference was not saved. Do not take payment again; ask a supervisor to reconcile Square."),
                DialogDetails: new CardPaymentRecoveryDialogDetails(
                    attempt.CheckoutId,
                    attempt.IdempotencyKey,
                    attempt.ResponseCode,
                    attempt.ResponseText,
                    attempt.Amount,
                    attempt.UpdatedAt));
        }

        var draft = TryDeserializeDraft(attempt);
        if (draft is null)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        if (attempt.Status == LocalSquarePaymentAttemptStatus.PaymentVerified &&
            !string.IsNullOrWhiteSpace(attempt.PaymentId) &&
            !string.IsNullOrWhiteSpace(attempt.PaymentStatus))
        {
            return await CompleteVerifiedAttemptAsync(
                attempt,
                draft,
                attempt.PaymentId!,
                attempt.PaymentStatus!,
                cardBrand: null,
                maskedCardNumber: null,
                authCode: null,
                cancellationToken);
        }

        SquareCheckoutStatusResult checkoutStatus;
        try
        {
            checkoutStatus = await squareTerminalPaymentClient.GetCheckoutAsync(settings, attempt.CheckoutId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write("SquareRecovery", $"checkout lookup failed attemptGuid={attempt.AttemptGuid} checkoutId={attempt.CheckoutId} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        var supervisorOutcome = await TryHandleSupervisorResolvedSaleAfterLookupAsync(
            cart,
            attempt,
            draft,
            CancellationToken.None);
        if (supervisorOutcome is not null)
        {
            return supervisorOutcome;
        }

        if (IsSquarePendingStatus(checkoutStatus.Status))
        {
            var guardedOutcome = await TryExecuteGuardedSaleWriteAsync(
                cart,
                attempt,
                draft,
                () => attemptRepository.UpdateCheckoutStatusAsync(
                    attempt.AttemptGuid,
                    LocalSquarePaymentAttemptStatus.Recovering,
                    checkoutStatus.Status,
                    checkoutStatus.CancelReason,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            if (guardedOutcome is not null)
            {
                return guardedOutcome;
            }

            return new CardPaymentRecoveryResult(CardPaymentRecoveryOutcome.Checking, checkingMessage);
        }

        if (string.Equals(checkoutStatus.Status, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            if (TryDeferForCurrentCart(cart, attempt, "checkout-final-CANCELED", out var deferredResult))
            {
                return deferredResult;
            }

            try
            {
                draft = ValidateAndMaterializeDraft(draft).Draft;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ConsoleLog.Write(
                    "SquareRecovery",
                    $"canceled checkout draft invalid attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    UnknownResultMessage());
            }

            var claimedAttempt = attempt;
            if (!IsAutomaticCanceledClaim(claimedAttempt))
            {
                var claimedAt = DateTimeOffset.UtcNow;
                bool claimed;
                try
                {
                    claimed = await RunLocalStoreAsync(
                        () => attemptRepository.TryTerminalizeNotPaidAsync(
                            attempt.AttemptGuid,
                            attempt.Status,
                            attempt.UpdatedAt,
                            claimedAt,
                            CancellationToken.None),
                        CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ConsoleLog.Write(
                        "SquareRecovery",
                        $"canceled checkout claim failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
                    return new CardPaymentRecoveryResult(
                        CardPaymentRecoveryOutcome.Unknown,
                        UnknownResultMessage());
                }

                if (!claimed)
                {
                    var latestOutcome = await TryHandleSupervisorResolvedSaleAfterLookupAsync(
                        cart,
                        attempt,
                        draft,
                        CancellationToken.None);
                    return latestOutcome ?? new CardPaymentRecoveryResult(
                        CardPaymentRecoveryOutcome.Unknown,
                        T("cardRecovery.square.stateChanged", "The Square payment state changed while the bank result was being checked. Run recovery again."));
                }

                claimedAttempt = attempt with
                {
                    Status = LocalSquarePaymentAttemptStatus.Recovering,
                    CheckoutStatus = "CANCELED",
                    ResponseCode = AutomaticCanceledClaimCode,
                    ResolvedAt = null,
                    UpdatedAt = claimedAt
                };
            }

            try
            {
                cart.RestoreSnapshot(draft.CartSnapshot);
            }
            catch (Exception ex)
            {
                // claim 始终保持开放；真实购物车通知失败时只撤销本次快照，下一次恢复可重试。
                RollbackSupervisorNotPaidCart(cart, attempt.AttemptGuid);
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                ConsoleLog.Write(
                    "SquareRecovery",
                    $"canceled checkout cart restore failed with open claim attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    UnknownResultMessage());
            }

            bool terminalized;
            try
            {
                terminalized = await RunLocalStoreAsync(
                    () => attemptRepository.TryTerminalizeNotPaidAsync(
                        claimedAttempt.AttemptGuid,
                        claimedAttempt.Status,
                        claimedAttempt.UpdatedAt,
                        DateTimeOffset.UtcNow,
                        CancellationToken.None),
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RollbackSupervisorNotPaidCart(cart, attempt.AttemptGuid);
                ConsoleLog.Write(
                    "SquareRecovery",
                    $"canceled checkout finalization failed with open claim attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    UnknownResultMessage());
            }

            if (!terminalized)
            {
                RollbackSupervisorNotPaidCart(cart, attempt.AttemptGuid);
                var latestOutcome = await TryHandleSupervisorResolvedSaleAfterLookupAsync(
                    cart,
                    attempt,
                    draft,
                    CancellationToken.None);
                return latestOutcome ?? new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    T("cardRecovery.square.stateChanged", "The Square payment state changed while the bank result was being checked. Run recovery again."));
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                Format("cardRecovery.square.cancelled", "The previous Square card payment was not completed: {0}. The order has been restored. Select a payment method again.", checkoutStatus.CancelReason ?? "CANCELED"));
        }

        if (!string.Equals(checkoutStatus.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            var guardedOutcome = await TryExecuteGuardedSaleWriteAsync(
                cart,
                attempt,
                draft,
                () => attemptRepository.MarkFailedAsync(
                    attempt.AttemptGuid,
                    LocalSquarePaymentAttemptStatus.Unknown,
                    checkoutStatus.Status,
                    attempt.PaymentStatus,
                    null,
                    $"Unexpected checkout status {checkoutStatus.Status}.",
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            if (guardedOutcome is not null)
            {
                return guardedOutcome;
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        var paymentId = checkoutStatus.PaymentIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            var guardedOutcome = await TryExecuteGuardedSaleWriteAsync(
                cart,
                attempt,
                draft,
                () => attemptRepository.MarkFailedAsync(
                    attempt.AttemptGuid,
                    LocalSquarePaymentAttemptStatus.Unknown,
                    checkoutStatus.Status,
                    attempt.PaymentStatus,
                    null,
                    "Square checkout did not return a payment id.",
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            if (guardedOutcome is not null)
            {
                return guardedOutcome;
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        SquarePaymentStatusResult payment;
        try
        {
            payment = await squareTerminalPaymentClient.GetPaymentAsync(settings, paymentId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write("SquareRecovery", $"payment lookup failed attemptGuid={attempt.AttemptGuid} checkoutId={attempt.CheckoutId} paymentId={paymentId} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        supervisorOutcome = await TryHandleSupervisorResolvedSaleAfterLookupAsync(
            cart,
            attempt,
            draft,
            CancellationToken.None);
        if (supervisorOutcome is not null)
        {
            return supervisorOutcome;
        }

        var verification = SquarePaymentVerifier.Verify(
            payment.Status,
            payment.AmountCents,
            payment.Currency,
            attempt.AmountCents,
            attempt.Currency);
        if (!verification.Verified)
        {
            var guardedOutcome = await TryExecuteGuardedSaleWriteAsync(
                cart,
                attempt,
                draft,
                () => attemptRepository.MarkFailedAsync(
                    attempt.AttemptGuid,
                    LocalSquarePaymentAttemptStatus.Unknown,
                    checkoutStatus.Status,
                    payment.Status,
                    null,
                    verification.Message,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            if (guardedOutcome is not null)
            {
                return guardedOutcome;
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                verification.Failure == SquarePaymentVerificationFailure.Amount
                    ? T("cardRecovery.square.amountMismatch", "The payment amount returned by Square does not match the order amount. The order was not saved automatically. Ask a supervisor to confirm.")
                    : UnknownResultMessage());
        }

        var paymentWriteOutcome = await TryExecuteGuardedSaleWriteAsync(
            cart,
            attempt,
            draft,
            () => attemptRepository.MarkPaymentVerifiedAsync(
                attempt.AttemptGuid,
                payment.PaymentId,
                payment.Status,
                null,
                "Payment verified during recovery.",
                DateTimeOffset.UtcNow,
                CancellationToken.None),
            CancellationToken.None);
        if (paymentWriteOutcome is not null)
        {
            return paymentWriteOutcome;
        }

        return await CompleteVerifiedAttemptAsync(
            attempt,
            draft,
            payment.PaymentId,
            payment.Status,
            payment.CardBrand,
            payment.MaskedCardNumber,
            payment.AuthCode,
            cancellationToken);
    }

    private static bool IsSupervisorPaidSale(LocalSquarePaymentAttempt attempt) =>
        string.Equals(attempt.OperationKind, "Sale", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(attempt.ResponseCode, ActiveSessionSupervisorResolutionCodes.ConfirmedPaid, StringComparison.Ordinal);

    private static bool IsAutomaticCanceledClaim(LocalSquarePaymentAttempt attempt) =>
        attempt.Status == LocalSquarePaymentAttemptStatus.Recovering &&
        string.Equals(attempt.ResponseCode, AutomaticCanceledClaimCode, StringComparison.Ordinal);

    private async Task<CardPaymentRecoveryResult?> TryExecuteGuardedSaleWriteAsync(
        PosCartService cart,
        LocalSquarePaymentAttempt queriedAttempt,
        CardPaymentOrderDraft draft,
        Func<Task> write,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunLocalStoreAsync(write, cancellationToken);
            return null;
        }
        catch (InvalidOperationException)
        {
            var latestOutcome = await TryHandleSupervisorResolvedSaleAfterLookupAsync(
                cart,
                queriedAttempt,
                draft,
                CancellationToken.None);
            if (latestOutcome is not null)
            {
                return latestOutcome;
            }

            // 重读仍是原自动恢复状态时，异常不是主管并发拒写，保留真实仓储错误。
            throw;
        }
    }

    private async Task<CardPaymentRecoveryResult?> TryHandleSupervisorResolvedSaleAfterLookupAsync(
        PosCartService cart,
        LocalSquarePaymentAttempt queriedAttempt,
        CardPaymentOrderDraft draft,
        CancellationToken cancellationToken)
    {
        var current = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(queriedAttempt.AttemptGuid, cancellationToken),
            cancellationToken);
        if (current is null)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.stateChanged", "The Square payment state changed while the bank result was being checked. Run recovery again."));
        }

        if (IsSupervisorNotPaidSale(current))
        {
            return await RecoverSupervisorNotPaidSaleAsync(cart, current, cancellationToken);
        }

        if (current.Status == LocalSquarePaymentAttemptStatus.PaymentVerified)
        {
            if (string.IsNullOrWhiteSpace(current.PaymentId) ||
                string.IsNullOrWhiteSpace(current.PaymentStatus))
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    UnknownResultMessage());
            }

            return await CompleteVerifiedAttemptAsync(
                current,
                draft,
                current.PaymentId,
                current.PaymentStatus,
                cardBrand: null,
                maskedCardNumber: null,
                authCode: null,
                cancellationToken);
        }

        if ((string.Equals(
                 current.ResponseCode,
                 ActiveSessionSupervisorResolutionCodes.ContinueWaiting,
                 StringComparison.Ordinal) &&
             !string.Equals(
                 queriedAttempt.ResponseCode,
                 ActiveSessionSupervisorResolutionCodes.ContinueWaiting,
                 StringComparison.Ordinal)) ||
            IsTerminalSquareStatus(current.Status))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.stateChanged", "The Square payment state changed while the bank result was being checked. Run recovery again."));
        }

        return null;
    }

    private async Task<CardPaymentRecoveryResult?> TryRecoverSquareRefundAsync(
        PosCartService cart,
        PosSessionState session,
        CardTerminalSettings settings,
        LocalSquarePaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        SquareRefundStatusResult refund;
        try
        {
            refund = await squareTerminalPaymentClient.GetRefundAsync(
                settings,
                attempt.PaymentId!,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"refund lookup failed attemptGuid={attempt.AttemptGuid} refundId={attempt.PaymentId} error={ex.GetType().Name}");
            return null;
        }

        string? originalPaymentId = null;
        try
        {
            var originalReference = DeserializeDraft(attempt).OriginalReference;
            if (!string.IsNullOrWhiteSpace(originalReference) &&
                originalReference.Trim().StartsWith("SQ:", StringComparison.OrdinalIgnoreCase))
            {
                originalPaymentId = originalReference.Trim()[3..].Trim();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // 草稿损坏时不能自动认定退款终态，保留主管核对路径。
        }

        if (!string.Equals(refund.RefundId, attempt.PaymentId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(originalPaymentId) ||
            !string.Equals(refund.PaymentId, originalPaymentId, StringComparison.Ordinal) ||
            refund.AmountCents != attempt.AmountCents ||
            !string.Equals(refund.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"refund identity mismatch attemptGuid={attempt.AttemptGuid} refundId={attempt.PaymentId}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.requiresReview", "A previous card refund is still unresolved. Do not refund again; ask a supervisor to reconcile Square and the original sale."),
                DialogDetails: BuildRefundDialogDetails(attempt),
                RefundDetails: BuildRefundDetails(attempt));
        }

        if (string.Equals(refund.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            var recorded = await RunLocalStoreAsync(
                () => attemptRepository.TryRecordRefundResponseAsync(
                    attempt.AttemptGuid,
                    attempt.SubmissionToken!,
                    refund.RefundId,
                    refund.Status,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
            if (!recorded)
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    T("cardRecovery.refund.requiresReview", "A previous card refund is still unresolved. Do not refund again; ask a supervisor to reconcile Square and the original sale."),
                    DialogDetails: BuildRefundDialogDetails(attempt),
                    RefundDetails: BuildRefundDetails(attempt));
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Checking,
                T("cardRecovery.refund.squarePending", "Square is still processing the refund. Do not refund again; run recovery later."),
                DialogDetails: BuildRefundDialogDetails(attempt),
                RefundDetails: BuildRefundDetails(attempt));
        }

        if (!string.Equals(refund.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.requiresReview", "A previous card refund is still unresolved. Do not refund again; ask a supervisor to reconcile Square and the original sale."),
                DialogDetails: BuildRefundDialogDetails(attempt),
                RefundDetails: BuildRefundDetails(attempt));
        }

        var completedAt = DateTimeOffset.UtcNow;
        var persisted = await RunLocalStoreAsync(
            () => attemptRepository.TryMarkRefundPaymentVerifiedAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                refund.RefundId,
                refund.Status,
                responseCode: null,
                responseText: "Square refund status confirmed by lookup.",
                completedAt,
                CancellationToken.None),
            CancellationToken.None);
        if (!persisted)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.requiresReview", "A previous card refund is still unresolved. Do not refund again; ask a supervisor to reconcile Square and the original sale."),
                DialogDetails: BuildRefundDialogDetails(attempt),
                RefundDetails: BuildRefundDetails(attempt));
        }

        var verifiedAttempt = attempt with
        {
            Status = LocalSquarePaymentAttemptStatus.PaymentVerified,
            PaymentStatus = refund.Status,
            ResponseCode = null,
            ResponseText = "Square refund status confirmed by lookup.",
            CompletedAt = completedAt,
            UpdatedAt = completedAt
        };
        return await CompleteSupervisorConfirmedRefundAsync(
            cart,
            session,
            verifiedAttempt,
            cancellationToken);
    }

    public async Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(
        CardRefundSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (resolution.Processor != CardProcessorKind.Square)
        {
            return new CardRefundSupervisorResolutionResult(false, "The selected refund does not belong to Square.");
        }

        if (!CardRefundSupervisorResolutionRules.TryNormalize(resolution, out var normalized, out var validationError))
        {
            return new CardRefundSupervisorResolutionResult(false, validationError);
        }

        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(normalized.AttemptGuid, cancellationToken),
            cancellationToken);
        if (attempt is null ||
            !string.Equals(attempt.OperationKind, "Refund", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.Environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return new CardRefundSupervisorResolutionResult(
                false,
                "The unresolved Square refund no longer matches this terminal and cannot be changed.");
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        var journal = BuildRefundSupervisorJournal(attempt, normalized, session, resolvedAt);
        var applied = await RunLocalStoreAsync(
            () => attemptRepository.ResolveRefundWithJournalAsync(
                new CardRefundAttemptResolution(
                    normalized.AttemptGuid,
                    normalized.Decision,
                    normalized.Reason,
                    normalized.Evidence,
                    normalized.RefundReference,
                    RetryTxnRef: null,
                    resolvedAt),
                journal,
                CancellationToken.None),
            CancellationToken.None);
        if (!applied)
        {
            return new CardRefundSupervisorResolutionResult(
                false,
                "The refund state changed before the supervisor decision was saved. Run recovery again.");
        }

        if (supervisorAuditReplay is not null)
        {
            await supervisorAuditReplay.PersistAfterCommitAsync(journal, CancellationToken.None);
        }

        var updatedAttempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(normalized.AttemptGuid, CancellationToken.None),
            CancellationToken.None) ?? attempt;
        ConsoleLog.Write(
            "SquareRecovery",
            $"supervisor refund resolution saved attemptGuid={attempt.AttemptGuid} decision={normalized.Decision} idempotencyKey={attempt.IdempotencyKey}");

        if (normalized.Decision == CardRefundSupervisorDecision.ContinueWaiting)
        {
            return new CardRefundSupervisorResolutionResult(
                true,
                T("cardRecovery.refund.waitingSaved", "The refund remains locked. Run recovery again after the bank result is available."),
                LockRetained: true);
        }

        if (normalized.Decision == CardRefundSupervisorDecision.ConfirmNotRefunded)
        {
            var recovery = RestoreSupervisorApprovedRetry(cart, updatedAttempt, cancellationToken);
            var retryAllowed = recovery.Outcome == CardPaymentRecoveryOutcome.DraftRestored;
            return new CardRefundSupervisorResolutionResult(
                retryAllowed,
                recovery.Message,
                recovery,
                RetryAllowed: retryAllowed,
                LockRetained: !retryAllowed);
        }

        var completed = await CompleteSupervisorConfirmedRefundAsync(
            cart,
            session,
            updatedAttempt,
            cancellationToken);
        var recoveryCompleted = completed.Outcome is
            CardPaymentRecoveryOutcome.OrderCompleted or
            CardPaymentRecoveryOutcome.DraftRestored;
        return new CardRefundSupervisorResolutionResult(
            recoveryCompleted,
            completed.Message,
            completed,
            LockRetained: !recoveryCompleted || completed.HasPostCommitWarning);
    }

    private async Task<CardPaymentRecoveryResult> CompleteSupervisorConfirmedRefundAsync(
        PosCartService cart,
        PosSessionState currentSession,
        LocalSquarePaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        var dialogDetails = BuildRefundDialogDetails(attempt);
        CardPaymentOrderDraft draft;
        try
        {
            draft = ValidateAndMaterializeDraft(DeserializeDraft(attempt)).Draft;
        }
        catch (Exception ex) when (IsInvalidDraftException(ex))
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"confirmed refund draft invalid attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.confirmedDraftInvalid", "The refund is confirmed, but POS could not rebuild the original return. Do not refund again; contact support."),
                DialogDetails: dialogDetails);
        }

        if (string.IsNullOrWhiteSpace(draft.OriginalReference))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.confirmedDraftInvalid", "The refund is confirmed, but POS could not rebuild the original return. Do not refund again; contact support."),
                DialogDetails: dialogDetails);
        }

        IReadOnlyList<PaymentTender> tenders;
        decimal tenderedAmount;
        try
        {
            var cardTender = new PaymentTender(
                PaymentMethodKind.Card,
                -Math.Abs(draft.CardAmount),
                CardRefundReference.Format(attempt.PaymentId, draft.OriginalReference),
                IdempotencyKey: $"SQUARE_ATTEMPT:{attempt.AttemptGuid:N}");
            tenders = draft.CurrentTenders.Concat([cardTender]).ToArray();
            // tender 物化与计算必须在任何活动购物车写入之前完成。
            tenderedAmount = tenders.Sum(tender => tender.Amount);
        }
        catch (Exception ex) when (IsInvalidDraftException(ex))
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"confirmed refund tender invalid attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.confirmedDraftInvalid", "The refund is confirmed, but POS could not rebuild the original return. Do not refund again; contact support."),
                DialogDetails: dialogDetails);
        }

        if (IsApprovedTenderPartial(draft, tenders))
        {
            if (!cart.IsEmpty)
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    T("cardRecovery.square.currentCartNotEmpty", "The confirmed refund is saved, but the current cart is not empty. Complete or clear it, then run recovery again."),
                    DialogDetails: dialogDetails);
            }

            // ValidateAndMaterializeDraft 已在活动购物车写入前完成快照和 tender 全量验证。
            try
            {
                cart.RestoreSnapshot(draft.CartSnapshot);
            }
            catch (Exception ex)
            {
                RollbackSupervisorNotPaidCart(cart, attempt.AttemptGuid);
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                ConsoleLog.Write(
                    "SquareRecovery",
                    $"confirmed partial refund cart restore failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    T("cardRecovery.refund.confirmedRestoreFailed", "The confirmed refund could not be restored to the current cart. Run recovery again."),
                    DialogDetails: dialogDetails);
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                T("cardRecovery.refund.confirmedTenderRestored", "The confirmed card refund was restored. Complete the remaining refund methods without refunding this card again."),
                TenderedAmount: tenderedAmount,
                DialogDetails: dialogDetails,
                RestoredTenders: tenders);
        }

        PaymentCheckoutResult checkoutResult;
        try
        {
            // 完整退款使用独立购物车重建订单，不能覆盖收银员正在处理的活动购物车。
            var recoveryCart = new PosCartService();
            recoveryCart.RestoreSnapshot(draft.CartSnapshot);
            var cashTenderedAmount = tenders
                .Where(tender => tender.Method == PaymentMethodKind.Cash)
                .Sum(tender => tender.Amount);
            checkoutResult = checkout.CreatePaymentOrder(recoveryCart, draft.Session, tenders, cashTenderedAmount);
        }
        catch (InvalidOperationException ex)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"confirmed refund order rebuild failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.confirmedDraftInvalid", "The refund is confirmed, but POS could not rebuild the original return. Do not refund again; contact support."),
                DialogDetails: dialogDetails);
        }

        var order = checkoutResult.Order with { OrderGuid = draft.OrderGuid };
        try
        {
            var existingOrder = await RunLocalStoreAsync(
                () => orderRepository.GetOrderAsync(draft.OrderGuid, CancellationToken.None),
                CancellationToken.None);
            if (existingOrder is null)
            {
                // 仅新建订单时解析取单来源；订单已保存（订单已保存、attempt 未收尾）时
                // 直接走既有订单幂等收尾，不再解析已经 Completed/bound 的 held claim。
                var heldOrder = await TryResolveHeldOrderAsync(
                    draft.Session,
                    draft.CartSnapshot,
                    CancellationToken.None);
                await RunLocalStoreAsync(
                    () => heldOrder is null
                        ? orderRepository.SavePendingOrderAsync(order, CancellationToken.None)
                        : orderRepository.SavePendingOrderWithHeldSourceAsync(
                            order,
                            heldOrder,
                            CancellationToken.None),
                    CancellationToken.None);
            }
            else
            {
                order = existingOrder;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"confirmed refund order save failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.confirmedOrderSaveFailed", "The refund is confirmed, but POS could not save the recovered return. Do not refund again; run recovery after the local store is available."),
                DialogDetails: dialogDetails);
        }

        var hasPostCommitWarning = false;
        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkOrderCompletedAsync(
                    attempt.AttemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hasPostCommitWarning = true;
            ConsoleLog.Write(
                "SquareRecovery",
                $"confirmed refund order saved but attempt finalization failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
        }

        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            T("cardRecovery.refund.confirmedCompleted", "The confirmed card refund was recovered and the return was saved."),
            order,
            tenderedAmount,
            checkoutResult.ChangeAmount,
            currentSession,
            dialogDetails,
            HasPostCommitWarning: hasPostCommitWarning);
    }

    private CardPaymentRecoveryResult RestoreSupervisorApprovedRetry(
        PosCartService cart,
        LocalSquarePaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (!cart.IsEmpty)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.currentCartNotEmpty", "The bank confirmed that no refund was processed, but the current cart is not empty. Complete or clear it, then run recovery again."),
                DialogDetails: BuildRefundDialogDetails(attempt));
        }

        ValidatedSquareRecoveryDraft validatedDraft;
        try
        {
            validatedDraft = ValidateAndMaterializeDraft(DeserializeDraft(attempt));
        }
        catch (Exception ex) when (IsInvalidDraftException(ex))
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"not-refunded retry draft invalid attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.retryDraftInvalid", "The bank confirmed no refund, but POS could not rebuild the original return. Do not retry until support checks this attempt."),
                DialogDetails: BuildRefundDialogDetails(attempt));
        }

        var draft = validatedDraft.Draft;
        try
        {
            cart.RestoreSnapshot(draft.CartSnapshot);
        }
        catch (Exception ex)
        {
            RollbackSupervisorNotPaidCart(cart, attempt.AttemptGuid);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            ConsoleLog.Write(
                "SquareRecovery",
                $"not-refunded retry cart restore failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.retryRestoreFailed", "The original return could not be restored to the current cart. Run recovery again."),
                DialogDetails: BuildRefundDialogDetails(attempt));
        }

        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.DraftRestored,
            T("cardRecovery.refund.retryAllowed", "The bank confirmed that no refund was processed. The original return is ready to retry with the same operation."),
            TenderedAmount: validatedDraft.CurrentTenderTotal,
            DialogDetails: BuildRefundDialogDetails(attempt),
            RestoredTenders: draft.CurrentTenders);
    }

    private static CardPaymentRecoveryDialogDetails BuildRefundDialogDetails(LocalSquarePaymentAttempt attempt) =>
        new(
            attempt.CheckoutId,
            attempt.IdempotencyKey,
            attempt.ResponseCode,
            attempt.ResponseText,
            attempt.Amount,
            attempt.UpdatedAt);

    private static CardRefundRecoveryDetails BuildRefundDetails(LocalSquarePaymentAttempt attempt)
    {
        string? originalReference = null;
        try
        {
            originalReference = DeserializeDraft(attempt).OriginalReference;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // 草稿损坏时仍保留 attempt 身份供主管审计，不能因为缺少展示字段释放退款锁。
        }

        return new CardRefundRecoveryDetails(
            attempt.AttemptGuid,
            CardProcessorKind.Square,
            attempt.OperationGuid,
            attempt.Amount,
            originalReference);
    }

    private static ValidatedSquareRecoveryDraft ValidateAndMaterializeDraft(CardPaymentOrderDraft draft)
    {
        if (draft.Session is null ||
            draft.CartSnapshot is null ||
            draft.CartSnapshot.Lines is null ||
            draft.CurrentTenders is null)
        {
            throw new InvalidOperationException("Square payment order draft is missing required nested data.");
        }

        var lines = draft.CartSnapshot.Lines.ToArray();
        if (lines.Any(line => line is null))
        {
            throw new InvalidOperationException("Square payment order draft contains an invalid cart line.");
        }

        var currentTenders = draft.CurrentTenders.ToArray();
        if (currentTenders.Any(tender => tender is null))
        {
            throw new InvalidOperationException("Square payment order draft contains an invalid tender.");
        }

        var materializedDraft = draft with
        {
            CartSnapshot = draft.CartSnapshot with { Lines = lines },
            CurrentTenders = currentTenders
        };

        // 临时购物车无外部订阅者；先完整验证语义，再允许任何活动购物车写入。
        var validationCart = new PosCartService();
        validationCart.RestoreSnapshot(materializedDraft.CartSnapshot);
        var currentTenderTotal = currentTenders.Sum(tender => tender.Amount);
        return new ValidatedSquareRecoveryDraft(materializedDraft, currentTenderTotal);
    }

    private static bool IsInvalidDraftException(Exception exception) =>
        exception is JsonException or
            InvalidOperationException or
            ArgumentException or
            NullReferenceException or
            ArithmeticException;

    private sealed record ValidatedSquareRecoveryDraft(
        CardPaymentOrderDraft Draft,
        decimal CurrentTenderTotal);

    private static bool IsApprovedTenderPartial(
        CardPaymentOrderDraft draft,
        IReadOnlyList<PaymentTender> tenders)
    {
        var actualAmount = decimal.Round(draft.ActualAmount, 2, MidpointRounding.AwayFromZero);
        var tenderTotal = decimal.Round(
            tenders.Sum(tender => tender.Amount),
            2,
            MidpointRounding.AwayFromZero);
        return actualAmount < 0m && tenderTotal < 0m && tenderTotal > actualAmount;
    }

    private async Task<CardPaymentRecoveryResult> CompleteVerifiedAttemptAsync(
        LocalSquarePaymentAttempt attempt,
        CardPaymentOrderDraft draft,
        string paymentId,
        string paymentStatus,
        string? cardBrand,
        string? maskedCardNumber,
        string? authCode,
        CancellationToken cancellationToken)
    {
        PaymentCheckoutResult checkoutResult;
        try
        {
            draft = ValidateAndMaterializeDraft(draft).Draft;
            var recoveryCart = new PosCartService();
            recoveryCart.RestoreSnapshot(draft.CartSnapshot);
            var cardTender = new PaymentTender(
                PaymentMethodKind.Card,
                Math.Abs(draft.CardAmount),
                $"SQ:{paymentId}",
                CardTransactions:
                [
                    new CardTransactionDto(
                        "Square",
                        paymentId,
                        authCode,
                        cardBrand,
                        null,
                        maskedCardNumber,
                        null,
                        null,
                        paymentStatus,
                        null,
                        DateTimeOffset.UtcNow,
                        Math.Abs(draft.CardAmount),
                        null)
                ],
                IdempotencyKey: $"SQUARE_ATTEMPT:{attempt.AttemptGuid:N}");
            var tenders = draft.CurrentTenders.Concat([cardTender]).ToArray();
            var cashTenderedAmount = tenders
                .Where(tender => tender.Method == PaymentMethodKind.Cash)
                .Sum(tender => tender.Amount);
            checkoutResult = checkout.CreatePaymentOrder(recoveryCart, draft.Session, tenders, cashTenderedAmount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"verified payment order rebuild failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        var order = checkoutResult.Order with { OrderGuid = draft.OrderGuid };
        try
        {
            var existingOrder = await RunLocalStoreAsync(
                () => orderRepository.GetOrderAsync(draft.OrderGuid, CancellationToken.None),
                CancellationToken.None);
            if (existingOrder is null)
            {
                // 仅新建订单时解析取单来源；订单已保存时直接走既有订单幂等收尾。
                var heldOrder = await TryResolveHeldOrderAsync(
                    draft.Session,
                    draft.CartSnapshot,
                    CancellationToken.None);
                await RunLocalStoreAsync(
                    () => heldOrder is null
                        ? orderRepository.SavePendingOrderAsync(order, CancellationToken.None)
                        : orderRepository.SavePendingOrderWithHeldSourceAsync(
                            order,
                            heldOrder,
                            CancellationToken.None),
                    CancellationToken.None);
            }
            else
            {
                order = existingOrder;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"verified payment order save failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        var hasPostCommitWarning = false;
        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkOrderCompletedAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hasPostCommitWarning = true;
            ConsoleLog.Write(
                "SquareRecovery",
                $"verified payment order saved but attempt finalization failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
        }

        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            T("cardRecovery.square.approved", "The previous Square card payment was successful. The order has been recovered and saved automatically."),
            order,
            HasPostCommitWarning: hasPostCommitWarning);
    }

    private bool TryDeferForCurrentCart(
        PosCartService cart,
        LocalSquarePaymentAttempt attempt,
        string reason,
        out CardPaymentRecoveryResult result)
    {
        if (cart.IsEmpty)
        {
            result = CardPaymentRecoveryResult.None;
            return false;
        }

        // 褰撳墠璐墿杞﹀凡鏈夋柊璁㈠崟鏃讹紝涓嶆仮澶嶆棫鑽夌銆佷笉淇濆瓨璁㈠崟锛屼篃涓嶆妸鏃?attempt 鏍囪涓哄凡澶勭悊銆?
        ConsoleLog.Write(
            "SquareRecovery",
            $"defer recovery because current cart is not empty attemptGuid={attempt.AttemptGuid} checkoutId={attempt.CheckoutId ?? "<null>"} reason={reason}");
        result = new CardPaymentRecoveryResult(CardPaymentRecoveryOutcome.Unknown, CurrentCartNotEmptyMessage());
        return true;
    }

    private static CardPaymentOrderDraft DeserializeDraft(LocalSquarePaymentAttempt attempt)
    {
        return JsonSerializer.Deserialize<CardPaymentOrderDraft>(attempt.OrderDraftJson, JsonOptions)
            ?? throw new InvalidOperationException("Square payment order draft is invalid.");
    }

    private static bool IsSquarePendingStatus(string status)
    {
        return string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "CANCEL_REQUESTED", StringComparison.OrdinalIgnoreCase);
    }

    private string CurrentCartNotEmptyMessage()
    {
        return T("cardRecovery.square.currentCartNotEmpty", "The previous Square card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order.");
    }

    private string UnknownResultMessage()
    {
        return T("cardRecovery.square.unknown", "The previous Square card result cannot be confirmed. Ask a supervisor to confirm the Square backend status before continuing.");
    }

    private static LocalFinancialSupervisorResolution BuildRefundSupervisorJournal(
        LocalSquarePaymentAttempt attempt,
        CardRefundSupervisorResolution resolution,
        PosSessionState session,
        DateTimeOffset resolvedAt)
    {
        var authorizer = OperationAuthorizationScope.CurrentAuthorizingSession ?? session.CashierSession;
        var operatorCashierId = authorizer?.CashierId ?? session.CashierId;
        var operatorUserGuid = authorizer?.UserGuid ?? session.CashierSession?.UserGuid;
        var operatorName = authorizer?.CashierName ?? session.CashierName;
        var resolutionGuid = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var auditEvent = new OperationAuditEventDto
        {
            EventId = auditEventId,
            OccurredAtUtc = resolvedAt,
            OperationType = "CARD_REFUND_SUPERVISOR_RESOLUTION",
            Outcome = resolution.Decision.ToString(),
            CashierId = operatorCashierId,
            UserGuid = operatorUserGuid,
            CashierName = operatorName,
            StoreCode = attempt.StoreCode,
            DeviceCode = attempt.DeviceCode,
            CorrelationId = attempt.AttemptGuid.ToString("D"),
            PaymentMethod = CardProcessorKind.Square.ToString(),
            ReasonCode = resolution.Decision.ToString(),
            SafeMessage = resolution.Reason,
            PaymentAmount = Math.Abs(attempt.Amount),
            Properties = new Dictionary<string, string?>
            {
                ["attemptGuid"] = attempt.AttemptGuid.ToString("D"),
                ["operationGuid"] = attempt.OperationGuid?.ToString("D"),
                ["checkoutId"] = attempt.CheckoutId,
                ["evidence"] = resolution.Evidence,
                ["financialReference"] = resolution.RefundReference,
                ["retryReference"] = attempt.IdempotencyKey
            }
        };
        return new LocalFinancialSupervisorResolution(
            resolutionGuid,
            LocalFinancialSupervisorResolutionTarget.CardRefund,
            CardProcessorKind.Square.ToString(),
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.AttemptGuid,
            null,
            attempt.OperationGuid,
            attempt.CheckoutId,
            resolution.Decision.ToString(),
            operatorCashierId,
            operatorUserGuid,
            operatorName,
            resolution.Reason,
            resolution.Evidence,
            resolution.RefundReference,
            attempt.IdempotencyKey,
            resolvedAt,
            auditEventId,
            JsonSerializer.Serialize(auditEvent, JsonOptions));
    }

    private string T(string key, string fallback)
    {
        var value = localization?.T(key);
        return string.IsNullOrWhiteSpace(value) || value == $"[[{key}]]" ? fallback : value;
    }

    private string Format(string key, string fallback, params object[] args)
    {
        var template = T(key, fallback);
        return string.Format(localization?.CurrentCulture ?? System.Globalization.CultureInfo.CurrentCulture, template, args);
    }

    private static Task RunLocalStoreAsync(Func<Task> operation, CancellationToken cancellationToken) =>
        Task.Run(operation, cancellationToken);

    private static Task<T> RunLocalStoreAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) =>
        Task.Run(operation, cancellationToken);
}
