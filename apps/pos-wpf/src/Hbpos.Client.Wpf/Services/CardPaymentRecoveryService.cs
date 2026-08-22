using System.IO;
using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;
using static Hbpos.Contracts.Linkly.LinklyCloudBackendStatusConstants;

namespace Hbpos.Client.Wpf.Services;

public enum CardPaymentRecoveryOutcome
{
    None,
    Checking,
    OrderCompleted,
    DraftRestored,
    Unknown,
    ActiveSessionApproved,
    ActiveSessionNotPaid,
    ActiveSessionManuallyCleared
}

public enum CardRefundSupervisorDecision
{
    ConfirmRefunded,
    ConfirmNotRefunded,
    ContinueWaiting
}

public sealed record CardRefundRecoveryDetails(
    Guid AttemptGuid,
    CardProcessorKind Processor,
    Guid? OperationGuid,
    decimal Amount,
    string? OriginalReference);

public sealed record CardRefundSupervisorResolution(
    Guid AttemptGuid,
    CardProcessorKind Processor,
    CardRefundSupervisorDecision Decision,
    string Reason,
    string? Evidence = null,
    string? RefundReference = null);

public sealed record CardRefundSupervisorResolutionResult(
    bool Succeeded,
    string Message,
    CardPaymentRecoveryResult? RecoveryResult = null,
    bool RetryAllowed = false,
    bool LockRetained = false);

public sealed record CardRefundAttemptResolution(
    Guid AttemptGuid,
    CardRefundSupervisorDecision Decision,
    string Reason,
    string? Evidence,
    string? RefundReference,
    string? RetryTxnRef,
    DateTimeOffset ResolvedAt);

public enum CardPaymentSupervisorDecision
{
    ConfirmPaid,
    ConfirmNotPaid,
    ContinueWaiting
}

public sealed record CardPaymentSupervisorDetails(
    Guid AttemptGuid,
    CardProcessorKind Processor,
    string SessionId,
    Guid? OperationGuid,
    LocalCardPaymentAttemptStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record CardPaymentSupervisorResolution(
    Guid AttemptGuid,
    CardProcessorKind Processor,
    CardPaymentSupervisorDecision Decision,
    string Reason,
    string OperatorCashierId,
    string? OperatorUserGuid = null,
    string? OperatorName = null,
    string? Evidence = null,
    string? PaymentReference = null);

public sealed record CardPaymentSupervisorResolutionResult(
    bool Succeeded,
    string Message,
    CardPaymentRecoveryResult? RecoveryResult = null,
    bool LockRetained = false);

internal static class CardRefundSupervisorResolutionCodes
{
    public const string ConfirmedRefunded = "SUPERVISOR_CONFIRMED_REFUNDED";
    public const string ConfirmedNotRefunded = "SUPERVISOR_CONFIRMED_NOT_REFUNDED";
    public const string ContinueWaiting = "SUPERVISOR_CONTINUE_WAITING";
}

internal static class CardRefundSupervisorResolutionRules
{
    public static bool TryNormalize(
        CardRefundSupervisorResolution resolution,
        out CardRefundSupervisorResolution normalized,
        out string error)
    {
        var reason = Normalize(resolution.Reason);
        var evidence = Normalize(resolution.Evidence);
        var refundReference = Normalize(resolution.RefundReference);
        normalized = resolution with
        {
            Reason = reason ?? string.Empty,
            Evidence = evidence,
            RefundReference = refundReference
        };

        if (reason?.Length > 500 || evidence?.Length > 1000 || refundReference?.Length > 200)
        {
            error = "The supervisor note, evidence, or refund reference is too long.";
            return false;
        }

        if (resolution.Decision == CardRefundSupervisorDecision.ConfirmRefunded &&
            reason is null &&
            refundReference is null)
        {
            error = "Enter the bank refund reference or a supervisor note before confirming the refund.";
            return false;
        }

        if (resolution.Decision == CardRefundSupervisorDecision.ConfirmNotRefunded &&
            evidence is null)
        {
            error = "Enter the bank evidence confirming that no refund was processed.";
            return false;
        }

        if (resolution.Decision == CardRefundSupervisorDecision.ContinueWaiting &&
            reason is null)
        {
            error = "Enter a supervisor note before keeping the refund locked.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class CardPaymentSupervisorResolutionRules
{
    public static bool TryNormalize(
        CardPaymentSupervisorResolution resolution,
        out CardPaymentSupervisorResolution normalized,
        out string error)
    {
        var reason = Normalize(resolution.Reason);
        var evidence = Normalize(resolution.Evidence);
        var paymentReference = Normalize(resolution.PaymentReference);
        var operatorCashierId = Normalize(resolution.OperatorCashierId);
        normalized = resolution with
        {
            Reason = reason ?? string.Empty,
            Evidence = evidence,
            PaymentReference = paymentReference,
            OperatorCashierId = operatorCashierId ?? string.Empty,
            OperatorUserGuid = Normalize(resolution.OperatorUserGuid),
            OperatorName = Normalize(resolution.OperatorName)
        };

        if (reason?.Length > 500 ||
            evidence?.Length > 1000 ||
            paymentReference?.Length > 200 ||
            operatorCashierId is null)
        {
            error = "The supervisor identity, note, evidence, or payment reference is invalid.";
            return false;
        }

        if (resolution.Decision == CardPaymentSupervisorDecision.ConfirmPaid &&
            evidence is null &&
            paymentReference is null)
        {
            error = "Enter the bank payment reference or evidence before confirming payment.";
            return false;
        }

        if (resolution.Decision == CardPaymentSupervisorDecision.ConfirmNotPaid &&
            evidence is null)
        {
            error = "Enter bank evidence confirming that no payment was processed.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CardPaymentRecoveryResult(
    CardPaymentRecoveryOutcome Outcome,
    string Message,
    LocalOrder? Order = null,
    decimal TenderedAmount = 0m,
    decimal ChangeAmount = 0m,
    PosSessionState? UpdatedSession = null,
    CardPaymentRecoveryDialogDetails? DialogDetails = null,
    CardPaymentRecoveryBankReceipt? BankReceipt = null,
    IReadOnlyList<PaymentTender>? RestoredTenders = null,
    CardRefundRecoveryDetails? RefundDetails = null,
    bool HasPostCommitWarning = false,
    CardPaymentSupervisorDetails? PaymentSupervisorDetails = null)
{
    public static CardPaymentRecoveryResult None { get; } = new(CardPaymentRecoveryOutcome.None, string.Empty);
}

public sealed record CardPaymentRecoveryBankReceipt(
    string Environment,
    string SessionId,
    string ReceiptText,
    LinklyBankReceiptKind Kind,
    string? ResponseCode,
    string? ResponseText);

public sealed record CardPaymentRecoveryDialogDetails(
    string? SessionId,
    string? TxnRef,
    string? ResponseCode,
    string? ResponseText,
    decimal? Amount,
    DateTimeOffset Timestamp);

public interface ICardPaymentRecoveryService
{
    Task<CardPaymentRecoveryResult> RecoverLatestAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<CardPaymentRecoveryResult> RecoverActiveSessionAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<CardPaymentRecoveryResult> ManuallyClearActiveSessionAsync(
        string sessionId,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(
        CardRefundSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CardRefundSupervisorResolutionResult(
            false,
            "Card refund supervisor resolution is unavailable."));

    Task<CardPaymentSupervisorResolutionResult> ResolvePaymentAsync(
        CardPaymentSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CardPaymentSupervisorResolutionResult(
            false,
            "Card payment supervisor resolution is unavailable.",
            LockRetained: true));

    Task<IReadOnlyList<CardRecoveryQueueItem>> ListOpenAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Card recovery queue is not wired for this service.");

    Task<CardPaymentRecoveryResult> RecoverAsync(
        CardRecoveryAttemptKey key,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Targeted card recovery is not wired for this service.");

    Task<CardRecoveryResolutionResult> ResolveAsync(
        CardRecoveryAttemptKey key,
        CardRecoverySupervisorDecision decision,
        string reason,
        string? evidence,
        string? reference,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Targeted card resolution is not wired for this service.");
}

public sealed class CardPaymentRecoveryService(
    ILocalCardPaymentAttemptRepository attemptRepository,
    ICardTerminalSettingsProvider settingsProvider,
    ILinklyBackendTerminalClient backendTerminalClient,
    CashCheckoutService checkout,
    ILocalOrderRepository orderRepository,
    ISyncQueueRepository syncQueueRepository,
    ILocalizationService? localization = null,
    ILinklyTerminalClient? linklyTerminalClient = null,
    FinancialSupervisorAuditReplayService? supervisorAuditReplay = null,
    ISharedHeldOrderRepository? sharedHeldOrderRepository = null) : ICardPaymentRecoveryService
{
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
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode);
        if (settings.Processor != CardProcessorKind.Linkly)
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

        if (mode != LinklyConnectionMode.CloudBackendAsync && mode != LinklyConnectionMode.LocalIp)
        {
            return CardPaymentRecoveryResult.None;
        }

        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetLatestOpenAttemptAsync(
            storeCode,
            deviceCode,
            // 中文注释：断电/退出恢复属于同一终端安全检查，不能被重启后的当前收银员阻断。
            cashierId: null,
            environment,
            cancellationToken),
            cancellationToken);
        LogRecoveryScan(settings, session, attempt);
        if (attempt is null)
        {
            LogRecoveryResult(settings, null, null, CardPaymentRecoveryOutcome.None, "no-open-attempt");
            return CardPaymentRecoveryResult.None;
        }

        return await RecoverSaleAttemptAsync(
            cart,
            session,
            settings,
            mode,
            attempt,
            cancellationToken);
    }

    private async Task<CardPaymentRecoveryResult> RecoverRefundAttemptAsync(
        PosCartService cart,
        PosSessionState session,
        CardTerminalSettings settings,
        LocalCardPaymentAttempt refundAttempt,
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

        if (refundAttempt.Status == LocalCardPaymentAttemptStatus.Pending &&
            string.Equals(
                refundAttempt.ResponseCode,
                CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
                StringComparison.Ordinal))
        {
            return RestoreSupervisorApprovedRetry(cart, refundAttempt);
        }

        // 未经主管核对的退款不能自动重发；启动恢复只维持锁并呈现三态结案入口。
        if (refundAttempt.Status == LocalCardPaymentAttemptStatus.Pending)
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkRecoveringAsync(
                    refundAttempt.AttemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
        }

        ConsoleLog.Write(
            "CardRecovery",
            $"open refund requires reconciliation attemptGuid={refundAttempt.AttemptGuid} txnRef={LogValue(refundAttempt.TxnRef)} amount={refundAttempt.Amount:0.00}");
        LogRecoveryResult(settings, refundAttempt, null, CardPaymentRecoveryOutcome.Unknown, "refund-requires-reconciliation");
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.refund.requiresReview", "A previous card refund is still unresolved. Do not refund again; ask a supervisor to reconcile the terminal and the original sale."),
            DialogDetails: BuildDialogDetails(refundAttempt),
            RefundDetails: BuildRefundDetails(refundAttempt, CardProcessorKind.Linkly));
    }

    private async Task<CardPaymentRecoveryResult> RecoverSaleAttemptAsync(
        PosCartService cart,
        PosSessionState session,
        CardTerminalSettings settings,
        LinklyConnectionMode mode,
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (IsSupervisorResolvedPayment(attempt))
        {
            return await FinalizeSupervisorPaymentAsync(
                cart,
                session,
                settings,
                attempt,
                cancellationToken);
        }

        var attemptMode = ResolveAttemptConnectionMode(attempt, mode);
        if (attempt.Status == LocalCardPaymentAttemptStatus.RequiresReview)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover requires review attemptGuid={attempt.AttemptGuid} sessionId={LogValue(attempt.SessionId)} txnRef={LogValue(attempt.TxnRef)} amount={attempt.Amount:0.00}");
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Unknown, "requires-review");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.requiresReview", "The previous card amount does not match the order amount. Ask a supervisor to confirm the Linkly backend status before handling."),
                DialogDetails: BuildDialogDetails(attempt),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (attempt.Status == LocalCardPaymentAttemptStatus.OrderCompleted &&
            attempt.AcknowledgedAt is null &&
            !string.IsNullOrWhiteSpace(attempt.SessionId) &&
            attemptMode == LinklyConnectionMode.CloudBackendAsync)
        {
            await RetryCompletedAttemptAcknowledgeAsync(settings, attempt, cancellationToken);
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.OrderCompleted, "order-completed-ack-retry");
            return CardPaymentRecoveryResult.None;
        }

        if (attemptMode == LinklyConnectionMode.LocalIp)
        {
            return await RecoverLatestLocalIpAsync(cart, session, settings, attempt, cancellationToken);
        }

        if (attemptMode != LinklyConnectionMode.CloudBackendAsync)
        {
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.None, "unsupported-attempt-connection-mode");
            return CardPaymentRecoveryResult.None;
        }

        var draft = DeserializeDraft(attempt);
        var checkingMessage = Format("cardRecovery.linkly.checking", "A previous card transaction for {0:C2} was in progress before the POS closed. Checking the card terminal status.", attempt.Amount);
        await RunLocalStoreAsync(
            () => attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken),
            cancellationToken);
        LogRecoveryMarkedRecovering(settings, attempt);

        LinklyCloudBackendSessionResponse? status = null;
        var statusFromResumable = string.IsNullOrWhiteSpace(attempt.SessionId);
        try
        {
            status = !statusFromResumable
                ? await backendTerminalClient.GetSessionStatusAsync(settings, attempt.SessionId!, cancellationToken)
                : await backendTerminalClient.GetResumableSessionAsync(settings, cancellationToken);

            // 鏈?SessionId 浣嗗悗绔?session 宸茶繃鏈?娓呯悊锛屽厹搴曞皾璇?Resumable
            if (!statusFromResumable && status is null)
            {
                ConsoleLog.Write(
                    "CardRecovery",
                    $"recover session-status-null retrying-resumable attemptGuid={attempt.AttemptGuid} sessionId={LogValue(attempt.SessionId)}");
                status = await backendTerminalClient.GetResumableSessionAsync(settings, cancellationToken);
            }

            if (status is not null)
            {
                attempt = await BindRecoveredSessionAsync(attempt, status, cancellationToken);
            }

            if (status is not null && !IsFinal(status))
            {
                ConsoleLog.Write(
                    "CardRecovery",
                    $"recover pending resume start attemptGuid={attempt.AttemptGuid} sessionId={LogValue(status.SessionId)} txnRef={LogValue(status.TxnRef)} status={status.Status}");
                status = await backendTerminalClient.ResumeSessionUntilFinalAsync(settings, status, cancellationToken);
                attempt = await BindRecoveredSessionAsync(attempt, status, cancellationToken);
            }
        }
        // 未知结果异常自带 session/txn 明细，不能再被通用失败文案覆盖。
        catch (LinklyBackendResultUnknownException ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover result-unknown attemptGuid={attempt.AttemptGuid} sessionId={LogValue(status?.SessionId ?? attempt.SessionId)} txnRef={LogValue(status?.TxnRef ?? attempt.TxnRef)} error={ex.GetType().Name}");
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Unknown, "result-unknown", ex.GetType().Name);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                ex.Message,
                DialogDetails: BuildDialogDetails(attempt, status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }
        // 本地停止等待后仍要保留未知结果语义，提醒人工确认 Linkly 后端状态。
        catch (LinklyBackendLocalCancelException ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover local-cancel-result-unknown attemptGuid={attempt.AttemptGuid} sessionId={LogValue(status?.SessionId ?? attempt.SessionId)} txnRef={LogValue(status?.TxnRef ?? attempt.TxnRef)} error={ex.GetType().Name}");
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Unknown, "local-cancel-result-unknown", ex.GetType().Name);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.localCancelUnknown", "Stopped waiting for the previous card result locally, so the final Linkly backend result is still unknown. Ask a supervisor to confirm Linkly before continuing."),
                DialogDetails: BuildDialogDetails(attempt, status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover status failed attemptGuid={attempt.AttemptGuid} sessionId={LogValue(attempt.SessionId)} error={ex.GetType().Name}");
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Unknown, "status-query-failed", ex.GetType().Name);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.unknown", "The previous card result cannot be confirmed. Ask a supervisor to confirm the Linkly backend status before continuing."),
                DialogDetails: BuildDialogDetails(attempt, status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (status is null)
        {
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Checking, "status-null");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Checking,
                checkingMessage,
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (!IsFinal(status))
        {
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Checking, "remote-status-not-final");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Checking,
                checkingMessage,
                DialogDetails: BuildDialogDetails(attempt, status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (!StatusMatchesAttempt(attempt, status, statusFromResumable, out var mismatchReason))
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover status mismatch attemptGuid={attempt.AttemptGuid} sessionId={LogValue(attempt.SessionId)} statusSessionId={LogValue(status.SessionId)} txnRef={LogValue(attempt.TxnRef)} statusTxnRef={LogValue(status.TxnRef)} reason={mismatchReason}");
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Unknown, mismatchReason);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.unknown", "The previous card result cannot be confirmed. Ask a supervisor to confirm the Linkly backend status before continuing."),
                DialogDetails: BuildDialogDetails(attempt, status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (IsApproved(status))
        {
            var result = await CompleteApprovedAttemptAsync(cart, session, settings, attempt, draft, status, cancellationToken);
            var reason = result.Outcome == CardPaymentRecoveryOutcome.OrderCompleted
                ? "approved-order-completed"
                : result.Outcome == CardPaymentRecoveryOutcome.DraftRestored
                    ? "approved-tender-restored"
                    : "approved-requires-review";
            LogRecoveryResult(settings, attempt, status, result.Outcome, reason);
            return result.Outcome == CardPaymentRecoveryOutcome.OrderCompleted
                ? result with { Message = T("cardRecovery.linkly.approved", "The previous card payment was successful. The order has been recovered and saved automatically.") }
                : result with { PaymentSupervisorDetails = BuildPaymentSupervisorDetails(attempt) };
        }

        if (!cart.IsEmpty)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover deferred current-cart-not-empty attemptGuid={attempt.AttemptGuid} sessionId={LogValue(attempt.SessionId)} statusSessionId={LogValue(status.SessionId)} outcome={status.Status}");
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Unknown, "current-cart-not-empty");
            // 只有需要恢复旧草稿的未付款结果才要求当前购物车为空；已付款整单在独立 recovery cart 中完成。
            await RunLocalStoreAsync(
                () => attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken),
                cancellationToken);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.currentCartNotEmpty", "The previous card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order."),
                DialogDetails: BuildDialogDetails(attempt, status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (IsDeclinedOrFailed(status))
        {
            cart.RestoreSnapshot(draft.CartSnapshot);
            await RunLocalStoreAsync(
                () => attemptRepository.UpdateOutcomeAsync(
                    attempt.AttemptGuid,
                    MapFailureStatus(status),
                    status.ResponseCode,
                    status.ResponseText,
                    attempt.PaymentReference,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            await TryAcknowledgeAsync(settings, attempt, status.SessionId, status.TxnRef, cancellationToken);
            var reason = string.IsNullOrWhiteSpace(status.ResponseText) ? status.Status : status.ResponseText;
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.DraftRestored, "declined-or-failed");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                Format("cardRecovery.linkly.failed", "The previous card payment failed: {0}. The order has been restored. Select a payment method again.", reason),
                DialogDetails: BuildDialogDetails(attempt, status));
        }

        LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Unknown, "unhandled-final-status");
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.linkly.unknown", "The previous card result cannot be confirmed. Ask a supervisor to confirm the Linkly backend status before continuing."),
            DialogDetails: BuildDialogDetails(attempt, status),
            PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
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
                CardProcessorKind.Linkly,
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
                attempt.SessionId,
                attempt.TxnRef,
                null,
                attempt.ResponseCode,
                attempt.ResponseText,
                attempt.PaymentReference,
                null,
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
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode);
        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(attemptGuid, cancellationToken),
            cancellationToken);
        if (attempt is null ||
            !string.Equals(attempt.Processor, CardProcessorKind.Linkly.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.Environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return CardPaymentRecoveryResult.None;
        }

        // 定点选择可能来自陈旧列表；终态只能展示历史，绝不能重新进入恢复状态机。
        if (!IsSupervisorResolvedPayment(attempt) &&
            attempt.Status is LocalCardPaymentAttemptStatus.Declined or
                LocalCardPaymentAttemptStatus.Failed or
                LocalCardPaymentAttemptStatus.Cancelled or
                LocalCardPaymentAttemptStatus.TimedOut or
                LocalCardPaymentAttemptStatus.Abandoned)
        {
            return CardPaymentRecoveryResult.None;
        }

        if (string.Equals(attempt.OperationKind, "Refund", StringComparison.OrdinalIgnoreCase))
        {
            return await RecoverRefundAttemptAsync(cart, session, settings, attempt, cancellationToken);
        }

        if (string.Equals(attempt.OperationKind, "ActiveSession", StringComparison.OrdinalIgnoreCase))
        {
            // 定点恢复：必须使用选中 attempt 的 SessionId，绝不能回退到 latest-only active session。
            return await RecoverActiveSessionAttemptAsync(cart, session, settings, attempt, cancellationToken);
        }

        LogRecoveryScan(settings, session, attempt);
        return await RecoverSaleAttemptAsync(cart, session, settings, mode, attempt, cancellationToken);
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
            !string.Equals(attempt.Processor, CardProcessorKind.Linkly.ToString(), StringComparison.OrdinalIgnoreCase) ||
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
                    CardProcessorKind.Linkly,
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

        var authorizer = OperationAuthorizationScope.CurrentAuthorizingSession ?? session.CashierSession;
        var paymentResult = await ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                MapPaymentDecision(decision),
                reason,
                authorizer?.CashierId ?? session.CashierId,
                authorizer?.UserGuid ?? session.CashierSession?.UserGuid,
                authorizer?.CashierName ?? session.CashierName,
                evidence,
                reference),
            cart,
            session,
            cancellationToken);
        return new CardRecoveryResolutionResult(
            paymentResult.Succeeded,
            paymentResult.Message,
            paymentResult.RecoveryResult,
            LockRetained: paymentResult.LockRetained);
    }

    private async Task<CardPaymentRecoveryResult> RecoverActiveSessionAttemptAsync(
        PosCartService cart,
        PosSessionState session,
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (IsSupervisorResolvedActiveSession(attempt))
        {
            return await FinalizeSupervisorPaymentAsync(cart, session, settings, attempt, cancellationToken);
        }

        var sessionId = NormalizeOptional(attempt.SessionId);
        if (sessionId is null)
        {
            return BuildUnresolvedActiveSessionResult(
                attempt,
                T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."));
        }

        LinklyCloudBackendSessionResponse? status = null;
        try
        {
            // 定点恢复：按选中 attempt 的 SessionId 查询，绝不无条件采用后端最新 resumable session。
            status = await backendTerminalClient.GetSessionStatusAsync(settings, sessionId, cancellationToken);
            if (status is null)
            {
                return BuildUnresolvedActiveSessionResult(
                    attempt,
                    T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."));
            }

            attempt = await BindRecoveredSessionAsync(attempt, status, cancellationToken);
            if (!IsFinal(status))
            {
                status = await backendTerminalClient.ResumeSessionUntilFinalAsync(settings, status, cancellationToken);
                attempt = await BindRecoveredSessionAsync(attempt, status, cancellationToken);
            }

            if (string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) &&
                status.TransactionSuccess is null)
            {
                status = await backendTerminalClient.GetSessionStatusAsync(settings, status.SessionId, cancellationToken);
            }
        }
        catch (LinklyBackendResultUnknownException ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover targeted active-session result-unknown attemptGuid={attempt.AttemptGuid} sessionId={LogValue(status?.SessionId ?? attempt.SessionId)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                ex.Message,
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }
        catch (LinklyBackendLocalCancelException ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover targeted active-session local-cancel-result-unknown attemptGuid={attempt.AttemptGuid} sessionId={LogValue(status?.SessionId ?? attempt.SessionId)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionLocalCancelUnknown", "Stopped waiting for the previous Linkly session locally, so the final result is still unknown. Ask a supervisor to confirm Linkly before charging again."),
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover targeted active-session failed attemptGuid={attempt.AttemptGuid} sessionId={LogValue(status?.SessionId ?? attempt.SessionId)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."),
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (!IsFinal(status))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Checking,
                T("cardRecovery.linkly.activeSessionStillPending", "The previous Linkly session is still pending. Try recovery again or ask a supervisor to check Linkly."),
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (IsApproved(status))
        {
            if (!await TrySaveActiveSessionOutcomeAsync(
                    attempt,
                    LocalCardPaymentAttemptStatus.Approved,
                    status,
                    cancellationToken))
            {
                return BuildUnresolvedActiveSessionResult(
                    attempt,
                    T("payment.card.resultUnknown", "The card result is unknown. Do not collect payment again until recovery is completed."));
            }

            if (!await TryAcknowledgeActiveSessionAsync(settings, status, attempt, cancellationToken))
            {
                return ActiveSessionAcknowledgeFailed(status, attempt);
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.ActiveSessionApproved,
                T("cardRecovery.linkly.activeSessionApprovedCleared", "The previous Linkly transaction was successful and has been cleared. Continue the current order."),
                DialogDetails: BuildDialogDetails(status),
                BankReceipt: BuildActiveSessionBankReceipt(status, LinklyBankReceiptKind.RecoveredApproved));
        }

        if (IsDeclinedOrFailed(status))
        {
            if (!await TrySaveActiveSessionOutcomeAsync(
                    attempt,
                    MapFailureStatus(status),
                    status,
                    cancellationToken))
            {
                return BuildUnresolvedActiveSessionResult(
                    attempt,
                    T("payment.card.resultUnknown", "The card result is unknown. Do not collect payment again until recovery is completed."));
            }

            if (!await TryAcknowledgeActiveSessionAsync(settings, status, attempt, cancellationToken))
            {
                return ActiveSessionAcknowledgeFailed(status, attempt);
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.ActiveSessionNotPaid,
                T("cardRecovery.linkly.activeSessionNotPaidCleared", "The previous Linkly transaction was not paid successfully and has been cleared. Continue the current order and retry payment if needed."),
                DialogDetails: BuildDialogDetails(status),
                BankReceipt: BuildActiveSessionBankReceipt(status, LinklyBankReceiptKind.RecoveredFailed));
        }

        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."),
            DialogDetails: BuildDialogDetails(status),
            PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
    }

    private static CardRefundSupervisorDecision MapRefundDecision(CardRecoverySupervisorDecision decision) => decision switch
    {
        CardRecoverySupervisorDecision.ConfirmProcessed => CardRefundSupervisorDecision.ConfirmRefunded,
        CardRecoverySupervisorDecision.ConfirmNotProcessed => CardRefundSupervisorDecision.ConfirmNotRefunded,
        _ => CardRefundSupervisorDecision.ContinueWaiting
    };

    private static CardPaymentSupervisorDecision MapPaymentDecision(CardRecoverySupervisorDecision decision) => decision switch
    {
        CardRecoverySupervisorDecision.ConfirmProcessed => CardPaymentSupervisorDecision.ConfirmPaid,
        CardRecoverySupervisorDecision.ConfirmNotProcessed => CardPaymentSupervisorDecision.ConfirmNotPaid,
        _ => CardPaymentSupervisorDecision.ContinueWaiting
    };

    private async Task<CardPaymentRecoveryResult> RecoverLatestLocalIpAsync(
        PosCartService cart,
        PosSessionState currentSession,
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        var txnRef = NormalizeOptional(attempt.TxnRef);
        if (txnRef is null || linklyTerminalClient is null)
        {
            var reason = txnRef is null ? "local-missing-txn-ref" : "local-client-unavailable";
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Unknown, reason);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.unknown", "The previous card result cannot be confirmed. Ask a supervisor to confirm the Linkly backend status before continuing."),
                DialogDetails: BuildDialogDetails(attempt),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        var draft = DeserializeDraft(attempt);
        await RunLocalStoreAsync(
            () => attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken),
            cancellationToken);
        LogRecoveryMarkedRecovering(settings, attempt);

        PaymentAuthorizationResult authorization;
        try
        {
            // LocalIp 断电恢复只依赖 EFT-Client 的 GetLast，不存在后端 session acknowledge。
            authorization = await linklyTerminalClient.RecoverLastTransactionAsync(
                attempt.Amount,
                draft.Session,
                settings,
                txnRef,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover local-ip get-last failed attemptGuid={attempt.AttemptGuid} txnRef={LogValue(txnRef)} error={ex.GetType().Name}");
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Unknown, "local-get-last-failed", ex.GetType().Name);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.unknown", "The previous card result cannot be confirmed. Ask a supervisor to confirm the Linkly backend status before continuing."),
                DialogDetails: BuildDialogDetails(attempt),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if ((authorization.Approved || HasLocalFinalResult(authorization)) &&
            !LocalAuthorizationMatchesAttempt(attempt, authorization))
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover local-ip txn-ref mismatch attemptGuid={attempt.AttemptGuid} expectedTxnRef={LogValue(attempt.TxnRef)} actualTxnRef={LogValue(ResolveAuthorizationTxnRef(authorization))}");
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Unknown, "local-txn-ref-mismatch");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.unknown", "The previous card result cannot be confirmed. Ask a supervisor to confirm the Linkly backend status before continuing."),
                DialogDetails: BuildDialogDetails(attempt, authorization),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (authorization.Approved)
        {
            var result = await CompleteApprovedLocalAttemptAsync(cart, currentSession, attempt, draft, authorization, cancellationToken);
            var reason = result.Outcome == CardPaymentRecoveryOutcome.OrderCompleted
                ? "local-approved-order-completed"
                : result.Outcome == CardPaymentRecoveryOutcome.DraftRestored
                    ? "local-approved-tender-restored"
                    : "local-approved-requires-review";
            LogRecoveryResult(settings, attempt, null, result.Outcome, reason);
            return result.Outcome == CardPaymentRecoveryOutcome.OrderCompleted
                ? result with { Message = T("cardRecovery.linkly.approved", "The previous card payment was successful. The order has been recovered and saved automatically.") }
                : result with { PaymentSupervisorDetails = BuildPaymentSupervisorDetails(attempt) };
        }

        if (HasLocalFinalResult(authorization) && !cart.IsEmpty)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover local-ip deferred current-cart-not-empty attemptGuid={attempt.AttemptGuid} txnRef={LogValue(txnRef)}");
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Unknown, "current-cart-not-empty");
            await RunLocalStoreAsync(
                () => attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken),
                cancellationToken);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.currentCartNotEmpty", "The previous card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order."),
                DialogDetails: BuildDialogDetails(attempt, authorization),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        if (HasLocalFinalResult(authorization))
        {
            var transaction = authorization.CardTransactions?.FirstOrDefault();
            var responseCode = transaction?.ResponseCode ?? authorization.ResponseCode;
            var responseText = transaction?.ResponseText ?? authorization.ResponseText ?? authorization.Message;
            cart.RestoreSnapshot(draft.CartSnapshot);
            await RunLocalStoreAsync(
                () => attemptRepository.UpdateOutcomeAsync(
                    attempt.AttemptGuid,
                    MapLocalFailureStatus(authorization),
                    responseCode,
                    responseText,
                    authorization.Reference ?? attempt.PaymentReference,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            var reason = string.IsNullOrWhiteSpace(responseText) ? T("cardRecovery.linkly.failedReasonFallback", "Not approved") : responseText;
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.DraftRestored, "local-declined-or-failed");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                Format("cardRecovery.linkly.failed", "The previous card payment failed: {0}. The order has been restored. Select a payment method again.", reason),
                DialogDetails: BuildDialogDetails(attempt, authorization));
        }

        LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Unknown, "local-result-unknown");
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.linkly.unknown", "The previous card result cannot be confirmed. Ask a supervisor to confirm the Linkly backend status before continuing."),
            DialogDetails: BuildDialogDetails(attempt, authorization),
            PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
    }

    public async Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(
        CardRefundSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (resolution.Processor != CardProcessorKind.Linkly)
        {
            return new CardRefundSupervisorResolutionResult(false, "The selected refund does not belong to Linkly.");
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
            !string.Equals(attempt.Processor, CardProcessorKind.Linkly.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.OperationKind, "Refund", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.Environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return new CardRefundSupervisorResolutionResult(
                false,
                "The unresolved Linkly refund no longer matches this terminal and cannot be changed.");
        }

        var retryTxnRef = normalized.Decision == CardRefundSupervisorDecision.ConfirmNotRefunded
            ? BuildSupervisorRetryTxnRef(DeserializeDraft(attempt).OriginalReference)
            : null;
        var resolvedAt = DateTimeOffset.UtcNow;
        var journal = BuildRefundSupervisorJournal(
            attempt,
            normalized,
            session,
            retryTxnRef,
            resolvedAt);
        var applied = await RunLocalStoreAsync(
            () => attemptRepository.ResolveRefundWithJournalAsync(
                new CardRefundAttemptResolution(
                    normalized.AttemptGuid,
                    normalized.Decision,
                    normalized.Reason,
                    normalized.Evidence,
                    normalized.RefundReference,
                    retryTxnRef,
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
            "CardRecovery",
            $"supervisor refund resolution saved attemptGuid={attempt.AttemptGuid} decision={normalized.Decision} retryTxnRef={LogValue(retryTxnRef)}");

        if (normalized.Decision == CardRefundSupervisorDecision.ContinueWaiting)
        {
            return new CardRefundSupervisorResolutionResult(
                true,
                T("cardRecovery.refund.waitingSaved", "The refund remains locked. Run recovery again after the bank result is available."),
                LockRetained: true);
        }

        if (normalized.Decision == CardRefundSupervisorDecision.ConfirmNotRefunded)
        {
            var recovery = RestoreSupervisorApprovedRetry(cart, updatedAttempt);
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
            CancellationToken.None);
        var recoveryCompleted = completed.Outcome is
            CardPaymentRecoveryOutcome.OrderCompleted or
            CardPaymentRecoveryOutcome.DraftRestored;
        return new CardRefundSupervisorResolutionResult(
            recoveryCompleted,
            completed.Message,
            completed,
            LockRetained: !recoveryCompleted || completed.HasPostCommitWarning);
    }

    public async Task<CardPaymentSupervisorResolutionResult> ResolvePaymentAsync(
        CardPaymentSupervisorResolution resolution,
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (resolution.Processor != CardProcessorKind.Linkly)
        {
            return new CardPaymentSupervisorResolutionResult(
                false,
                "The selected payment does not belong to Linkly.",
                LockRetained: true);
        }

        if (!CardPaymentSupervisorResolutionRules.TryNormalize(
                resolution,
                out var normalized,
                out var validationError))
        {
            return new CardPaymentSupervisorResolutionResult(
                false,
                validationError,
                LockRetained: true);
        }

        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(normalized.AttemptGuid, cancellationToken),
            cancellationToken);
        var sessionKey = NormalizeOptional(attempt?.SessionId) ?? NormalizeOptional(attempt?.TxnRef);
        if (attempt is null ||
            sessionKey is null ||
            !string.Equals(attempt.Processor, CardProcessorKind.Linkly.ToString(), StringComparison.OrdinalIgnoreCase) ||
            attempt.OperationKind is not ("Sale" or "ActiveSession") ||
            !string.Equals(attempt.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(attempt.Environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return new CardPaymentSupervisorResolutionResult(
                false,
                "The unresolved Linkly payment no longer matches this terminal and cannot be changed.",
                LockRetained: true);
        }

        var draft = TryDeserializeDraft(attempt);
        var needsEmptyCart = normalized.Decision == CardPaymentSupervisorDecision.ConfirmNotPaid ||
            (normalized.Decision == CardPaymentSupervisorDecision.ConfirmPaid && draft is null);
        if (needsEmptyCart && !cart.IsEmpty)
        {
            return new CardPaymentSupervisorResolutionResult(
                false,
                "Suspend or clear the current cart before resolving this payment so it cannot be overwritten.",
                LockRetained: true);
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        var repositoryResolution = new ActiveSessionResolution(
            attempt.AttemptGuid,
            sessionKey,
            normalized.Decision switch
            {
                CardPaymentSupervisorDecision.ConfirmPaid => ActiveSessionSupervisorDecision.ConfirmPaid,
                CardPaymentSupervisorDecision.ConfirmNotPaid => ActiveSessionSupervisorDecision.ConfirmNotPaid,
                _ => ActiveSessionSupervisorDecision.ContinueWaiting
            },
            attempt.Status,
            attempt.UpdatedAt,
            normalized.Reason,
            normalized.Evidence,
            normalized.PaymentReference,
            resolvedAt);
        var journal = BuildPaymentSupervisorJournal(
            attempt,
            sessionKey,
            normalized,
            resolvedAt);
        var applied = await RunLocalStoreAsync(
            () => attemptRepository.ResolvePaymentWithJournalAsync(
                repositoryResolution,
                journal,
                CancellationToken.None),
            CancellationToken.None);
        if (!applied)
        {
            return new CardPaymentSupervisorResolutionResult(
                false,
                "The payment state changed before the supervisor decision was saved. Run recovery again.",
                LockRetained: true);
        }

        if (supervisorAuditReplay is not null)
        {
            await supervisorAuditReplay.PersistAfterCommitAsync(journal, CancellationToken.None);
        }

        var updatedAttempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(attempt.AttemptGuid, CancellationToken.None),
            CancellationToken.None) ?? attempt;
        if (normalized.Decision == CardPaymentSupervisorDecision.ContinueWaiting)
        {
            var waiting = new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.supervisorWaiting", "The payment remains locked. Run recovery again after the bank result is available."),
                DialogDetails: BuildDialogDetails(updatedAttempt),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(updatedAttempt));
            return new CardPaymentSupervisorResolutionResult(
                true,
                waiting.Message,
                waiting,
                LockRetained: true);
        }

        var completed = await FinalizeSupervisorPaymentAsync(
            cart,
            session,
            settings,
            updatedAttempt,
            CancellationToken.None);
        var lockRetained = completed.Outcome is CardPaymentRecoveryOutcome.Unknown or CardPaymentRecoveryOutcome.Checking;
        return new CardPaymentSupervisorResolutionResult(
            true,
            completed.Message,
            completed,
            lockRetained);
    }

    public async Task<CardPaymentRecoveryResult> RecoverActiveSessionAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        if (settings.Processor != CardProcessorKind.Linkly ||
            !CanRecoverBackendActiveSession(settings))
        {
            return CardPaymentRecoveryResult.None;
        }

        var persistedAttempt = await RunLocalStoreAsync(
            () => attemptRepository.GetLatestOpenActiveSessionAsync(
                session.StoreCode,
                session.DeviceCode,
                settings.Environment.ToString(),
                cancellationToken),
            cancellationToken);
        if (IsSupervisorResolvedActiveSession(persistedAttempt))
        {
            return await FinalizeSupervisorPaymentAsync(
                cart,
                session,
                settings,
                persistedAttempt!,
                cancellationToken);
        }

        LinklyCloudBackendSessionResponse? status = null;
        try
        {
            status = await backendTerminalClient.GetResumableSessionAsync(settings, cancellationToken);
            if (status is null)
            {
                if (persistedAttempt is not null)
                {
                    return BuildUnresolvedActiveSessionResult(
                        persistedAttempt,
                        T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."));
                }

                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.None,
                    T("cardRecovery.linkly.noActiveSession", "No unfinished Linkly session was found. You can try the card payment again."));
            }

            persistedAttempt = await PersistActiveSessionAsync(
                settings,
                session,
                status,
                cancellationToken);
            if (IsSupervisorResolvedActiveSession(persistedAttempt))
            {
                return await FinalizeSupervisorPaymentAsync(
                    cart,
                    session,
                    settings,
                    persistedAttempt,
                    cancellationToken);
            }

            // 付款页按钮只处理后端 active/resumable session，不能把它和当前购物车自动合并。
            if (!IsFinal(status))
            {
                ConsoleLog.Write(
                    "CardRecovery",
                    $"recover active-session resume start sessionId={LogValue(status.SessionId)} txnRef={LogValue(status.TxnRef)} status={status.Status}");
                status = await backendTerminalClient.ResumeSessionUntilFinalAsync(settings, status, cancellationToken);
            }

            if (string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) &&
                status.TransactionSuccess is null)
            {
                // 中文注释：resumable 可能只返回 active session 摘要；Completed 但没有成功/失败位时必须按 SessionId 再查一次权威状态。
                ConsoleLog.Write(
                    "CardRecovery",
                    $"recover active-session refresh final summary sessionId={LogValue(status.SessionId)} txnRef={LogValue(status.TxnRef)} status={status.Status}");
                status = await backendTerminalClient.GetSessionStatusAsync(settings, status.SessionId, cancellationToken);
            }
        }
        // 未知结果异常自带 session/txn 明细，不能再被付款页的兜底文案覆盖。
        catch (LinklyBackendResultUnknownException ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover active-session result-unknown sessionId={LogValue(status?.SessionId)} txnRef={LogValue(status?.TxnRef)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                ex.Message,
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(persistedAttempt));
        }
        // 本地停止等待后要明确告诉收银员结果未知，而不是落回通用 active-session 失败文案。
        catch (LinklyBackendLocalCancelException ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover active-session local-cancel-result-unknown sessionId={LogValue(status?.SessionId)} txnRef={LogValue(status?.TxnRef)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionLocalCancelUnknown", "Stopped waiting for the previous Linkly session locally, so the final result is still unknown. Ask a supervisor to confirm Linkly before charging again."),
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(persistedAttempt));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover active-session failed sessionId={LogValue(status?.SessionId)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."),
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(persistedAttempt));
        }

        if (!IsFinal(status))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Checking,
                T("cardRecovery.linkly.activeSessionStillPending", "The previous Linkly session is still pending. Try recovery again or ask a supervisor to check Linkly."),
                DialogDetails: BuildDialogDetails(status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(persistedAttempt));
        }

        if (IsApproved(status))
        {
            if (!await TrySaveActiveSessionOutcomeAsync(
                    persistedAttempt,
                    LocalCardPaymentAttemptStatus.Approved,
                    status,
                    cancellationToken))
            {
                return BuildUnresolvedActiveSessionResult(
                    persistedAttempt,
                    T("payment.card.resultUnknown", "The card result is unknown. Do not collect payment again until recovery is completed."));
            }

            // 付款页恢复只确认并清理上一笔 active session，不能把结果合并进当前购物车。
            if (!await TryAcknowledgeActiveSessionAsync(settings, status, persistedAttempt, cancellationToken))
            {
                return ActiveSessionAcknowledgeFailed(status, persistedAttempt);
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.ActiveSessionApproved,
                T("cardRecovery.linkly.activeSessionApprovedCleared", "The previous Linkly transaction was successful and has been cleared. Continue the current order."),
                DialogDetails: BuildDialogDetails(status),
                BankReceipt: BuildActiveSessionBankReceipt(status, LinklyBankReceiptKind.RecoveredApproved));
        }

        if (IsDeclinedOrFailed(status))
        {
            if (!await TrySaveActiveSessionOutcomeAsync(
                    persistedAttempt,
                    MapFailureStatus(status),
                    status,
                    cancellationToken))
            {
                return BuildUnresolvedActiveSessionResult(
                    persistedAttempt,
                    T("payment.card.resultUnknown", "The card result is unknown. Do not collect payment again until recovery is completed."));
            }

            // 失败/未提交终态已可安全清理，收银员继续当前订单并按需重新刷卡。
            if (!await TryAcknowledgeActiveSessionAsync(settings, status, persistedAttempt, cancellationToken))
            {
                return ActiveSessionAcknowledgeFailed(status, persistedAttempt);
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.ActiveSessionNotPaid,
                T("cardRecovery.linkly.activeSessionNotPaidCleared", "The previous Linkly transaction was not paid successfully and has been cleared. Continue the current order and retry payment if needed."),
                DialogDetails: BuildDialogDetails(status),
                BankReceipt: BuildActiveSessionBankReceipt(status, LinklyBankReceiptKind.RecoveredFailed));
        }

        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."),
            DialogDetails: BuildDialogDetails(status),
            PaymentSupervisorDetails: BuildPaymentSupervisorDetails(persistedAttempt));
    }

    private async Task<CardPaymentRecoveryResult> FinalizeSupervisorPaymentAsync(
        PosCartService cart,
        PosSessionState session,
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        var confirmedPaid = string.Equals(
            attempt.ResponseCode,
            ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
            StringComparison.Ordinal);
        var confirmedNotPaid = string.Equals(
            attempt.ResponseCode,
            ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
            StringComparison.Ordinal);
        if (!confirmedPaid && !confirmedNotPaid)
        {
            return BuildUnresolvedActiveSessionResult(
                attempt,
                T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."));
        }

        var draft = TryDeserializeDraft(attempt);
        var mode = ResolveAttemptConnectionMode(
            attempt,
            CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode));
        if (confirmedNotPaid)
        {
            var restoredDraft = false;
            if (draft is not null)
            {
                if (!cart.IsEmpty)
                {
                    return BuildUnresolvedActiveSessionResult(
                        attempt,
                        T("cardRecovery.linkly.currentCartNotEmpty", "The previous card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order."));
                }

                cart.RestoreSnapshot(draft.CartSnapshot);
                restoredDraft = true;
            }

            if (!await CompleteSupervisorAcknowledgeAsync(settings, attempt, mode, cancellationToken))
            {
                if (restoredDraft)
                {
                    try
                    {
                        // acknowledge 或本地标记失败时，只撤销本次从空购物车恢复的旧快照。
                        cart.Clear();
                    }
                    catch (Exception ex)
                    {
                        // Clear 先清内部状态再通知订阅者；通知异常不能遮蔽异常记录仍开放的结果。
                        ConsoleLog.Write(
                            "CardRecovery",
                            $"supervisor not-paid cart rollback notification failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
                    }
                }

                return BuildUnresolvedActiveSessionResult(
                    attempt,
                    T("cardRecovery.linkly.activeSessionAcknowledgeFailed", "The previous Linkly result was confirmed, but POS could not clear it with Linkly. Try recovery again or ask a supervisor before charging again."));
            }

            return new CardPaymentRecoveryResult(
                draft is null
                    ? CardPaymentRecoveryOutcome.ActiveSessionNotPaid
                    : CardPaymentRecoveryOutcome.DraftRestored,
                draft is null
                    ? T("cardRecovery.linkly.activeSessionNotPaidCleared", "The previous Linkly transaction was not paid successfully and has been cleared. Continue the current order and retry payment if needed.")
                    : T("cardRecovery.linkly.supervisorNotPaidRestored", "The bank confirmed that no payment was processed. The original order has been restored and can be paid again."),
                DialogDetails: BuildDialogDetails(attempt));
        }

        if (draft is null)
        {
            if (!cart.IsEmpty)
            {
                return BuildUnresolvedActiveSessionResult(
                    attempt,
                    T("cardRecovery.linkly.currentCartNotEmpty", "The previous card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order."));
            }

            if (!await CompleteSupervisorAcknowledgeAsync(settings, attempt, mode, cancellationToken))
            {
                return BuildUnresolvedActiveSessionResult(
                    attempt,
                    T("cardRecovery.linkly.activeSessionAcknowledgeFailed", "The previous Linkly result was confirmed, but POS could not clear it with Linkly. Try recovery again or ask a supervisor before charging again."));
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.ActiveSessionApproved,
                T("cardRecovery.linkly.supervisorPaidNoDraft", "The supervisor confirmed the previous payment. The session is cleared, but no local order draft was available; reconcile the order before continuing."),
                DialogDetails: BuildDialogDetails(attempt));
        }

        CardPaymentRecoveryResult result;
        if (mode == LinklyConnectionMode.LocalIp)
        {
            var authorization = new PaymentAuthorizationResult(
                true,
                Reference: attempt.PaymentReference ?? attempt.TxnRef ?? attempt.AttemptGuid.ToString("N"),
                Message: attempt.ResponseText,
                AuthorizedAmount: Math.Abs(draft.CardAmount),
                Processor: CardProcessorKind.Linkly.ToString(),
                Environment: attempt.Environment,
                ConnectionMode: attempt.ConnectionMode,
                TxnType: attempt.TxnType,
                SessionId: attempt.SessionId,
                TxnRef: attempt.TxnRef,
                ResponseCode: "00",
                ResponseText: attempt.ResponseText);
            result = await CompleteApprovedLocalAttemptAsync(
                cart,
                session,
                attempt,
                draft,
                authorization,
                cancellationToken);
            if (result.Outcome == CardPaymentRecoveryOutcome.OrderCompleted)
            {
                result = await AddLocalSupervisorAcknowledgeWarningAsync(
                    attempt,
                    result,
                    cancellationToken);
            }
        }
        else
        {
            result = await CompleteApprovedAttemptAsync(
                cart,
                session,
                settings,
                attempt,
                draft,
                BuildSupervisorApprovedStatus(attempt),
                cancellationToken);
        }

        return result.Outcome is CardPaymentRecoveryOutcome.Unknown or CardPaymentRecoveryOutcome.Checking
            ? result with { PaymentSupervisorDetails = BuildPaymentSupervisorDetails(attempt) }
            : result;
    }

    public async Task<CardPaymentRecoveryResult> ManuallyClearActiveSessionAsync(
        string sessionId,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = NormalizeOptional(sessionId);
        if (normalizedSessionId is null)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionManualClearMissing", "Cannot clear the previous Linkly session because the session id is missing."));
        }

        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        if (settings.Processor != CardProcessorKind.Linkly ||
            !CanRecoverBackendActiveSession(settings))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionManualClearFailed", "POS could not clear the previous Linkly session. Try recovery again or check Linkly before charging again."));
        }

        try
        {
            await backendTerminalClient.AcknowledgeSessionAsync(settings, normalizedSessionId, cancellationToken);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.ActiveSessionManuallyCleared,
                T("cardRecovery.linkly.activeSessionManuallyCleared", "The previous Linkly session was manually checked and cleared. Continue the current order."),
                DialogDetails: new CardPaymentRecoveryDialogDetails(
                    normalizedSessionId,
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover active-session manual-clear failed sessionId={LogValue(normalizedSessionId)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionManualClearFailed", "POS could not clear the previous Linkly session. Try recovery again or check Linkly before charging again."),
                DialogDetails: new CardPaymentRecoveryDialogDetails(
                    normalizedSessionId,
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.Now));
        }
    }

    private CardPaymentRecoveryResult ActiveSessionAcknowledgeFailed(
        LinklyCloudBackendSessionResponse status,
        LocalCardPaymentAttempt? attempt)
    {
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.linkly.activeSessionAcknowledgeFailed", "The previous Linkly result was confirmed, but POS could not clear it with Linkly. Try recovery again or ask a supervisor before charging again."),
            DialogDetails: BuildDialogDetails(status),
            PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
    }

    private async Task<CardPaymentRecoveryResult> CompleteSupervisorConfirmedRefundAsync(
        PosCartService cart,
        PosSessionState currentSession,
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        var dialogDetails = BuildDialogDetails(attempt);
        CardPaymentOrderDraft draft;
        try
        {
            draft = DeserializeDraft(attempt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            ConsoleLog.Write(
                "CardRecovery",
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

        var tenderReference = CardRefundReference.Format(attempt.PaymentReference, draft.OriginalReference);
        var cardTender = new PaymentTender(
            PaymentMethodKind.Card,
            -Math.Abs(draft.CardAmount),
            tenderReference,
            IdempotencyKey: $"CARD_ATTEMPT:{attempt.AttemptGuid:N}");
        var tenders = draft.CurrentTenders.Concat([cardTender]).ToList();
        if (IsApprovedTenderPartial(draft, tenders))
        {
            if (!cart.IsEmpty)
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    T("cardRecovery.linkly.currentCartNotEmpty", "The confirmed refund is saved, but the current cart is not empty. Complete or clear it, then run recovery again."),
                    DialogDetails: dialogDetails);
            }

            cart.RestoreSnapshot(draft.CartSnapshot);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                T("cardRecovery.refund.confirmedTenderRestored", "The confirmed card refund was restored. Complete the remaining refund methods without refunding this card again."),
                TenderedAmount: tenders.Sum(tender => tender.Amount),
                DialogDetails: dialogDetails,
                RestoredTenders: tenders);
        }

        PaymentCheckoutResult checkoutResult;
        try
        {
            // 完整退款在独立购物车中重建并落单，不读取或清理收银员当前的新购物车。
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
                "CardRecovery",
                $"confirmed refund order rebuild failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.refund.confirmedDraftInvalid", "The refund is confirmed, but POS could not rebuild the original return. Do not refund again; contact support."),
                DialogDetails: dialogDetails);
        }

        var order = checkoutResult.Order with { OrderGuid = draft.OrderGuid };
        var existingOrder = await RunLocalStoreAsync(
            () => orderRepository.GetOrderAsync(draft.OrderGuid, CancellationToken.None),
            CancellationToken.None);
        if (existingOrder is null)
        {
            // 仅新建订单时解析取单来源（与 LocalOrder 同一事务写入来源/完成 claim）；
            // 订单已存在（订单已保存、attempt 未收尾）时直接走既有订单幂等收尾，
            // 不再解析已经 Completed/bound 的 held claim。
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

        await RunLocalStoreAsync(
            () => attemptRepository.MarkOrderCompletedAsync(
                attempt.AttemptGuid,
                DateTimeOffset.UtcNow,
                CancellationToken.None),
            CancellationToken.None);
        var pendingSyncCount = await RunLocalStoreAsync(
            () => syncQueueRepository.CountPendingAsync(CancellationToken.None),
            CancellationToken.None);
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            T("cardRecovery.refund.confirmedCompleted", "The confirmed card refund was recovered and the return was saved."),
            order,
            tenders.Sum(tender => tender.Amount),
            checkoutResult.ChangeAmount,
            currentSession with { PendingSyncCount = pendingSyncCount },
            dialogDetails);
    }

    private CardPaymentRecoveryResult RestoreSupervisorApprovedRetry(
        PosCartService cart,
        LocalCardPaymentAttempt attempt)
    {
        if (!cart.IsEmpty)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.currentCartNotEmpty", "The bank confirmed that no refund was processed, but the current cart is not empty. Complete or clear it, then run recovery again."),
                DialogDetails: BuildDialogDetails(attempt));
        }

        var draft = DeserializeDraft(attempt);
        cart.RestoreSnapshot(draft.CartSnapshot);
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.DraftRestored,
            T("cardRecovery.refund.retryAllowed", "The bank confirmed that no refund was processed. The original return is ready to retry with the same operation."),
            TenderedAmount: draft.CurrentTenders.Sum(tender => tender.Amount),
            DialogDetails: BuildDialogDetails(attempt),
            RestoredTenders: draft.CurrentTenders);
    }

    private static CardRefundRecoveryDetails BuildRefundDetails(
        LocalCardPaymentAttempt attempt,
        CardProcessorKind processor)
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
            processor,
            attempt.OperationGuid,
            attempt.Amount,
            originalReference);
    }

    private static string BuildSupervisorRetryTxnRef(string? originalReference)
    {
        var normalizedOriginalReference = NormalizeLinklyReference(originalReference);
        string txnRef;
        do
        {
            txnRef = Guid.NewGuid().ToString("N");
        }
        while (string.Equals(txnRef, normalizedOriginalReference, StringComparison.OrdinalIgnoreCase));

        return txnRef;
    }

    private async Task<CardPaymentRecoveryResult> CompleteApprovedAttemptAsync(
        PosCartService cart,
        PosSessionState currentSession,
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt,
        CardPaymentOrderDraft draft,
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        var recoveryCart = new PosCartService();
        recoveryCart.RestoreSnapshot(draft.CartSnapshot);
        var tenderAmount = draft.TxnType.Equals("R", StringComparison.OrdinalIgnoreCase)
            ? -Math.Abs(draft.CardAmount)
            : Math.Abs(draft.CardAmount);
        var cardTender = new PaymentTender(
            PaymentMethodKind.Card,
            tenderAmount,
            BuildPaymentReference(attempt, status),
            CardTransactions: [BuildCardTransaction(attempt, status, tenderAmount)],
            IdempotencyKey: $"CARD_ATTEMPT:{attempt.AttemptGuid:N}");
        var tenders = draft.CurrentTenders.Concat([cardTender]).ToList();
        var cashTenderedAmount = tenders
            .Where(tender => tender.Method == PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount);
        if (IsApprovedTenderPartial(draft, tenders))
        {
            return await RestoreApprovedTenderAsync(
                cart,
                draft,
                attempt,
                status.ResponseCode,
                status.ResponseText,
                cardTender.Reference,
                tenders,
                BuildDialogDetails(attempt, status),
                cancellationToken);
        }

        if (!await TryPersistApprovedOutcomeAsync(
                attempt,
                status.ResponseCode,
                status.ResponseText,
                cardTender.Reference))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("payment.card.resultUnknown", "The card result is unknown. Do not collect payment again until recovery is completed."),
                DialogDetails: BuildDialogDetails(attempt, status),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        PaymentCheckoutResult checkoutResult;
        try
        {
            checkoutResult = checkout.CreatePaymentOrder(recoveryCart, draft.Session, tenders, cashTenderedAmount);
        }
        catch (Exception ex)
        {
            return await MarkApprovedRecoveryRequiresReviewAsync(
                attempt,
                status.ResponseCode,
                status.ResponseText,
                cardTender.Reference,
                BuildDialogDetails(attempt, status),
                ex,
                cancellationToken);
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
        catch (Exception ex)
        {
            return await MarkApprovedRecoveryRequiresReviewAsync(
                attempt,
                status.ResponseCode,
                status.ResponseText,
                cardTender.Reference,
                BuildDialogDetails(attempt, status),
                ex,
                cancellationToken);
        }

        var hasPostCommitWarning = false;
        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkOrderCompletedAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, CancellationToken.None),
                CancellationToken.None);
            await TryAcknowledgeAsync(settings, attempt, status.SessionId, status.TxnRef, CancellationToken.None);
        }
        catch (Exception ex)
        {
            hasPostCommitWarning = true;
            ConsoleLog.Write(
                "CardRecovery",
                $"approved order saved but attempt finalization failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
        }

        var pendingSyncCount = currentSession.PendingSyncCount;
        try
        {
            pendingSyncCount = await RunLocalStoreAsync(
                () => syncQueueRepository.CountPendingAsync(CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            hasPostCommitWarning = true;
            ConsoleLog.Write(
                "CardRecovery",
                $"approved order saved but pending sync refresh failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
        }
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            string.Empty,
            order,
            tenders.Sum(tender => tender.Amount),
            checkoutResult.ChangeAmount,
            currentSession with { PendingSyncCount = pendingSyncCount },
            BuildDialogDetails(attempt, status),
            HasPostCommitWarning: hasPostCommitWarning);
    }

    private async Task<CardPaymentRecoveryResult> CompleteApprovedLocalAttemptAsync(
        PosCartService cart,
        PosSessionState currentSession,
        LocalCardPaymentAttempt attempt,
        CardPaymentOrderDraft draft,
        PaymentAuthorizationResult authorization,
        CancellationToken cancellationToken)
    {
        var recoveryCart = new PosCartService();
        recoveryCart.RestoreSnapshot(draft.CartSnapshot);
        var tenderAmount = draft.TxnType.Equals("R", StringComparison.OrdinalIgnoreCase)
            ? -Math.Abs(draft.CardAmount)
            : Math.Abs(draft.CardAmount);
        var cardTransactions = BuildLocalCardTransactions(attempt, authorization, tenderAmount);
        var firstTransaction = cardTransactions.FirstOrDefault();
        var cardTender = new PaymentTender(
            PaymentMethodKind.Card,
            tenderAmount,
            BuildLocalPaymentReference(attempt, authorization),
            CardTransactions: cardTransactions,
            IdempotencyKey: $"CARD_ATTEMPT:{attempt.AttemptGuid:N}");
        var tenders = draft.CurrentTenders.Concat([cardTender]).ToList();
        var cashTenderedAmount = tenders
            .Where(tender => tender.Method == PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount);
        if (IsApprovedTenderPartial(draft, tenders))
        {
            return await RestoreApprovedTenderAsync(
                cart,
                draft,
                attempt,
                firstTransaction?.ResponseCode ?? authorization.ResponseCode,
                firstTransaction?.ResponseText ?? authorization.ResponseText,
                cardTender.Reference,
                tenders,
                BuildDialogDetails(attempt, authorization),
                cancellationToken);
        }

        if (!await TryPersistApprovedOutcomeAsync(
                attempt,
                firstTransaction?.ResponseCode ?? authorization.ResponseCode,
                firstTransaction?.ResponseText ?? authorization.ResponseText,
                cardTender.Reference))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("payment.card.resultUnknown", "The card result is unknown. Do not collect payment again until recovery is completed."),
                DialogDetails: BuildDialogDetails(attempt, authorization),
                PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
        }

        PaymentCheckoutResult checkoutResult;
        try
        {
            checkoutResult = checkout.CreatePaymentOrder(recoveryCart, draft.Session, tenders, cashTenderedAmount);
        }
        catch (Exception ex)
        {
            return await MarkApprovedRecoveryRequiresReviewAsync(
                attempt,
                firstTransaction?.ResponseCode ?? authorization.ResponseCode,
                firstTransaction?.ResponseText ?? authorization.ResponseText,
                cardTender.Reference,
                BuildDialogDetails(attempt, authorization),
                ex,
                cancellationToken);
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
        catch (Exception ex)
        {
            return await MarkApprovedRecoveryRequiresReviewAsync(
                attempt,
                firstTransaction?.ResponseCode ?? authorization.ResponseCode,
                firstTransaction?.ResponseText ?? authorization.ResponseText,
                cardTender.Reference,
                BuildDialogDetails(attempt, authorization),
                ex,
                cancellationToken);
        }

        var hasPostCommitWarning = false;
        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkOrderCompletedAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            hasPostCommitWarning = true;
            ConsoleLog.Write(
                "CardRecovery",
                $"approved local order saved but attempt finalization failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
        }

        var pendingSyncCount = currentSession.PendingSyncCount;
        try
        {
            pendingSyncCount = await RunLocalStoreAsync(
                () => syncQueueRepository.CountPendingAsync(CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            hasPostCommitWarning = true;
            ConsoleLog.Write(
                "CardRecovery",
                $"approved local order saved but pending sync refresh failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
        }
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            string.Empty,
            order,
            tenders.Sum(tender => tender.Amount),
            checkoutResult.ChangeAmount,
            currentSession with { PendingSyncCount = pendingSyncCount },
            BuildDialogDetails(attempt, authorization),
            HasPostCommitWarning: hasPostCommitWarning);
    }

    private async Task<bool> TryPersistApprovedOutcomeAsync(
        LocalCardPaymentAttempt attempt,
        string? responseCode,
        string? responseText,
        string? paymentReference)
    {
        if (IsSupervisorResolvedPayment(attempt))
        {
            return true;
        }

        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.UpdateOutcomeAsync(
                    attempt.AttemptGuid,
                    LocalCardPaymentAttemptStatus.Approved,
                    responseCode,
                    responseText,
                    paymentReference,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"approved outcome persistence failed before order save attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return false;
        }
    }

    private async Task<CardPaymentRecoveryResult> RestoreApprovedTenderAsync(
        PosCartService cart,
        CardPaymentOrderDraft draft,
        LocalCardPaymentAttempt attempt,
        string? responseCode,
        string? responseText,
        string? paymentReference,
        IReadOnlyList<PaymentTender> tenders,
        CardPaymentRecoveryDialogDetails dialogDetails,
        CancellationToken cancellationToken)
    {
        if (!IsSupervisorResolvedPayment(attempt))
        {
            if (!await TryPersistApprovedOutcomeAsync(
                    attempt,
                    responseCode,
                    responseText,
                    paymentReference))
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.Unknown,
                    T("cardRecovery.linkly.approvedRecoveryRequiresReview", "The previous card payment was approved, but POS could not safely rebuild the order. Ask a supervisor to confirm the payment before continuing."),
                    DialogDetails: dialogDetails,
                    PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
            }
        }

        if (!cart.IsEmpty)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.currentCartNotEmpty", "The previous card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order."),
                DialogDetails: dialogDetails);
        }

        // 中文注释：终端已批准但未覆盖整单时，只恢复购物车和 tender，让收银员在付款页补齐差额。
        cart.RestoreSnapshot(draft.CartSnapshot);
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.DraftRestored,
            T("cardRecovery.linkly.approvedTenderRestored", "The previous card payment was approved and restored as a tender. Complete the remaining payment amount before finishing the order."),
            TenderedAmount: tenders.Sum(tender => tender.Amount),
            DialogDetails: dialogDetails,
            RestoredTenders: tenders);
    }

    private async Task<CardPaymentRecoveryResult> MarkApprovedRecoveryRequiresReviewAsync(
        LocalCardPaymentAttempt attempt,
        string? responseCode,
        string? responseText,
        string? paymentReference,
        CardPaymentRecoveryDialogDetails dialogDetails,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ConsoleLog.Write(
            "CardRecovery",
            $"recover approved draft invalid attemptGuid={attempt.AttemptGuid} error={exception.GetType().Name} message={exception.Message}");

        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.UpdateOutcomeAsync(
                    attempt.AttemptGuid,
                    LocalCardPaymentAttemptStatus.RequiresReview,
                    responseCode,
                    responseText ?? exception.Message,
                    paymentReference,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
        }
        catch (Exception stateException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"approved recovery review state save failed attemptGuid={attempt.AttemptGuid} error={stateException.GetType().Name}");
        }

        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.linkly.approvedRecoveryRequiresReview", "The previous card payment was approved, but POS could not safely rebuild the order. Ask a supervisor to confirm the payment before continuing."),
            DialogDetails: dialogDetails);
    }

    private static bool IsApprovedTenderPartial(CardPaymentOrderDraft draft, IReadOnlyList<PaymentTender> tenders)
    {
        var actualAmount = RoundCurrency(draft.ActualAmount);
        var tenderTotal = RoundCurrency(tenders.Sum(tender => tender.Amount));

        if (actualAmount > 0m)
        {
            return tenderTotal > 0m && tenderTotal < actualAmount;
        }

        if (actualAmount < 0m)
        {
            return tenderTotal < 0m && tenderTotal > actualAmount;
        }

        return false;
    }

    private static decimal RoundCurrency(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static CardPaymentRecoveryDialogDetails BuildDialogDetails(
        LocalCardPaymentAttempt attempt,
        LinklyCloudBackendSessionResponse? status = null)
    {
        return new CardPaymentRecoveryDialogDetails(
            NormalizeOptional(status?.SessionId) ?? NormalizeOptional(attempt.SessionId),
            NormalizeOptional(status?.TxnRef) ?? NormalizeOptional(attempt.TxnRef),
            status?.ResponseCode,
            status?.ResponseText,
            attempt.Amount,
            DateTimeOffset.Now);
    }

    private static CardPaymentRecoveryDialogDetails BuildDialogDetails(
        LocalCardPaymentAttempt attempt,
        PaymentAuthorizationResult authorization)
    {
        var transaction = authorization.CardTransactions?.FirstOrDefault();
        return new CardPaymentRecoveryDialogDetails(
            NormalizeOptional(authorization.SessionId) ?? NormalizeOptional(attempt.SessionId),
            ResolveAuthorizationTxnRef(authorization) ?? NormalizeOptional(attempt.TxnRef),
            transaction?.ResponseCode ?? authorization.ResponseCode,
            transaction?.ResponseText ?? authorization.ResponseText,
            authorization.AuthorizedAmount ?? attempt.Amount,
            DateTimeOffset.Now);
    }

    private static CardPaymentRecoveryDialogDetails? BuildDialogDetails(LinklyCloudBackendSessionResponse? status)
    {
        if (status is null)
        {
            return null;
        }

        return new CardPaymentRecoveryDialogDetails(
            NormalizeOptional(status.SessionId),
            NormalizeOptional(status.TxnRef),
            status.ResponseCode,
            status.ResponseText,
            null,
            DateTimeOffset.Now);
    }

    private static CardPaymentRecoveryBankReceipt? BuildActiveSessionBankReceipt(
        LinklyCloudBackendSessionResponse status,
        LinklyBankReceiptKind kind)
    {
        // 认证恢复证据优先使用后端汇总 ReceiptText，缺失时回退到 receipt notification。
        var receiptText = ReadReceiptText(status);
        if (receiptText is null)
        {
            return null;
        }

        return new CardPaymentRecoveryBankReceipt(
            status.Environment,
            status.SessionId,
            receiptText,
            kind,
            status.ResponseCode,
            status.ResponseText);
    }

    private static string BuildLocalPaymentReference(
        LocalCardPaymentAttempt attempt,
        PaymentAuthorizationResult authorization)
    {
        return NormalizeOptional(authorization.Reference) ??
            $"ANZ:{ResolveAuthorizationTxnRef(authorization) ?? NormalizeOptional(attempt.TxnRef) ?? attempt.AttemptGuid.ToString("N")}";
    }

    private static IReadOnlyList<CardTransactionDto> BuildLocalCardTransactions(
        LocalCardPaymentAttempt attempt,
        PaymentAuthorizationResult authorization,
        decimal amount)
    {
        if (authorization.CardTransactions is { Count: > 0 } transactions)
        {
            return transactions;
        }

        var txnRef = ResolveAuthorizationTxnRef(authorization) ??
            NormalizeOptional(attempt.TxnRef) ??
            attempt.AttemptGuid.ToString("N");
        return
        [
            new CardTransactionDto(
                "ANZ",
                txnRef,
                null,
                null,
                null,
                null,
                null,
                authorization.ResponseCode,
                authorization.ResponseText ?? authorization.Message,
                null,
                DateTimeOffset.UtcNow,
                Math.Abs(amount),
                null)
        ];
    }

    private async Task RetryCompletedAttemptAcknowledgeAsync(
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        ConsoleLog.Write(
            "CardRecovery",
            $"recover acknowledge retry attemptGuid={attempt.AttemptGuid} sessionId={LogValue(attempt.SessionId)} txnRef={LogValue(attempt.TxnRef)}");
        await TryAcknowledgeAsync(settings, attempt, attempt.SessionId!, attempt.TxnRef, cancellationToken);
    }

    private async Task<bool> TryAcknowledgeAsync(
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt,
        string sessionId,
        string? txnRef,
        CancellationToken cancellationToken)
    {
        try
        {
            await backendTerminalClient.AcknowledgeSessionAsync(settings, sessionId, cancellationToken);
            await RunLocalStoreAsync(
                () => attemptRepository.MarkAcknowledgedAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken),
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 鏈湴璁㈠崟/鑽夌鎭㈠宸茬粡瀹屾垚锛宎ck 澶辫触鍙奖鍝?backend 娓呯悊锛屼笉鑳介樆鏂惎鍔ㄤ綋楠屻€?
            ConsoleLog.Write(
                "CardRecovery",
                $"recover acknowledge failed attemptGuid={attempt.AttemptGuid} sessionId={LogValue(sessionId)} txnRef={LogValue(txnRef)} error={ex.GetType().Name}");
            return false;
        }
    }

    private async Task<bool> TryAcknowledgeActiveSessionAsync(
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse status,
        LocalCardPaymentAttempt? attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            await backendTerminalClient.AcknowledgeSessionAsync(settings, status.SessionId, cancellationToken);
            if (attempt is not null)
            {
                await RunLocalStoreAsync(
                    () => attemptRepository.MarkAcknowledgedAsync(
                        attempt.AttemptGuid,
                        DateTimeOffset.UtcNow,
                        CancellationToken.None),
                    CancellationToken.None);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover active-session acknowledge failed sessionId={LogValue(status.SessionId)} txnRef={LogValue(status.TxnRef)} error={ex.GetType().Name}");
            return false;
        }
    }

    private async Task<LocalCardPaymentAttempt> PersistActiveSessionAsync(
        CardTerminalSettings settings,
        PosSessionState session,
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(status.SessionId))
        {
            throw new InvalidOperationException("Linkly active session does not contain a SessionId.");
        }

        var now = DateTimeOffset.UtcNow;
        var attempt = new LocalCardPaymentAttempt(
            Guid.NewGuid(),
            status.SessionId.Trim(),
            NormalizeOptional(status.TxnRef),
            CardProcessorKind.Linkly.ToString(),
            settings.Environment.ToString(),
            nameof(LinklyConnectionMode.CloudBackendAsync),
            "P",
            0m,
            LocalCardPaymentAttemptStatus.Recovering,
            "{}",
            session.StoreCode,
            session.DeviceCode,
            session.CashierId,
            status.ResponseCode,
            status.ResponseText,
            null,
            now,
            now,
            null,
            null,
            OperationKind: "ActiveSession",
            OperationGuid: Guid.NewGuid());
        return await RunLocalStoreAsync(
            () => attemptRepository.CreateOrGetActiveSessionAsync(attempt, CancellationToken.None),
            CancellationToken.None);
    }

    private async Task<bool> TrySaveActiveSessionOutcomeAsync(
        LocalCardPaymentAttempt? attempt,
        LocalCardPaymentAttemptStatus outcome,
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        if (attempt is null)
        {
            return false;
        }

        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.UpdateOutcomeAsync(
                    attempt.AttemptGuid,
                    outcome,
                    status.ResponseCode,
                    status.ResponseText,
                    NormalizeOptional(status.TxnRef) ?? attempt.PaymentReference,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"active-session outcome persistence failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return false;
        }
    }

    private async Task<bool> CompleteSupervisorAcknowledgeAsync(
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt,
        LinklyConnectionMode mode,
        CancellationToken cancellationToken)
    {
        var sessionId = NormalizeOptional(attempt.SessionId);
        if (mode == LinklyConnectionMode.CloudBackendAsync && sessionId is not null)
        {
            return await TryAcknowledgeAsync(
                settings,
                attempt,
                sessionId,
                attempt.TxnRef,
                cancellationToken);
        }

        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkAcknowledgedAsync(
                    attempt.AttemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"supervisor payment local acknowledge failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return false;
        }
    }

    private async Task<CardPaymentRecoveryResult> AddLocalSupervisorAcknowledgeWarningAsync(
        LocalCardPaymentAttempt attempt,
        CardPaymentRecoveryResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunLocalStoreAsync(
                () => attemptRepository.MarkAcknowledgedAsync(
                    attempt.AttemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
            return result;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"supervisor-approved local payment saved but acknowledge marker failed attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return result with { HasPostCommitWarning = true };
        }
    }

    private static bool IsSupervisorResolvedPayment(LocalCardPaymentAttempt? attempt) =>
        attempt is not null &&
        (string.Equals(
             attempt.ResponseCode,
             ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
             StringComparison.Ordinal) ||
         string.Equals(
             attempt.ResponseCode,
             ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
             StringComparison.Ordinal));

    private static bool IsSupervisorResolvedActiveSession(LocalCardPaymentAttempt? attempt) =>
        attempt is not null &&
        string.Equals(attempt.OperationKind, "ActiveSession", StringComparison.Ordinal) &&
        IsSupervisorResolvedPayment(attempt);

    private static CardPaymentSupervisorDetails? BuildPaymentSupervisorDetails(
        LocalCardPaymentAttempt? attempt)
    {
        var sessionId = NormalizeOptional(attempt?.SessionId) ?? NormalizeOptional(attempt?.TxnRef);
        return attempt is null || sessionId is null
            ? null
            : new CardPaymentSupervisorDetails(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                sessionId,
                attempt.OperationGuid,
                attempt.Status,
                attempt.UpdatedAt);
    }

    private CardPaymentRecoveryResult BuildUnresolvedActiveSessionResult(
        LocalCardPaymentAttempt? attempt,
        string message)
    {
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            message,
            DialogDetails: attempt is null ? null : BuildDialogDetails(attempt),
            PaymentSupervisorDetails: BuildPaymentSupervisorDetails(attempt));
    }

    private static CardPaymentOrderDraft? TryDeserializeDraft(LocalCardPaymentAttempt attempt)
    {
        if (string.Equals(attempt.OperationKind, "ActiveSession", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attempt.OrderDraftJson))
        {
            return null;
        }

        try
        {
            var draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(
                attempt.OrderDraftJson,
                JsonOptions);
            return draft is not null &&
                draft.OrderGuid != Guid.Empty &&
                draft.Session is not null &&
                draft.CartSnapshot is not null
                    ? draft
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LinklyCloudBackendSessionResponse BuildSupervisorApprovedStatus(
        LocalCardPaymentAttempt attempt)
    {
        return new LinklyCloudBackendSessionResponse(
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.SessionId ?? attempt.TxnRef ?? attempt.AttemptGuid.ToString("N"),
            StatusCompleted,
            attempt.TxnRef,
            "00",
            attempt.ResponseText,
            null,
            null,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            0,
            null,
            null,
            null,
            [],
            true);
    }

    private static LocalFinancialSupervisorResolution BuildPaymentSupervisorJournal(
        LocalCardPaymentAttempt attempt,
        string sessionKey,
        CardPaymentSupervisorResolution resolution,
        DateTimeOffset resolvedAt)
    {
        var resolutionGuid = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var auditEvent = new OperationAuditEventDto
        {
            EventId = auditEventId,
            OccurredAtUtc = resolvedAt,
            OperationType = "CARD_PAYMENT_SUPERVISOR_RESOLUTION",
            Outcome = "Succeeded",
            CashierId = resolution.OperatorCashierId,
            UserGuid = resolution.OperatorUserGuid,
            CashierName = resolution.OperatorName,
            StoreCode = attempt.StoreCode,
            DeviceCode = attempt.DeviceCode,
            CorrelationId = attempt.AttemptGuid.ToString("D"),
            PaymentMethod = attempt.Processor,
            ReasonCode = resolution.Decision.ToString(),
            SafeMessage = resolution.Reason,
            PaymentAmount = Math.Abs(attempt.Amount),
            Properties = new Dictionary<string, string?>
            {
                ["attemptGuid"] = attempt.AttemptGuid.ToString("D"),
                ["operationGuid"] = attempt.OperationGuid?.ToString("D"),
                ["sessionId"] = sessionKey,
                ["evidence"] = resolution.Evidence,
                ["financialReference"] = resolution.PaymentReference
            }
        };
        return new LocalFinancialSupervisorResolution(
            resolutionGuid,
            LocalFinancialSupervisorResolutionTarget.ActiveSession,
            attempt.Processor,
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.AttemptGuid,
            null,
            attempt.OperationGuid,
            sessionKey,
            resolution.Decision.ToString(),
            resolution.OperatorCashierId,
            resolution.OperatorUserGuid,
            resolution.OperatorName,
            resolution.Reason,
            resolution.Evidence,
            resolution.PaymentReference,
            null,
            resolvedAt,
            auditEventId,
            JsonSerializer.Serialize(auditEvent, JsonOptions));
    }

    private static LocalFinancialSupervisorResolution BuildRefundSupervisorJournal(
        LocalCardPaymentAttempt attempt,
        CardRefundSupervisorResolution resolution,
        PosSessionState session,
        string? retryTxnRef,
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
            PaymentMethod = attempt.Processor,
            ReasonCode = resolution.Decision.ToString(),
            SafeMessage = resolution.Reason,
            PaymentAmount = Math.Abs(attempt.Amount),
            Properties = new Dictionary<string, string?>
            {
                ["attemptGuid"] = attempt.AttemptGuid.ToString("D"),
                ["operationGuid"] = attempt.OperationGuid?.ToString("D"),
                ["evidence"] = resolution.Evidence,
                ["financialReference"] = resolution.RefundReference,
                ["retryReference"] = retryTxnRef
            }
        };
        return new LocalFinancialSupervisorResolution(
            resolutionGuid,
            LocalFinancialSupervisorResolutionTarget.CardRefund,
            attempt.Processor,
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.AttemptGuid,
            null,
            attempt.OperationGuid,
            attempt.SessionId,
            resolution.Decision.ToString(),
            operatorCashierId,
            operatorUserGuid,
            operatorName,
            resolution.Reason,
            resolution.Evidence,
            resolution.RefundReference,
            retryTxnRef,
            resolvedAt,
            auditEventId,
            JsonSerializer.Serialize(auditEvent, JsonOptions));
    }

    private async Task<LocalCardPaymentAttempt> BindRecoveredSessionAsync(
        LocalCardPaymentAttempt attempt,
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        if (!CanBindRecoveredSession(attempt, status))
        {
            return attempt;
        }

        var now = DateTimeOffset.UtcNow;
        await RunLocalStoreAsync(
            () => attemptRepository.UpdateSessionAsync(
                attempt.AttemptGuid,
                status.SessionId,
                status.TxnRef,
                now,
                cancellationToken),
            cancellationToken);
        ConsoleLog.Write(
            "CardRecovery",
            $"recover session bound attemptGuid={attempt.AttemptGuid} sessionId={LogValue(status.SessionId)} txnRef={LogValue(status.TxnRef)} status={status.Status}");
        return attempt with
        {
            SessionId = status.SessionId,
            TxnRef = status.TxnRef ?? attempt.TxnRef,
            Status = LocalCardPaymentAttemptStatus.SessionStarted,
            UpdatedAt = now
        };
    }

    private static CardPaymentOrderDraft DeserializeDraft(LocalCardPaymentAttempt attempt)
    {
        return JsonSerializer.Deserialize<CardPaymentOrderDraft>(attempt.OrderDraftJson, JsonOptions)
            ?? throw new InvalidOperationException("Card payment recovery draft is invalid.");
    }

    private static bool IsFinal(LinklyCloudBackendSessionResponse status)
    {
        return string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, StatusFailed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, StatusNotSubmitted, StringComparison.OrdinalIgnoreCase) ||
            IsCancelledStatus(status.Status);
    }

    private static bool IsApproved(LinklyCloudBackendSessionResponse status)
    {
        return string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase) &&
            status.TransactionSuccess == true;
    }

    private static bool IsDeclinedOrFailed(LinklyCloudBackendSessionResponse status)
    {
        if (string.Equals(status.Status, StatusCompleted, StringComparison.OrdinalIgnoreCase))
        {
            return status.TransactionSuccess == false;
        }

        return IsFinal(status);
    }

    private static bool IsCancelledStatus(string? status)
    {
        // Linkly 后端可能用英式或美式拼写表示收银员取消成功；都应视为可清除终态。
        return string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanBindRecoveredSession(
        LocalCardPaymentAttempt attempt,
        LinklyCloudBackendSessionResponse status)
    {
        if (!TextEquals(attempt.Environment, status.Environment) ||
            !TextEquals(attempt.StoreCode, status.StoreCode) ||
            !TextEquals(attempt.DeviceCode, status.DeviceCode))
        {
            return false;
        }

        var attemptSessionId = NormalizeOptional(attempt.SessionId);
        var statusSessionId = NormalizeOptional(status.SessionId);
        var attemptTxnRef = NormalizeOptional(attempt.TxnRef);
        var statusTxnRef = NormalizeOptional(status.TxnRef);

        if (attemptSessionId is not null && !TextEquals(attemptSessionId, statusSessionId))
        {
            return false;
        }

        if (attemptTxnRef is not null && statusTxnRef is not null && !TextEquals(attemptTxnRef, statusTxnRef))
        {
            return false;
        }

        if (attemptSessionId is not null && attemptTxnRef is null && statusTxnRef is not null)
        {
            return true;
        }

        return attemptTxnRef is not null &&
            attemptSessionId is null &&
            statusTxnRef is not null &&
            TextEquals(attemptTxnRef, statusTxnRef);
    }

    private static bool StatusMatchesAttempt(
        LocalCardPaymentAttempt attempt,
        LinklyCloudBackendSessionResponse status,
        bool statusFromResumable,
        out string mismatchReason)
    {
        if (!TextEquals(attempt.Environment, status.Environment))
        {
            mismatchReason = "environment-mismatch";
            return false;
        }

        var attemptSessionId = NormalizeOptional(attempt.SessionId);
        var statusSessionId = NormalizeOptional(status.SessionId);
        if (attemptSessionId is not null)
        {
            if (!TextEquals(attemptSessionId, statusSessionId))
            {
                mismatchReason = "session-id-mismatch";
                return false;
            }
        }
        else if (!statusFromResumable)
        {
            mismatchReason = "missing-attempt-session";
            return false;
        }

        var attemptTxnRef = NormalizeOptional(attempt.TxnRef);
        var statusTxnRef = NormalizeOptional(status.TxnRef);
        if (attemptTxnRef is not null)
        {
            if (statusTxnRef is null || !TextEquals(attemptTxnRef, statusTxnRef))
            {
                mismatchReason = "txn-ref-mismatch";
                return false;
            }
        }
        else if (attemptSessionId is null)
        {
            // 鏈湴娌℃湁 sessionId 鏃跺彧鑳介€氳繃 txnRef 缁戝畾 backend resumable锛岀己澶卞垯涓嶈兘鑷姩澶勭悊銆?
            mismatchReason = "missing-recoverable-binding";
            return false;
        }

        mismatchReason = string.Empty;
        return true;
    }

    private static LocalCardPaymentAttemptStatus MapFailureStatus(LinklyCloudBackendSessionResponse status)
    {
        var text = $"{status.Status} {status.ResponseText}".ToUpperInvariant();
        if (text.Contains("TIMEOUT", StringComparison.Ordinal))
        {
            return LocalCardPaymentAttemptStatus.TimedOut;
        }

        if (text.Contains("CANCEL", StringComparison.Ordinal))
        {
            return LocalCardPaymentAttemptStatus.Cancelled;
        }

        if (text.Contains("DECLIN", StringComparison.Ordinal))
        {
            return LocalCardPaymentAttemptStatus.Declined;
        }

        return LocalCardPaymentAttemptStatus.Failed;
    }

    private static LinklyConnectionMode ResolveAttemptConnectionMode(
        LocalCardPaymentAttempt attempt,
        LinklyConnectionMode fallback)
    {
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(attempt.ConnectionMode, fallback);
        // 本地 LocalIp attempt 不会产生 backend session；若已有 SessionId，说明实际交易已进入 CloudBackendAsync。
        return mode == LinklyConnectionMode.LocalIp && !string.IsNullOrWhiteSpace(attempt.SessionId)
            ? LinklyConnectionMode.CloudBackendAsync
            : mode;
    }

    private static bool HasLocalFinalResult(PaymentAuthorizationResult authorization)
    {
        var transaction = authorization.CardTransactions?.FirstOrDefault();
        return !authorization.ResultUnknown &&
            (!string.IsNullOrWhiteSpace(authorization.Reference) ||
                !string.IsNullOrWhiteSpace(authorization.ResponseCode) ||
                !string.IsNullOrWhiteSpace(authorization.ResponseText) ||
                !string.IsNullOrWhiteSpace(transaction?.TxnRef) ||
                !string.IsNullOrWhiteSpace(transaction?.ResponseCode) ||
                !string.IsNullOrWhiteSpace(transaction?.ResponseText));
    }

    private static bool LocalAuthorizationMatchesAttempt(
        LocalCardPaymentAttempt attempt,
        PaymentAuthorizationResult authorization)
    {
        var expectedTxnRef = NormalizeLinklyReference(attempt.TxnRef);
        var actualTxnRef = ResolveAuthorizationTxnRef(authorization);
        return expectedTxnRef is not null &&
            actualTxnRef is not null &&
            TextEquals(expectedTxnRef, actualTxnRef);
    }

    private static LocalCardPaymentAttemptStatus MapLocalFailureStatus(PaymentAuthorizationResult authorization)
    {
        var transaction = authorization.CardTransactions?.FirstOrDefault();
        var responseCode = NormalizeOptional(transaction?.ResponseCode) ?? NormalizeOptional(authorization.ResponseCode);
        if (IsTimeoutResponseCode(responseCode))
        {
            return LocalCardPaymentAttemptStatus.TimedOut;
        }

        if (IsCancelResponseCode(responseCode))
        {
            return LocalCardPaymentAttemptStatus.Cancelled;
        }

        if (IsDeclineResponseCode(responseCode))
        {
            return LocalCardPaymentAttemptStatus.Declined;
        }

        var text = $"{authorization.Message} {authorization.ResponseText} {transaction?.ResponseText}".ToUpperInvariant();
        if (text.Contains("TIMEOUT", StringComparison.Ordinal))
        {
            return LocalCardPaymentAttemptStatus.TimedOut;
        }

        if (text.Contains("CANCEL", StringComparison.Ordinal))
        {
            return LocalCardPaymentAttemptStatus.Cancelled;
        }

        if (text.Contains("DECLIN", StringComparison.Ordinal))
        {
            return LocalCardPaymentAttemptStatus.Declined;
        }

        return LocalCardPaymentAttemptStatus.Failed;
    }

    private static bool CanRecoverBackendActiveSession(CardTerminalSettings settings)
    {
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode);
        if (mode == LinklyConnectionMode.CloudBackendAsync)
        {
            return true;
        }

        return settings.LinklyConnectionModePriority is { Count: > 0 } priority &&
            CardTerminalSettings.NormalizeLinklyConnectionModePriority(priority, mode)
                .Contains(LinklyConnectionMode.CloudBackendAsync);
    }

    private static bool IsDeclineResponseCode(string? responseCode)
    {
        return !string.IsNullOrWhiteSpace(responseCode) &&
            !LinklyApprovalResponseCodes.IsApproved(responseCode) &&
            !IsCancelResponseCode(responseCode) &&
            !IsTimeoutResponseCode(responseCode);
    }

    private static bool IsCancelResponseCode(string? responseCode)
    {
        return string.Equals(responseCode, "C0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(responseCode, "CA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(responseCode, "CANCEL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(responseCode, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(responseCode, "CANCELED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTimeoutResponseCode(string? responseCode)
    {
        return string.Equals(responseCode, "TO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(responseCode, "TIMEOUT", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveAuthorizationTxnRef(PaymentAuthorizationResult authorization)
    {
        return NormalizeLinklyReference(authorization.TxnRef) ??
            NormalizeLinklyReference(authorization.CardTransactions?.FirstOrDefault()?.TxnRef) ??
            NormalizeLinklyReference(authorization.Reference);
    }

    private static string? NormalizeLinklyReference(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.StartsWith("ANZ:", StringComparison.OrdinalIgnoreCase)
            ? NormalizeOptional(normalized[4..])
            : normalized;
    }

    private static string BuildPaymentReference(
        LocalCardPaymentAttempt attempt,
        LinklyCloudBackendSessionResponse status)
    {
        if (!string.IsNullOrWhiteSpace(attempt.PaymentReference))
        {
            return attempt.PaymentReference;
        }

        var txnRef = status.TxnRef ?? attempt.TxnRef ?? status.SessionId;
        return LinklyBackendPaymentReference.Format(txnRef, status.SessionId, status.Environment, TryReadRefundReference(status));
    }

    private static string? TryReadRefundReference(LinklyCloudBackendSessionResponse status)
    {
        foreach (var notification in (status.Notifications ?? []).Reverse())
        {
            if (!string.Equals(notification.Type, "transaction", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(notification.PayloadJson))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(notification.PayloadJson);
                var response = ReadResponse(document.RootElement);
                var purchaseAnalysisData = ReadValue(response, "PurchaseAnalysisData");
                // 官方 GET transaction payload 里 RFN 是后续退款和认证证据链的关键引用。
                return TryReadRefundReferenceValue(purchaseAnalysisData) ??
                    TryReadRefundReferenceValue(response) ??
                    TryReadRefundReferenceValue(document.RootElement);
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return null;
    }

    private static string? ReadReceiptText(LinklyCloudBackendSessionResponse status)
    {
        return NormalizeOptional(status.ReceiptText) ?? ReadReceiptText(status.Notifications ?? []);
    }

    private static string? ReadReceiptText(IReadOnlyList<LinklyCloudBackendNotificationDto> notifications)
    {
        var receipts = notifications
            .Where(notification => string.Equals(notification.Type, "receipt", StringComparison.OrdinalIgnoreCase))
            .Select(notification => ReadReceiptNotification(notification.PayloadJson))
            .Where(receipt => !string.IsNullOrWhiteSpace(receipt))
            .Select(receipt => receipt!)
            .ToArray();

        return receipts.Length == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, receipts);
    }

    private static string? ReadReceiptNotification(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return ReadReceiptText(document.RootElement) ?? ReadReceiptText(ReadResponse(document.RootElement));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadReceiptText(JsonElement element)
    {
        if (!TryGetProperty(element, "ReceiptText", out var receipt))
        {
            return null;
        }

        return receipt.ValueKind == JsonValueKind.String
            ? NormalizeOptional(receipt.GetString())
            : null;
    }

    private static JsonElement ReadResponse(JsonElement root)
    {
        return TryGetProperty(root, "Response", out var response) ? response : root;
    }

    private static JsonElement ReadValue(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) ? value : default;
    }

    private static string? TryReadRefundReferenceValue(JsonElement element, bool allowScalar = false)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => TryReadRefundReferenceObject(element),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => TryReadRefundReferenceValue(item))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            JsonValueKind.String when allowScalar => NormalizeOptional(element.GetString()),
            _ => null
        };
    }

    private static string? TryReadRefundReferenceObject(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "RFN", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadRefundReferenceValue(property.Value, allowScalar: true);
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            var value = TryReadRefundReferenceValue(property.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static CardTransactionDto BuildCardTransaction(
        LocalCardPaymentAttempt attempt,
        LinklyCloudBackendSessionResponse status,
        decimal amount)
    {
        return new CardTransactionDto(
            "ANZ",
            status.TxnRef ?? attempt.TxnRef ?? status.SessionId,
            null,
            null,
            null,
            null,
            null,
            status.ResponseCode,
            status.ResponseText,
            null,
            DateTimeOffset.UtcNow,
            Math.Abs(amount),
            status.ReceiptText);
    }

    private static void LogRecoveryScan(
        CardTerminalSettings settings,
        PosSessionState session,
        LocalCardPaymentAttempt? attempt)
    {
        LinklyJsonLog.Write(
            "CardRecovery",
            "card-recovery",
            "startup-recovery",
            "scan",
            environment: settings.Environment,
            sessionId: NormalizeOptional(attempt?.SessionId),
            success: attempt is not null,
            reason: attempt is null ? "no-open-attempt" : null,
            details: new
            {
                timestamp = DateTimeOffset.Now,
                pendingAttemptFound = attempt is not null,
                storeCode = session.StoreCode,
                deviceCode = session.DeviceCode,
                cashierId = session.CashierId,
                requestedCashierId = (string?)null,
                attemptCashierId = attempt?.CashierId,
                selectedEnvironment = settings.Environment.ToString(),
                certCase = "4.1.1",
                transactionReference = NormalizeOptional(attempt?.SessionId) ?? NormalizeOptional(attempt?.TxnRef),
                attemptGuid = attempt?.AttemptGuid,
                localStatus = attempt?.Status.ToString(),
                attemptSessionId = NormalizeOptional(attempt?.SessionId),
                txnRef = NormalizeOptional(attempt?.TxnRef),
                txnType = attempt?.TxnType,
                amount = attempt?.Amount,
                createdAt = attempt?.CreatedAt,
                updatedAt = attempt?.UpdatedAt
            });
    }

    private static void LogRecoveryMarkedRecovering(
        CardTerminalSettings settings,
        LocalCardPaymentAttempt attempt)
    {
        LinklyJsonLog.Write(
            "CardRecovery",
            "card-recovery",
            "startup-recovery",
            "marked-recovering",
            environment: settings.Environment,
            sessionId: NormalizeOptional(attempt.SessionId),
            details: new
            {
                timestamp = DateTimeOffset.Now,
                attemptGuid = attempt.AttemptGuid,
                certCase = "4.1.2",
                transactionReference = NormalizeOptional(attempt.SessionId) ?? NormalizeOptional(attempt.TxnRef),
                localStatus = attempt.Status.ToString(),
                attemptSessionId = NormalizeOptional(attempt.SessionId),
                txnRef = NormalizeOptional(attempt.TxnRef),
                txnType = attempt.TxnType,
                amount = attempt.Amount,
                storeCode = attempt.StoreCode,
                deviceCode = attempt.DeviceCode,
                cashierId = attempt.CashierId
            });
    }

    private static void LogRecoveryResult(
        CardTerminalSettings settings,
        LocalCardPaymentAttempt? attempt,
        LinklyCloudBackendSessionResponse? status,
        CardPaymentRecoveryOutcome outcome,
        string reason,
        string? error = null)
    {
        LinklyJsonLog.Write(
            "CardRecovery",
            "card-recovery",
            "startup-recovery",
            "result",
            environment: settings.Environment,
            sessionId: NormalizeOptional(attempt?.SessionId) ?? NormalizeOptional(status?.SessionId),
            success: outcome is CardPaymentRecoveryOutcome.OrderCompleted or CardPaymentRecoveryOutcome.DraftRestored,
            reason: reason,
            response: status is null
                ? null
                : new
                {
                    environment = status.Environment,
                    storeCode = status.StoreCode,
                    deviceCode = status.DeviceCode,
                    sessionId = status.SessionId,
                    status = status.Status,
                    txnRef = NormalizeOptional(status.TxnRef),
                    responseCode = status.ResponseCode,
                    responseText = status.ResponseText,
                    recoveryAction = status.RecoveryAction,
                    lastHttpStatus = status.LastHttpStatus
                },
            details: new
            {
                timestamp = DateTimeOffset.Now,
                outcome = outcome.ToString(),
                certCase = GetRecoveryCertificationCase(outcome, reason),
                error,
                attemptGuid = attempt?.AttemptGuid,
                transactionReference = NormalizeOptional(attempt?.SessionId) ??
                    NormalizeOptional(status?.SessionId) ??
                    NormalizeOptional(attempt?.TxnRef) ??
                    NormalizeOptional(status?.TxnRef),
                localStatus = attempt?.Status.ToString(),
                attemptSessionId = NormalizeOptional(attempt?.SessionId),
                statusSessionId = NormalizeOptional(status?.SessionId),
                txnRef = NormalizeOptional(attempt?.TxnRef),
                statusTxnRef = NormalizeOptional(status?.TxnRef),
                txnType = attempt?.TxnType,
                amount = attempt?.Amount,
                storeCode = attempt?.StoreCode ?? status?.StoreCode,
                deviceCode = attempt?.DeviceCode ?? status?.DeviceCode,
                cashierId = attempt?.CashierId,
                responseCode = status?.ResponseCode,
                responseText = status?.ResponseText
            });
    }

    private static string GetRecoveryCertificationCase(CardPaymentRecoveryOutcome outcome, string reason)
    {
        if (reason.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("declined", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return "3.1.2/4.1.2";
        }

        return "4.1.2";
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(NormalizeOptional(left), NormalizeOptional(right), StringComparison.OrdinalIgnoreCase);
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
