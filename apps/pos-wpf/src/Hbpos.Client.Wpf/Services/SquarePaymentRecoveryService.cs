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
}

public sealed class SquarePaymentRecoveryService(
    ILocalSquarePaymentAttemptRepository attemptRepository,
    ICardTerminalSettingsProvider settingsProvider,
    ISquareTerminalPaymentClient squareTerminalPaymentClient,
    CashCheckoutService checkout,
    ILocalOrderRepository orderRepository,
    ILocalizationService? localization = null,
    FinancialSupervisorAuditReplayService? supervisorAuditReplay = null) : ISquarePaymentRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
                return RestoreSupervisorApprovedRetry(cart, refundAttempt);
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

        await RunLocalStoreAsync(
            () => attemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, cancellationToken),
            cancellationToken);
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

        var draft = DeserializeDraft(attempt);
        if (attempt.Status == LocalSquarePaymentAttemptStatus.PaymentVerified &&
            !string.IsNullOrWhiteSpace(attempt.PaymentId) &&
            !string.IsNullOrWhiteSpace(attempt.PaymentStatus))
        {
            if (TryDeferForCurrentCart(cart, attempt, "payment-already-verified", out var deferredResult))
            {
                return deferredResult;
            }

            return await CompleteVerifiedAttemptAsync(
                cart,
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

        if (IsSquarePendingStatus(checkoutStatus.Status))
        {
            await RunLocalStoreAsync(
                () => attemptRepository.UpdateCheckoutStatusAsync(
                    attempt.AttemptGuid,
                    LocalSquarePaymentAttemptStatus.Recovering,
                    checkoutStatus.Status,
                    checkoutStatus.CancelReason,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            return new CardPaymentRecoveryResult(CardPaymentRecoveryOutcome.Checking, checkingMessage);
        }

        if (TryDeferForCurrentCart(cart, attempt, $"checkout-final-{checkoutStatus.Status}", out var finalDeferredResult))
        {
            return finalDeferredResult;
        }

        if (string.Equals(checkoutStatus.Status, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            cart.RestoreSnapshot(draft.CartSnapshot);
            await RunLocalStoreAsync(
                () => attemptRepository.UpdateCheckoutStatusAsync(
                    attempt.AttemptGuid,
                    LocalSquarePaymentAttemptStatus.Canceled,
                    checkoutStatus.Status,
                    checkoutStatus.CancelReason,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                Format("cardRecovery.square.cancelled", "The previous Square card payment was not completed: {0}. The order has been restored. Select a payment method again.", checkoutStatus.CancelReason ?? "CANCELED"));
        }

        if (!string.Equals(checkoutStatus.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            await RunLocalStoreAsync(
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
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                UnknownResultMessage());
        }

        var paymentId = checkoutStatus.PaymentIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            await RunLocalStoreAsync(
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

        var verification = SquarePaymentVerifier.Verify(
            payment.Status,
            payment.AmountCents,
            payment.Currency,
            attempt.AmountCents,
            attempt.Currency);
        if (!verification.Verified)
        {
            await RunLocalStoreAsync(
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
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                verification.Failure == SquarePaymentVerificationFailure.Amount
                    ? T("cardRecovery.square.amountMismatch", "The payment amount returned by Square does not match the order amount. The order was not saved automatically. Ask a supervisor to confirm.")
                    : UnknownResultMessage());
        }

        await RunLocalStoreAsync(
            () => attemptRepository.MarkPaymentVerifiedAsync(
                attempt.AttemptGuid,
                payment.PaymentId,
                payment.Status,
                null,
                "Payment verified during recovery.",
                DateTimeOffset.UtcNow,
                CancellationToken.None),
            CancellationToken.None);
        return await CompleteVerifiedAttemptAsync(
            cart,
            attempt,
            draft,
            payment.PaymentId,
            payment.Status,
            payment.CardBrand,
            payment.MaskedCardNumber,
            payment.AuthCode,
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
            settings.Processor != CardProcessorKind.Square ||
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
            var recovery = RestoreSupervisorApprovedRetry(cart, updatedAttempt);
            return new CardRefundSupervisorResolutionResult(
                true,
                T("cardRecovery.refund.retryAllowed", "The bank confirmed that no refund was processed. The original return is ready to retry with the same operation."),
                recovery,
                RetryAllowed: true);
        }

        var completed = await CompleteSupervisorConfirmedRefundAsync(
            cart,
            session,
            updatedAttempt,
            CancellationToken.None);
        return new CardRefundSupervisorResolutionResult(
            true,
            T("cardRecovery.refund.confirmedSaved", "The confirmed refund was recorded and the original return was recovered."),
            completed);
    }

    private async Task<CardPaymentRecoveryResult> CompleteSupervisorConfirmedRefundAsync(
        PosCartService cart,
        PosSessionState currentSession,
        LocalSquarePaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        var dialogDetails = BuildRefundDialogDetails(attempt);
        if (!cart.IsEmpty)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.currentCartNotEmpty", "The confirmed refund is saved, but the current cart is not empty. Complete or clear it, then run recovery again."),
                DialogDetails: dialogDetails);
        }

        CardPaymentOrderDraft draft;
        try
        {
            draft = DeserializeDraft(attempt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
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

        cart.RestoreSnapshot(draft.CartSnapshot);
        var cardTender = new PaymentTender(
            PaymentMethodKind.Card,
            -Math.Abs(draft.CardAmount),
            CardRefundReference.Format(attempt.PaymentId, draft.OriginalReference),
            IdempotencyKey: $"SQUARE_ATTEMPT:{attempt.AttemptGuid:N}");
        var tenders = draft.CurrentTenders.Concat([cardTender]).ToList();
        if (IsApprovedTenderPartial(draft, tenders))
        {
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
            var cashTenderedAmount = tenders
                .Where(tender => tender.Method == PaymentMethodKind.Cash)
                .Sum(tender => tender.Amount);
            checkoutResult = checkout.CreatePaymentOrder(cart, draft.Session, tenders, cashTenderedAmount);
        }
        catch (InvalidOperationException ex)
        {
            ConsoleLog.Write(
                "SquareRecovery",
                $"confirmed refund checkout restore deferred attemptGuid={attempt.AttemptGuid} error={ex.GetType().Name}");
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.DraftRestored,
                T("cardRecovery.refund.confirmedTenderRestored", "The confirmed card refund was restored. Complete the remaining refund methods without refunding this card again."),
                TenderedAmount: tenders.Sum(tender => tender.Amount),
                DialogDetails: dialogDetails,
                RestoredTenders: tenders);
        }

        var order = checkoutResult.Order with { OrderGuid = draft.OrderGuid };
        var existingOrder = await RunLocalStoreAsync(
            () => orderRepository.GetOrderAsync(draft.OrderGuid, CancellationToken.None),
            CancellationToken.None);
        if (existingOrder is null)
        {
            await RunLocalStoreAsync(
                () => orderRepository.SavePendingOrderAsync(order, CancellationToken.None),
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
        cart.Clear();
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.OrderCompleted,
            T("cardRecovery.refund.confirmedCompleted", "The confirmed card refund was recovered and the return was saved."),
            order,
            tenders.Sum(tender => tender.Amount),
            checkoutResult.ChangeAmount,
            currentSession,
            dialogDetails);
    }

    private CardPaymentRecoveryResult RestoreSupervisorApprovedRetry(
        PosCartService cart,
        LocalSquarePaymentAttempt attempt)
    {
        if (!cart.IsEmpty)
        {
            return new CardPaymentRecoveryResult(
                CardPaymentRecoveryOutcome.Unknown,
                T("cardRecovery.square.currentCartNotEmpty", "The bank confirmed that no refund was processed, but the current cart is not empty. Complete or clear it, then run recovery again."),
                DialogDetails: BuildRefundDialogDetails(attempt));
        }

        var draft = DeserializeDraft(attempt);
        cart.RestoreSnapshot(draft.CartSnapshot);
        return new CardPaymentRecoveryResult(
            CardPaymentRecoveryOutcome.DraftRestored,
            T("cardRecovery.refund.retryAllowed", "The bank confirmed that no refund was processed. The original return is ready to retry with the same operation."),
            TenderedAmount: draft.CurrentTenders.Sum(tender => tender.Amount),
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
        PosCartService cart,
        LocalSquarePaymentAttempt attempt,
        CardPaymentOrderDraft draft,
        string paymentId,
        string paymentStatus,
        string? cardBrand,
        string? maskedCardNumber,
        string? authCode,
        CancellationToken cancellationToken)
    {
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
        var tenders = draft.CurrentTenders.Concat([cardTender]).ToList();
        var cashTenderedAmount = tenders
            .Where(tender => tender.Method == PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount);
        PaymentCheckoutResult checkoutResult;
        try
        {
            checkoutResult = checkout.CreatePaymentOrder(recoveryCart, draft.Session, tenders, cashTenderedAmount);
        }
        catch (Exception ex)
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
                await RunLocalStoreAsync(
                    () => orderRepository.SavePendingOrderAsync(order, CancellationToken.None),
                    CancellationToken.None);
            }
            else
            {
                order = existingOrder;
            }
        }
        catch (Exception ex)
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
        catch (Exception ex)
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
