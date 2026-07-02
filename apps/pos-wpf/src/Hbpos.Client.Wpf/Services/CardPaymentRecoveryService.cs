using System.Text.Json;
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

public sealed record CardPaymentRecoveryResult(
    CardPaymentRecoveryOutcome Outcome,
    string Message,
    LocalOrder? Order = null,
    decimal TenderedAmount = 0m,
    decimal ChangeAmount = 0m,
    PosSessionState? UpdatedSession = null,
    CardPaymentRecoveryDialogDetails? DialogDetails = null,
    CardPaymentRecoveryBankReceipt? BankReceipt = null)
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
}

public sealed class CardPaymentRecoveryService(
    ILocalCardPaymentAttemptRepository attemptRepository,
    ICardTerminalSettingsProvider settingsProvider,
    ILinklyBackendTerminalClient backendTerminalClient,
    CashCheckoutService checkout,
    ILocalOrderRepository orderRepository,
    ISyncQueueRepository syncQueueRepository,
    ILocalizationService? localization = null,
    ILinklyTerminalClient? linklyTerminalClient = null) : ICardPaymentRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CardPaymentRecoveryResult> RecoverLatestAsync(
        PosCartService cart,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsProvider.GetSettingsAsync(cancellationToken);
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode);
        if (settings.Processor != CardProcessorKind.Linkly ||
            (mode != LinklyConnectionMode.CloudBackendAsync && mode != LinklyConnectionMode.LocalIp))
        {
            return CardPaymentRecoveryResult.None;
        }

        var attempt = await attemptRepository.GetLatestOpenAttemptAsync(
            session.StoreCode,
            session.DeviceCode,
            // 中文注释：断电/退出恢复属于同一终端安全检查，不能被重启后的当前收银员阻断。
            cashierId: null,
            settings.Environment.ToString(),
            cancellationToken);
        LogRecoveryScan(settings, session, attempt);
        if (attempt is null)
        {
            LogRecoveryResult(settings, null, null, CardPaymentRecoveryOutcome.None, "no-open-attempt");
            return CardPaymentRecoveryResult.None;
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
                DialogDetails: BuildDialogDetails(attempt));
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
        await attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken);
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
                DialogDetails: BuildDialogDetails(attempt, status));
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
                DialogDetails: BuildDialogDetails(attempt, status));
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
                DialogDetails: BuildDialogDetails(attempt, status));
        }

        if (status is null)
        {
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Checking, "status-null");
            return new CardPaymentRecoveryResult(CardPaymentRecoveryOutcome.Checking, checkingMessage);
        }

        if (!IsFinal(status))
        {
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Checking, "remote-status-not-final");
            return new CardPaymentRecoveryResult(CardPaymentRecoveryOutcome.Checking, checkingMessage);
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
                DialogDetails: BuildDialogDetails(attempt, status));
        }

        if (!cart.IsEmpty)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover deferred current-cart-not-empty attemptGuid={attempt.AttemptGuid} sessionId={LogValue(attempt.SessionId)} statusSessionId={LogValue(status.SessionId)} outcome={status.Status}");
            LogRecoveryResult(settings, attempt, status, CardPaymentRecoveryOutcome.Unknown, "current-cart-not-empty");
            // 宸茶ˉ缁?session 鐨勬仮澶嶉」浠嶉渶淇濇寔 Recovering锛岄伩鍏嶇敤鎴锋竻绌鸿喘鐗╄溅鍚庢棤娉曞啀娆℃仮澶嶃€?
            await attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.currentCartNotEmpty", "The previous card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order."),
                DialogDetails: BuildDialogDetails(attempt, status));
        }

        if (IsApproved(status))
        {
            var result = await CompleteApprovedAttemptAsync(cart, session, settings, attempt, draft, status, cancellationToken);
            LogRecoveryResult(settings, attempt, status, result.Outcome, "approved-order-completed");
            return result with { Message = T("cardRecovery.linkly.approved", "The previous card payment was successful. The order has been recovered and saved automatically.") };
        }

        if (IsDeclinedOrFailed(status))
        {
            cart.RestoreSnapshot(draft.CartSnapshot);
            await attemptRepository.UpdateOutcomeAsync(
                attempt.AttemptGuid,
                MapFailureStatus(status),
                status.ResponseCode,
                status.ResponseText,
                attempt.PaymentReference,
                DateTimeOffset.UtcNow,
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
            DialogDetails: BuildDialogDetails(attempt, status));
    }

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
                DialogDetails: BuildDialogDetails(attempt));
        }

        var draft = DeserializeDraft(attempt);
        await attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken);
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
                DialogDetails: BuildDialogDetails(attempt));
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
                DialogDetails: BuildDialogDetails(attempt, authorization));
        }

        if ((authorization.Approved || HasLocalFinalResult(authorization)) && !cart.IsEmpty)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover local-ip deferred current-cart-not-empty attemptGuid={attempt.AttemptGuid} txnRef={LogValue(txnRef)}");
            LogRecoveryResult(settings, attempt, null, CardPaymentRecoveryOutcome.Unknown, "current-cart-not-empty");
            await attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.currentCartNotEmpty", "The previous card result needs handling, but the current cart already contains items. Complete or clear the current cart before recovering the previous order."),
                DialogDetails: BuildDialogDetails(attempt, authorization));
        }

        if (authorization.Approved)
        {
            var result = await CompleteApprovedLocalAttemptAsync(cart, currentSession, attempt, draft, authorization, cancellationToken);
            LogRecoveryResult(settings, attempt, null, result.Outcome, "local-approved-order-completed");
            return result with { Message = T("cardRecovery.linkly.approved", "The previous card payment was successful. The order has been recovered and saved automatically.") };
        }

        if (HasLocalFinalResult(authorization))
        {
            var transaction = authorization.CardTransactions?.FirstOrDefault();
            var responseCode = transaction?.ResponseCode ?? authorization.ResponseCode;
            var responseText = transaction?.ResponseText ?? authorization.ResponseText ?? authorization.Message;
            cart.RestoreSnapshot(draft.CartSnapshot);
            await attemptRepository.UpdateOutcomeAsync(
                attempt.AttemptGuid,
                MapLocalFailureStatus(authorization),
                responseCode,
                responseText,
                authorization.Reference ?? attempt.PaymentReference,
                DateTimeOffset.UtcNow,
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
            DialogDetails: BuildDialogDetails(attempt, authorization));
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

        LinklyCloudBackendSessionResponse? status = null;
        try
        {
            status = await backendTerminalClient.GetResumableSessionAsync(settings, cancellationToken);
            if (status is null)
            {
                return new CardPaymentRecoveryResult(
                    CardPaymentRecoveryOutcome.None,
                    T("cardRecovery.linkly.noActiveSession", "No unfinished Linkly session was found. You can try the card payment again."));
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
                DialogDetails: BuildDialogDetails(status));
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
                DialogDetails: BuildDialogDetails(status));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"recover active-session failed sessionId={LogValue(status?.SessionId)} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.linkly.activeSessionUnknown", "The previous Linkly session cannot be confirmed. Ask a supervisor to check Linkly before charging again."),
                DialogDetails: BuildDialogDetails(status));
        }

        if (!IsFinal(status))
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Checking,
                T("cardRecovery.linkly.activeSessionStillPending", "The previous Linkly session is still pending. Try recovery again or ask a supervisor to check Linkly."),
                DialogDetails: BuildDialogDetails(status));
        }

        if (IsApproved(status))
        {
            // 付款页恢复只确认并清理上一笔 active session，不能把结果合并进当前购物车。
            if (!await TryAcknowledgeActiveSessionAsync(settings, status, cancellationToken))
            {
                return ActiveSessionAcknowledgeFailed(status);
            }

            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.ActiveSessionApproved,
                T("cardRecovery.linkly.activeSessionApprovedCleared", "The previous Linkly transaction was successful and has been cleared. Continue the current order."),
                DialogDetails: BuildDialogDetails(status),
                BankReceipt: BuildActiveSessionBankReceipt(status, LinklyBankReceiptKind.RecoveredApproved));
        }

        if (IsDeclinedOrFailed(status))
        {
            // 失败/未提交终态已可安全清理，收银员继续当前订单并按需重新刷卡。
            if (!await TryAcknowledgeActiveSessionAsync(settings, status, cancellationToken))
            {
                return ActiveSessionAcknowledgeFailed(status);
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
            DialogDetails: BuildDialogDetails(status));
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

    private CardPaymentRecoveryResult ActiveSessionAcknowledgeFailed(LinklyCloudBackendSessionResponse status)
    {
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.Unknown,
            T("cardRecovery.linkly.activeSessionAcknowledgeFailed", "The previous Linkly result was confirmed, but POS could not clear it with Linkly. Try recovery again or ask a supervisor before charging again."),
            DialogDetails: BuildDialogDetails(status));
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
        cart.RestoreSnapshot(draft.CartSnapshot);
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
        var checkoutResult = checkout.CreatePaymentOrder(cart, draft.Session, tenders, cashTenderedAmount);
        var order = checkoutResult.Order with { OrderGuid = draft.OrderGuid };
        var existingOrder = await orderRepository.GetOrderAsync(draft.OrderGuid, cancellationToken);
        if (existingOrder is null)
        {
            await orderRepository.SavePendingOrderAsync(order, cancellationToken);
        }
        else
        {
            order = existingOrder;
        }

        await attemptRepository.UpdateOutcomeAsync(
            attempt.AttemptGuid,
            LocalCardPaymentAttemptStatus.Approved,
            status.ResponseCode,
            status.ResponseText,
            cardTender.Reference,
            DateTimeOffset.UtcNow,
            cancellationToken);
        await attemptRepository.MarkOrderCompletedAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken);
        await TryAcknowledgeAsync(settings, attempt, status.SessionId, status.TxnRef, cancellationToken);
        cart.Clear();

        var pendingSyncCount = await syncQueueRepository.CountPendingAsync(cancellationToken);
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            string.Empty,
            order,
            tenders.Sum(tender => tender.Amount),
            checkoutResult.ChangeAmount,
            currentSession with { PendingSyncCount = pendingSyncCount },
            BuildDialogDetails(attempt, status));
    }

    private async Task<CardPaymentRecoveryResult> CompleteApprovedLocalAttemptAsync(
        PosCartService cart,
        PosSessionState currentSession,
        LocalCardPaymentAttempt attempt,
        CardPaymentOrderDraft draft,
        PaymentAuthorizationResult authorization,
        CancellationToken cancellationToken)
    {
        cart.RestoreSnapshot(draft.CartSnapshot);
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
        var checkoutResult = checkout.CreatePaymentOrder(cart, draft.Session, tenders, cashTenderedAmount);
        var order = checkoutResult.Order with { OrderGuid = draft.OrderGuid };
        var existingOrder = await orderRepository.GetOrderAsync(draft.OrderGuid, cancellationToken);
        if (existingOrder is null)
        {
            await orderRepository.SavePendingOrderAsync(order, cancellationToken);
        }
        else
        {
            order = existingOrder;
        }

        await attemptRepository.UpdateOutcomeAsync(
            attempt.AttemptGuid,
            LocalCardPaymentAttemptStatus.Approved,
            firstTransaction?.ResponseCode ?? authorization.ResponseCode,
            firstTransaction?.ResponseText ?? authorization.ResponseText,
            cardTender.Reference,
            DateTimeOffset.UtcNow,
            cancellationToken);
        await attemptRepository.MarkOrderCompletedAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken);
        cart.Clear();

        var pendingSyncCount = await syncQueueRepository.CountPendingAsync(cancellationToken);
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            string.Empty,
            order,
            tenders.Sum(tender => tender.Amount),
            checkoutResult.ChangeAmount,
            currentSession with { PendingSyncCount = pendingSyncCount },
            BuildDialogDetails(attempt, authorization));
    }

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
            await attemptRepository.MarkAcknowledgedAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        try
        {
            await backendTerminalClient.AcknowledgeSessionAsync(settings, status.SessionId, cancellationToken);
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
        await attemptRepository.UpdateSessionAsync(
            attempt.AttemptGuid,
            status.SessionId,
            status.TxnRef,
            now,
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
}
