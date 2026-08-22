using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hbpos.Contracts.Linkly;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Services;

public interface ICashPaymentWorkflowService
{
    bool TryParseTenderedAmount(string? amountTenderedText, out decimal tenderedAmount);

    decimal CalculateChange(string? amountTenderedText, decimal actualAmount);

    decimal CalculateTenderedAmount(IReadOnlyList<PaymentTender> tenders);

    decimal CalculateRemainingAmount(decimal actualAmount, IReadOnlyList<PaymentTender> tenders);

    decimal CalculateChange(IReadOnlyList<PaymentTender> tenders, decimal actualAmount);

    Task<PaymentTenderAttemptResult> AddTenderAsync(
        PaymentMethodKind method,
        PosSessionState session,
        decimal actualAmount,
        IReadOnlyList<PaymentTender> currentTenders,
        string? amountText,
        string? referenceText = null,
        CancellationToken cancellationToken = default,
        PosCartSnapshot? cartSnapshot = null);

    Task<bool> ReleaseVoucherTenderAsync(
        PaymentTender tender,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<CashPaymentWorkflowResult> CompleteAsync(
        PosCartService cart,
        PosSessionState session,
        string? amountTenderedText,
        CancellationToken cancellationToken = default);

    Task<CashPaymentWorkflowResult> CompletePaymentAsync(
        PosCartService cart,
        PosSessionState session,
        IReadOnlyList<PaymentTender> tenders,
        decimal cashTenderedAmount,
        CancellationToken cancellationToken = default);

    Task<CashPaymentWorkflowResult> RetryVoucherUploadAsync(
        Guid orderGuid,
        PosCartService cart,
        PosSessionState session,
        decimal tenderedAmount,
        decimal changeAmount,
        CancellationToken cancellationToken = default);
}

public sealed class CashPaymentWorkflowService(
    CashCheckoutService checkout,
    ILocalOrderRepository orderRepository,
    ISyncQueueRepository syncQueueRepository,
    IOrderUploadService? orderUploadService = null,
    ICardTerminalClient? cardTerminalClient = null,
    IVoucherTenderClient? voucherTenderClient = null,
    ILocalCardPaymentAttemptRepository? cardPaymentAttemptRepository = null,
    ICardTerminalSettingsProvider? cardTerminalSettingsProvider = null,
    ILocalSquarePaymentAttemptRepository? squarePaymentAttemptRepository = null,
    ILinklyPaymentAttemptContextAccessor? linklyPaymentAttemptContextAccessor = null,
    ISquarePaymentAttemptContextAccessor? squarePaymentAttemptContextAccessor = null,
    ILinklyBackendTerminalClient? linklyBackendTerminalClient = null,
    IEnumerable<ICardPaymentResultPolicy>? cardPaymentResultPolicies = null,
    ISharedHeldOrderRepository? sharedHeldOrderRepository = null) : ICashPaymentWorkflowService
{
    private static readonly JsonSerializerOptions CardAttemptJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly CashRoundingPolicy _cashRoundingPolicy = new();
    private readonly ICardTerminalClient _cardTerminalClient = cardTerminalClient ?? UnavailableCardTerminalClient.Instance;
    private readonly IVoucherTenderClient _voucherTenderClient = voucherTenderClient ?? UnavailableVoucherTenderClient.Instance;
    private readonly CardPaymentResultPolicyResolver _cardPaymentResultPolicyResolver = new(
        cardPaymentResultPolicies ??
        [
            new LinklyCardPaymentResultPolicy(),
            new SquareCardPaymentResultPolicy(),
            new FallbackCardPaymentResultPolicy()
        ]);
    private readonly ISharedHeldOrderPaymentSourceResolver? _heldOrderPaymentSourceResolver =
        sharedHeldOrderRepository is null
            ? null
            : new SharedHeldOrderPaymentSourceResolver(
                sharedHeldOrderRepository,
                new SharedHeldOrderReverseMapper());

    public bool TryParseTenderedAmount(string? amountTenderedText, out decimal tenderedAmount)
    {
        if (string.IsNullOrWhiteSpace(amountTenderedText))
        {
            tenderedAmount = 0m;
            return false;
        }

        return decimal.TryParse(amountTenderedText, NumberStyles.Number, CultureInfo.CurrentCulture, out tenderedAmount)
            || decimal.TryParse(amountTenderedText, NumberStyles.Number, CultureInfo.InvariantCulture, out tenderedAmount);
    }

    public decimal CalculateChange(string? amountTenderedText, decimal actualAmount)
    {
        if (RoundCurrency(actualAmount) < 0m)
        {
            return 0m;
        }

        if (!TryParseTenderedAmount(amountTenderedText, out var tenderedAmount))
        {
            return 0m;
        }

        var normalizedTenderedAmount = _cashRoundingPolicy.NormalizeCashTender(tenderedAmount);
        var roundedCashDue = _cashRoundingPolicy.CalculateRoundedCashDue(actualAmount);
        return _cashRoundingPolicy.CalculateChange(normalizedTenderedAmount, roundedCashDue);
    }

    public decimal CalculateTenderedAmount(IReadOnlyList<PaymentTender> tenders)
    {
        return RoundCurrency(tenders.Sum(tender => NormalizeTender(tender).Amount));
    }

    public decimal CalculateRemainingAmount(decimal actualAmount, IReadOnlyList<PaymentTender> tenders)
    {
        actualAmount = RoundCurrency(actualAmount);
        if (actualAmount < 0m)
        {
            return CalculateRefundRemainingAmount(actualAmount, tenders);
        }

        var normalizedTenders = tenders.Select(NormalizeTender).ToList();
        var nonCashTotal = RoundCurrency(normalizedTenders
            .Where(tender => tender.Method != PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount));
        var cashTotal = RoundCurrency(normalizedTenders
            .Where(tender => tender.Method == PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount));
        if (cashTotal <= 0m)
        {
            return RoundCurrency(actualAmount - nonCashTotal);
        }

        var roundedCashDue = _cashRoundingPolicy.CalculateRoundedCashDue(actualAmount, nonCashTotal);
        return RoundCurrency(roundedCashDue - cashTotal);
    }

    public decimal CalculateChange(IReadOnlyList<PaymentTender> tenders, decimal actualAmount)
    {
        if (RoundCurrency(actualAmount) < 0m)
        {
            return 0m;
        }

        var normalizedTenders = tenders.Select(NormalizeTender).ToList();
        var nonCashTotal = RoundCurrency(normalizedTenders
            .Where(tender => tender.Method != PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount));
        var cashTotal = RoundCurrency(normalizedTenders
            .Where(tender => tender.Method == PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount));
        if (cashTotal <= 0m)
        {
            return 0m;
        }

        var roundedCashDue = _cashRoundingPolicy.CalculateRoundedCashDue(actualAmount, nonCashTotal);
        return _cashRoundingPolicy.CalculateChange(cashTotal, roundedCashDue);
    }

    public async Task<PaymentTenderAttemptResult> AddTenderAsync(
        PaymentMethodKind method,
        PosSessionState session,
        decimal actualAmount,
        IReadOnlyList<PaymentTender> currentTenders,
        string? amountText,
        string? referenceText = null,
        CancellationToken cancellationToken = default,
        PosCartSnapshot? cartSnapshot = null)
    {
        if (!TryParseTenderedAmount(amountText, out var amount) || amount <= 0m)
        {
            return PaymentTenderAttemptResult.Fail("payment.status.invalidAmount");
        }

        var isRefund = RoundCurrency(actualAmount) < 0m;
        var remainingAmount = CalculateRemainingAmount(actualAmount, currentTenders);
        if ((!isRefund && remainingAmount <= 0m) ||
            (isRefund && remainingAmount >= 0m))
        {
            return PaymentTenderAttemptResult.Fail("payment.status.alreadyFullyPaid");
        }

        if (!isRefund &&
            method == PaymentMethodKind.Voucher &&
            HasExistingVoucherTender(currentTenders, referenceText))
        {
            return PaymentTenderAttemptResult.Fail("payment.status.duplicateVoucher");
        }

        if (isRefund)
        {
            if (method == PaymentMethodKind.Card && string.IsNullOrWhiteSpace(referenceText))
            {
                ConsoleLog.Write("CardRefund", "workflow blocked card refund reason=missing-original-reference");
                return PaymentTenderAttemptResult.Fail("payment.status.cardDeclined", "Original card payment reference is required.");
            }

            return method switch
            {
                PaymentMethodKind.Cash => CreateRefundCashTenderAttempt(amount),
                PaymentMethodKind.Card => await AuthorizeCardTenderAsync(
                    amount,
                    CalculateExternalRemainingAmount(actualAmount, currentTenders),
                    session,
                    actualAmount,
                    currentTenders,
                    cartSnapshot,
                    referenceText,
                    cancellationToken,
                    isRefund: true,
                    "payment.status.cardExceedsRemaining",
                    "payment.status.cardDeclined",
                    "payment.status.cardTenderAdded"),
                PaymentMethodKind.Voucher => AuthorizeRefundTenderAsync(
                    amount,
                    CalculateExternalRemainingAmount(actualAmount, currentTenders),
                    session,
                    referenceText,
                    cancellationToken,
                    PaymentMethodKind.Voucher,
                    "payment.status.voucherExceedsRemaining",
                    "payment.status.voucherTenderAdded"),
                _ => PaymentTenderAttemptResult.Fail("payment.status.unsupportedMethod")
            };
        }

        return method switch
        {
            PaymentMethodKind.Cash => CreateCashTenderAttempt(amount),
            PaymentMethodKind.Card => await AuthorizeCardTenderAsync(
                amount,
                CalculateExternalRemainingAmount(actualAmount, currentTenders),
                session,
                actualAmount,
                currentTenders,
                cartSnapshot,
                null,
                cancellationToken,
                isRefund: false,
                "payment.status.cardExceedsRemaining",
                "payment.status.cardDeclined",
                "payment.status.cardTenderAdded"),
            PaymentMethodKind.Voucher => await AuthorizeExternalTenderAsync(
                amount,
                CalculateExternalRemainingAmount(actualAmount, currentTenders),
                session,
                referenceText,
                cancellationToken,
                _voucherTenderClient.RedeemAsync,
                PaymentMethodKind.Voucher,
                "payment.status.voucherExceedsRemaining",
                "payment.status.voucherDeclined",
                "payment.status.voucherTenderAdded"),
            _ => PaymentTenderAttemptResult.Fail("payment.status.unsupportedMethod")
        };
    }

    public async Task<bool> ReleaseVoucherTenderAsync(
        PaymentTender tender,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (tender.Method != PaymentMethodKind.Voucher)
        {
            return false;
        }

        var (voucherCode, reservationToken) = ParseVoucherReservationFromReference(tender.Reference);
        if (string.IsNullOrWhiteSpace(voucherCode) || string.IsNullOrWhiteSpace(reservationToken))
        {
            return false;
        }

        try
        {
            // 中文注释：释放锁定只使用前三段，余额打印扩展段不会参与后端释放。
            return await _voucherTenderClient.ReleaseAsync(session, voucherCode, reservationToken, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write(
                "VoucherRelease",
                $"release failed voucher={LogValue(voucherCode)} token={LogValue(reservationToken)} error={ex.GetType().Name} message={LogValue(ex.Message)}");
            return false;
        }
    }

    public async Task<CashPaymentWorkflowResult> CompleteAsync(
        PosCartService cart,
        PosSessionState session,
        string? amountTenderedText,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseTenderedAmount(amountTenderedText, out var tenderedAmount))
        {
            throw new InvalidOperationException("Tendered amount is invalid.");
        }

        var result = checkout.CreateCashOrder(cart, session, tenderedAmount);
        // 共享挂单取单完成：现金路径必须用正在结账的购物车快照做来源匹配，
        // 避免把无关 open claim 误绑到普通订单。
        var cartSnapshot = cart.CreateSnapshot();
        // 共享挂单取单完成：现金路径与 LocalOrder 同一事务写入来源/完成 claim/消费本地挂单。
        var heldOrder = await TryResolveHeldOrderAsync(session, cartSnapshot, cancellationToken);
        await RunLocalStoreAsync(
            () => heldOrder is null
                ? orderRepository.SavePendingOrderAsync(result.Order, cancellationToken)
                : orderRepository.SavePendingOrderWithHeldSourceAsync(
                    result.Order,
                    heldOrder,
                    cancellationToken),
            cancellationToken);

        var hasPostCommitWarning = TryClearCartAfterCommit(cart, result.Order.OrderGuid);
        var pendingSyncResult = await ReadPendingSyncCountAfterCommitAsync(session, cancellationToken);
        hasPostCommitWarning |= pendingSyncResult.HasPostCommitWarning;
        var pendingSyncCount = pendingSyncResult.PendingSyncCount;
        var updatedSession = session with { PendingSyncCount = pendingSyncCount };

        return new CashPaymentWorkflowResult(
            result.Order,
            result.TenderedAmount,
            result.ChangeAmount,
            pendingSyncCount,
            updatedSession,
            hasPostCommitWarning);
    }

    public async Task<CashPaymentWorkflowResult> CompletePaymentAsync(
        PosCartService cart,
        PosSessionState session,
        IReadOnlyList<PaymentTender> tenders,
        decimal cashTenderedAmount,
        CancellationToken cancellationToken = default)
    {
        // 中文注释：在离开 UI 线程前固定付款明细，后台 SQLite 不得枚举可能继续变化的界面集合。
        var tenderSnapshot = tenders.ToArray();
        var cartSnapshot = cart.CreateSnapshot();
        var result = checkout.CreatePaymentOrder(cart, session, tenderSnapshot, cashTenderedAmount);
        // 退款代金券先以待发券状态落本地，确保崩溃后仍能沿用原始幂等键恢复。
        var orderForPersistence = PrepareOrderForVoucherRefundPersistence(result.Order);
        // 终端已批准后，订单 GUID 恢复和本地订单落盘不能被随后取消的 UI 操作打断。
        var persistenceCancellationToken = CancellationToken.None;
        LocalOrder order;
        try
        {
            order = await RunLocalStoreAsync(
                () => PrepareOrderForRecoverableCardPersistenceAsync(
                    orderForPersistence,
                    tenderSnapshot,
                    persistenceCancellationToken),
                persistenceCancellationToken);
            // 共享挂单取单完成：混合付款路径与 LocalOrder 同一事务写入来源/完成 claim/消费本地挂单。
            var heldOrder = await TryResolveHeldOrderAsync(session, cartSnapshot, persistenceCancellationToken);
            await RunLocalStoreAsync(
                () => heldOrder is null
                    ? orderRepository.SavePendingOrderAsync(order, persistenceCancellationToken)
                    : orderRepository.SavePendingOrderWithHeldSourceAsync(
                        order,
                        heldOrder,
                        persistenceCancellationToken),
                persistenceCancellationToken);
        }
        catch (Exception ex) when (
            tenderSnapshot.Any(tender => tender.Method == PaymentMethodKind.Card) &&
            ex is not OutOfMemoryException and not StackOverflowException)
        {
            // 已批准银行卡 tender 代表真实金融副作用；本地订单失败只能进入待恢复，绝不能退回普通付款失败。
            await MarkApprovedCardPersistenceRequiresReviewAsync(tenderSnapshot, ex);
            throw new CardPaymentPersistenceUnknownException(
                orderForPersistence.OrderGuid,
                "The card was approved, but POS could not safely save the order. Recovery or supervisor review is required.",
                ex);
        }
        try
        {
            order = await IssuePendingRefundVouchersAsync(order, session, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PaymentUploadFailedException(
                order.OrderGuid,
                CalculateTenderedAmount(tenderSnapshot),
                result.ChangeAmount,
                ex.Message,
                ex);
        }

        result = result with { Order = order };

        var hasPositiveVoucher = result.Order.Payments.Any(payment =>
            payment.Method == Hbpos.Contracts.Orders.PaymentMethodKind.Voucher &&
            payment.Amount > 0m);
        if (hasPositiveVoucher)
        {
            if (orderUploadService is null)
            {
                throw new InvalidOperationException("Voucher payments require online order upload.");
            }

            try
            {
                await orderUploadService.UploadOrderAsync(result.Order.OrderGuid, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new PaymentUploadFailedException(
                    result.Order.OrderGuid,
                    CalculateTenderedAmount(tenderSnapshot),
                    result.ChangeAmount,
                    ex.Message,
                    ex);
            }
        }

        var hasPostCommitWarning = false;
        try
        {
            await MarkCompletedCardAttemptsAsync(tenderSnapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            hasPostCommitWarning = true;
            LogPostCommitWarning("mark-card-attempt-completed", result.Order.OrderGuid, ex);
        }

        try
        {
            await MarkCompletedSquareAttemptsAsync(tenderSnapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            hasPostCommitWarning = true;
            LogPostCommitWarning("mark-square-attempt-completed", result.Order.OrderGuid, ex);
        }

        hasPostCommitWarning |= TryClearCartAfterCommit(cart, result.Order.OrderGuid);

        var pendingSyncResult = await ReadPendingSyncCountAfterCommitAsync(session, cancellationToken);
        hasPostCommitWarning |= pendingSyncResult.HasPostCommitWarning;
        var pendingSyncCount = pendingSyncResult.PendingSyncCount;
        var updatedSession = session with { PendingSyncCount = pendingSyncCount };

        return new CashPaymentWorkflowResult(
            result.Order,
            CalculateTenderedAmount(tenderSnapshot),
            result.ChangeAmount,
            pendingSyncCount,
            updatedSession,
            hasPostCommitWarning);
    }

    public async Task<CashPaymentWorkflowResult> RetryVoucherUploadAsync(
        Guid orderGuid,
        PosCartService cart,
        PosSessionState session,
        decimal tenderedAmount,
        decimal changeAmount,
        CancellationToken cancellationToken = default)
    {
        var order = await RunLocalStoreAsync(
                () => orderRepository.GetOrderAsync(orderGuid, cancellationToken),
                cancellationToken)
            ?? throw new InvalidOperationException("Pending voucher order was not found.");
        try
        {
            order = await IssuePendingRefundVouchersAsync(order, session, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PaymentUploadFailedException(
                orderGuid,
                tenderedAmount,
                changeAmount,
                ex.Message,
                ex);
        }

        var hasPositiveVoucher = order.Payments.Any(payment =>
            payment.Method == PaymentMethodKind.Voucher &&
            payment.Amount > 0m);
        if (hasPositiveVoucher)
        {
            if (orderUploadService is null)
            {
                throw new InvalidOperationException("Voucher payments require online order upload.");
            }

            try
            {
                await orderUploadService.UploadOrderAsync(orderGuid, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new PaymentUploadFailedException(
                    orderGuid,
                    tenderedAmount,
                    changeAmount,
                    ex.Message,
                    ex);
            }
        }

        var hasPostCommitWarning = TryClearCartAfterCommit(cart, order.OrderGuid);
        var pendingSyncResult = await ReadPendingSyncCountAfterCommitAsync(session, cancellationToken);
        hasPostCommitWarning |= pendingSyncResult.HasPostCommitWarning;
        var pendingSyncCount = pendingSyncResult.PendingSyncCount;
        var updatedSession = session with { PendingSyncCount = pendingSyncCount };

        return new CashPaymentWorkflowResult(
            order,
            tenderedAmount,
            changeAmount,
            pendingSyncCount,
            updatedSession,
            hasPostCommitWarning);
    }

    private async Task<(int PendingSyncCount, bool HasPostCommitWarning)> ReadPendingSyncCountAfterCommitAsync(
        PosSessionState session,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await RunLocalStoreAsync(
                () => syncQueueRepository.CountPendingAsync(cancellationToken),
                cancellationToken), false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LogPostCommitWarning("refresh-pending-sync", orderGuid: null, ex);
            return (session.PendingSyncCount, true);
        }
    }

    private static void LogPostCommitWarning(string stage, Guid? orderGuid, Exception ex)
    {
        ConsoleLog.Write(
            "CashPaymentWorkflow",
            $"post-commit warning stage={stage} orderGuid={orderGuid?.ToString("D") ?? "<none>"} error={ex.GetType().Name}");
    }

    private async Task<LocalHeldOrderCompletionContext?> TryResolveHeldOrderAsync(
        PosSessionState session,
        PosCartSnapshot cartSnapshot,
        CancellationToken cancellationToken)
    {
        if (_heldOrderPaymentSourceResolver is null)
        {
            return null;
        }

        return await _heldOrderPaymentSourceResolver.TryResolveAsync(
            session,
            cartSnapshot,
            cancellationToken);
    }

    private static Task RunLocalStoreAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        // 中文注释：终端取消后仍要完成已开始的本地金融日志，不能让已取消的 UI token 跳过审计状态落盘。
        var workerCancellationToken = GetLocalPersistenceCancellationToken(cancellationToken);
        return Task.Run(operation, workerCancellationToken);
    }

    private static Task<T> RunLocalStoreAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        // 中文注释：调用方必须先构造不可变订单或 attempt 快照，后台委托不得访问购物车或界面集合。
        var workerCancellationToken = GetLocalPersistenceCancellationToken(cancellationToken);
        return Task.Run(operation, workerCancellationToken);
    }

    private static CancellationToken GetLocalPersistenceCancellationToken(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;

    private static bool TryClearCartAfterCommit(PosCartService cart, Guid orderGuid)
    {
        try
        {
            cart.Clear();
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // 订单已持久化后，购物车通知失败只能作为收尾告警，不能把已完成收款变成可重付失败。
            LogPostCommitWarning("clear-cart", orderGuid, ex);
            return true;
        }
    }

    private async Task<PaymentTenderAttemptResult> AuthorizeCardTenderAsync(
        decimal amount,
        decimal remainingAmount,
        PosSessionState session,
        decimal actualAmount,
        IReadOnlyList<PaymentTender> currentTenders,
        PosCartSnapshot? cartSnapshot,
        string? referenceText,
        CancellationToken cancellationToken,
        bool isRefund,
        string exceedsRemainingStatusKey,
        string declinedStatusKey,
        string approvedStatusKey)
    {
        if (amount > remainingAmount)
        {
            ConsoleLog.Write(
                "CardRefund",
                $"workflow blocked card {(isRefund ? "refund" : "payment")} reason=amount-exceeds-remaining amount={amount:0.00} remaining={remainingAmount:0.00} originalReference={LogValue(referenceText)}");
            return PaymentTenderAttemptResult.Fail(exceedsRemainingStatusKey);
        }

        if (!isRefund && amount < remainingAmount)
        {
            ConsoleLog.Write(
                "CardRefund",
                $"workflow blocked card payment reason=amount-below-remaining amount={amount:0.00} remaining={remainingAmount:0.00}");
            return PaymentTenderAttemptResult.Fail("payment.status.cardMustBeFinalTender");
        }

        var operation = isRefund ? "refund" : "payment";
        ConsoleLog.Write(
            "CardRefund",
            $"workflow terminal {operation} start amount={amount:0.00} remaining={remainingAmount:0.00} originalReference={LogValue(referenceText)}");

        // 中文注释：attempt 草稿包含当前付款明细的不可变副本，后台 SQLite 不能直接读取 UI 绑定集合。
        var tenderSnapshot = currentTenders.ToArray();
        var persistenceCancellationToken = GetLocalPersistenceCancellationToken(cancellationToken);
        var cardAttemptSelection = await RunLocalStoreAsync(
            () => TryCreateCardPaymentAttemptAsync(
                amount,
                session,
                actualAmount,
                tenderSnapshot,
                cartSnapshot,
                referenceText,
                isRefund,
                persistenceCancellationToken),
            persistenceCancellationToken);
        var attempt = cardAttemptSelection.Attempt;
        var squareAttemptSelection = await RunLocalStoreAsync(
            () => TryCreateSquarePaymentAttemptAsync(
                amount,
                session,
                actualAmount,
                tenderSnapshot,
                cartSnapshot,
                referenceText,
                isRefund,
                persistenceCancellationToken),
            persistenceCancellationToken);
        var squareAttempt = squareAttemptSelection.Attempt;

        if (isRefund)
        {
            var existingLinklyAttemptRequiresRecovery = cardAttemptSelection.Reused &&
                attempt is not null &&
                (attempt.Status != LocalCardPaymentAttemptStatus.Pending ||
                 !string.IsNullOrWhiteSpace(attempt.SessionId));
            var existingSquareAttemptRequiresRecovery = squareAttemptSelection.Reused &&
                squareAttempt is not null &&
                (squareAttempt.Status != LocalSquarePaymentAttemptStatus.Pending ||
                 !string.IsNullOrWhiteSpace(squareAttempt.CheckoutId) ||
                 !string.IsNullOrWhiteSpace(squareAttempt.PaymentId));
            if (existingLinklyAttemptRequiresRecovery || existingSquareAttemptRequiresRecovery)
            {
                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    "A matching card refund is already in progress or requires supervisor reconciliation.",
                    recoveryAttempt: attempt,
                    squareRecoveryAttempt: squareAttempt);
            }

            var durableRefundConfigured = cardTerminalSettingsProvider is not null &&
                (cardPaymentAttemptRepository is not null || squarePaymentAttemptRepository is not null);
            if (durableRefundConfigured && attempt is null && squareAttempt is null)
            {
                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    "The card refund attempt could not be persisted, so the terminal was not called.");
            }
        }

        PaymentAuthorizationResult authorization;
        var linklySubmissionObserved = false;
        var squareSubmissionObserved = false;
        var refundDispatchBoundaryPersisted = false;
        string? refundIdempotencyKey = null;
        string? refundSubmissionToken = null;
        if (isRefund && (attempt is not null || squareAttempt is not null))
        {
            refundIdempotencyKey = attempt?.TxnRef ?? squareAttempt?.IdempotencyKey;
            if (string.IsNullOrWhiteSpace(refundIdempotencyKey) ||
                _cardTerminalClient is not IIdempotentCardRefundClient)
            {
                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    "The card refund adapter cannot safely reuse the persisted attempt.",
                    recoveryAttempt: attempt,
                    squareRecoveryAttempt: squareAttempt);
            }

            try
            {
                // 中文注释：提交令牌既是跨进程 claim，也是旧 worker 的 fencing token；只有 CAS 赢家可以调用终端。
                refundSubmissionToken = Guid.NewGuid().ToString("N");
                if (attempt is not null)
                {
                    var boundaryAt = NextAttemptTimestamp(attempt.UpdatedAt);
                    var claimed = await RunLocalStoreAsync(
                        () => cardPaymentAttemptRepository!.TryBeginRefundSubmissionAsync(
                            attempt.AttemptGuid,
                            attempt.UpdatedAt,
                            refundSubmissionToken,
                            boundaryAt,
                            CancellationToken.None),
                        CancellationToken.None);
                    if (!claimed)
                    {
                        return CreateCardFailureResult(
                            "payment.card.resultUnknown",
                            "The card refund attempt is already being processed or was reconciled.",
                            recoveryAttempt: attempt);
                    }

                    attempt = attempt with
                    {
                        Status = LocalCardPaymentAttemptStatus.Recovering,
                        ResponseCode = null,
                        ResponseText = null,
                        SubmissionToken = refundSubmissionToken,
                        UpdatedAt = boundaryAt
                    };
                }

                if (squareAttempt is not null)
                {
                    var boundaryAt = NextAttemptTimestamp(squareAttempt.UpdatedAt);
                    var claimed = await RunLocalStoreAsync(
                        () => squarePaymentAttemptRepository!.TryBeginRefundSubmissionAsync(
                            squareAttempt.AttemptGuid,
                            squareAttempt.UpdatedAt,
                            refundSubmissionToken,
                            boundaryAt,
                            CancellationToken.None),
                        CancellationToken.None);
                    if (!claimed)
                    {
                        return CreateCardFailureResult(
                            "payment.card.resultUnknown",
                            "The card refund attempt is already being processed or was reconciled.",
                            squareRecoveryAttempt: squareAttempt);
                    }

                    squareAttempt = squareAttempt with
                    {
                        Status = LocalSquarePaymentAttemptStatus.Recovering,
                        ResponseCode = null,
                        ResponseText = null,
                        SubmissionToken = refundSubmissionToken,
                        UpdatedAt = boundaryAt
                    };
                }

                refundDispatchBoundaryPersisted = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                LogCardRecoveryWarning("persist-refund-dispatch-boundary", attempt?.AttemptGuid ?? squareAttempt?.AttemptGuid, ex);
                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    "The card refund attempt could not be locked before terminal submission.");
            }
        }

        using var linklyAttemptScope = attempt is null || linklyPaymentAttemptContextAccessor is null
            ? null
            : linklyPaymentAttemptContextAccessor.Begin(new LinklyPaymentAttemptContext(
                attempt.AttemptGuid,
                (sessionId, txnRef, updatedAt, _) =>
                {
                    // 已收到 Linkly session 表明终端已接单；本地提交证据必须脱离 UI 取消。
                    linklySubmissionObserved = true;
                    return RunLocalStoreAsync(
                        () => refundSubmissionToken is not null
                            ? RequireRefundCasAsync(
                                cardPaymentAttemptRepository!.TryUpdateRefundSessionAsync(
                                    attempt.AttemptGuid,
                                    refundSubmissionToken,
                                    sessionId,
                                    txnRef,
                                    updatedAt,
                                    CancellationToken.None))
                            : cardPaymentAttemptRepository!.UpdateSessionAsync(
                                attempt.AttemptGuid,
                                sessionId,
                                txnRef,
                                updatedAt,
                                CancellationToken.None),
                        CancellationToken.None);
                },
                attempt.TxnRef,
                refundSubmissionToken,
                TakeOverActiveSessionAsync: (settings, activeStatus, cancellationToken) =>
                    TakeOverActiveSessionForNewPaymentAsync(
                        settings,
                        activeStatus,
                        session,
                        cancellationToken)));
        using var squareAttemptScope = squareAttempt is null || squarePaymentAttemptContextAccessor is null
            ? null
            : squarePaymentAttemptContextAccessor.Begin(new SquarePaymentAttemptContext(
                squareAttempt.AttemptGuid,
                squareAttempt.IdempotencyKey,
                (checkoutId, checkoutStatus, updatedAt, _) =>
                {
                    // Square 返回 checkoutId 即已提交；状态证据必须脱离 UI 取消。
                    squareSubmissionObserved = true;
                    return RunLocalStoreAsync(
                        () => !string.IsNullOrWhiteSpace(squareAttempt.SubmissionToken)
                            ? RequireRefundCasAsync(
                                squarePaymentAttemptRepository!.TryMarkCheckoutCreatedAsync(
                                    squareAttempt.AttemptGuid,
                                    squareAttempt.SubmissionToken!,
                                    checkoutId,
                                    checkoutStatus,
                                    updatedAt,
                                    CancellationToken.None))
                            : squarePaymentAttemptRepository!.MarkCheckoutCreatedAsync(
                                squareAttempt.AttemptGuid,
                                checkoutId,
                                checkoutStatus,
                                updatedAt,
                                CancellationToken.None),
                        CancellationToken.None);
                },
                squareAttempt.SubmissionToken,
                (refundId, refundStatus, updatedAt, _) =>
                {
                    // Square 已返回 refundId 即代表退款已受理；必须先持久化，重启后只能查询，不能重复 POST。
                    squareSubmissionObserved = true;
                    return RunLocalStoreAsync(
                        () => !string.IsNullOrWhiteSpace(squareAttempt.SubmissionToken)
                            ? RequireRefundCasAsync(
                                squarePaymentAttemptRepository!.TryRecordRefundResponseAsync(
                                    squareAttempt.AttemptGuid,
                                    squareAttempt.SubmissionToken!,
                                    refundId,
                                    refundStatus,
                                    updatedAt,
                                    CancellationToken.None))
                            : Task.FromException(
                                new InvalidOperationException("Square 退款缺少 submission token，无法持久化退款响应。")),
                        CancellationToken.None);
                }));
        try
        {
            authorization = isRefund && refundIdempotencyKey is not null
                ? await ((IIdempotentCardRefundClient)_cardTerminalClient).RefundAsync(
                    amount,
                    session,
                    referenceText,
                    refundIdempotencyKey,
                    cancellationToken)
                : isRefund
                    ? await _cardTerminalClient.RefundAsync(amount, session, referenceText, cancellationToken)
                : await _cardTerminalClient.AuthorizeAsync(amount, session, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            var definitelyNotSubmitted = ex is CardTerminalNotSubmittedException;
            LocalCardPaymentAttempt? linklyAttemptAfterException;
            LocalSquarePaymentAttempt? squareAttemptAfterException;
            try
            {
                linklyAttemptAfterException = attempt is null
                    ? null
                    : await RunLocalStoreAsync(
                        () => cardPaymentAttemptRepository!.GetAttemptAsync(attempt.AttemptGuid, CancellationToken.None),
                        CancellationToken.None);
                squareAttemptAfterException = squareAttempt is null
                    ? null
                    : await RunLocalStoreAsync(
                        () => squarePaymentAttemptRepository!.GetAttemptAsync(squareAttempt.AttemptGuid, CancellationToken.None),
                        CancellationToken.None);
            }
            catch (Exception readException) when (readException is not OutOfMemoryException and not StackOverflowException)
            {
                LogCardRecoveryWarning("read-attempt", attempt?.AttemptGuid ?? squareAttempt?.AttemptGuid, readException);
                if (definitelyNotSubmitted && cancellationToken.IsCancellationRequested)
                {
                    return CreateCardFailureResult("payment.status.cardCancelled");
                }

                // 未收到“提交前取消”信号且无法可靠读取提交边界时，必须保守锁定。
                return CreateCardFailureResult("payment.card.resultUnknown");
            }

            // LocalIp 在 socket 写入前已持久化 TxnRef；没有 SessionId 也不能把其取消当作未提交。
            var wasSubmitted = !definitelyNotSubmitted && (
                linklySubmissionObserved ||
                squareSubmissionObserved ||
                refundDispatchBoundaryPersisted ||
                !string.IsNullOrWhiteSpace(linklyAttemptAfterException?.SessionId) ||
                !string.IsNullOrWhiteSpace(linklyAttemptAfterException?.TxnRef) ||
                !string.IsNullOrWhiteSpace(squareAttemptAfterException?.CheckoutId));

            if (wasSubmitted)
            {
                // 终端已接单后任何异常都不能被当成失败并允许重新收款。
                var recoveryPersistenceSucceeded = true;
                try
                {
                    if (linklyAttemptAfterException is not null &&
                        (!string.IsNullOrWhiteSpace(linklyAttemptAfterException.SessionId) ||
                         !string.IsNullOrWhiteSpace(linklyAttemptAfterException.TxnRef)))
                    {
                        await RunLocalStoreAsync(
                            () => refundSubmissionToken is not null
                                ? RequireRefundCasAsync(
                                    cardPaymentAttemptRepository!.TryMarkRefundRecoveringAsync(
                                        linklyAttemptAfterException.AttemptGuid,
                                        refundSubmissionToken,
                                        DateTimeOffset.UtcNow,
                                        CancellationToken.None))
                                : cardPaymentAttemptRepository!.MarkRecoveringAsync(
                                    linklyAttemptAfterException.AttemptGuid,
                                    DateTimeOffset.UtcNow,
                                    CancellationToken.None),
                            CancellationToken.None);
                    }

                    if (squareAttemptAfterException is not null &&
                        (isRefund ||
                         squareSubmissionObserved ||
                         !string.IsNullOrWhiteSpace(squareAttemptAfterException.CheckoutId)) &&
                        string.Equals(
                            squareAttemptAfterException.SubmissionToken,
                            squareAttempt?.SubmissionToken,
                            StringComparison.Ordinal))
                    {
                        await RunLocalStoreAsync(
                            () => refundSubmissionToken is not null
                                ? RequireRefundCasAsync(
                                    squarePaymentAttemptRepository!.TryMarkRefundFailedAsync(
                                        squareAttemptAfterException.AttemptGuid,
                                        refundSubmissionToken,
                                        LocalSquarePaymentAttemptStatus.Unknown,
                                        squareAttemptAfterException.CheckoutStatus,
                                        squareAttemptAfterException.PaymentStatus,
                                        squareAttemptAfterException.ResponseCode,
                                        "Square checkout ended with an exception after submission; result requires recovery.",
                                        DateTimeOffset.UtcNow,
                                        CancellationToken.None,
                                        squareAttemptAfterException.CancelReason))
                                : squarePaymentAttemptRepository!.MarkFailedAsync(
                                    squareAttemptAfterException.AttemptGuid,
                                    LocalSquarePaymentAttemptStatus.Unknown,
                                    squareAttemptAfterException.CheckoutStatus,
                                    squareAttemptAfterException.PaymentStatus,
                                    squareAttemptAfterException.ResponseCode,
                                    "Square checkout ended with an exception after submission; result requires recovery.",
                                    DateTimeOffset.UtcNow,
                                    CancellationToken.None,
                                    squareAttemptAfterException.CancelReason),
                            CancellationToken.None);
                    }
                }
                catch (Exception persistException) when (persistException is not OutOfMemoryException and not StackOverflowException)
                {
                    recoveryPersistenceSucceeded = false;
                    LogCardRecoveryWarning("persist-recovery", attempt?.AttemptGuid ?? squareAttempt?.AttemptGuid, persistException);
                }

                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    recoveryAttempt: recoveryPersistenceSucceeded ? linklyAttemptAfterException : null,
                    squareRecoveryAttempt: recoveryPersistenceSucceeded ? squareAttemptAfterException : null);
            }

            try
            {
                if (attempt is not null)
                {
                    var attemptStatus = ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                        ? LocalCardPaymentAttemptStatus.Cancelled
                        : LocalCardPaymentAttemptStatus.Failed;
                    var attemptMessage = ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                        ? "Card payment was canceled before terminal submission."
                        : "Card terminal request failed before terminal submission.";
                    await RunLocalStoreAsync(
                        () => refundSubmissionToken is not null
                            ? RequireRefundCasAsync(
                                cardPaymentAttemptRepository!.TryUpdateRefundOutcomeAsync(
                                    attempt.AttemptGuid,
                                    refundSubmissionToken,
                                    attemptStatus,
                                    null,
                                    attemptMessage,
                                    null,
                                    DateTimeOffset.UtcNow,
                                    CancellationToken.None))
                            : cardPaymentAttemptRepository!.UpdateOutcomeAsync(
                                attempt.AttemptGuid,
                                attemptStatus,
                                null,
                                attemptMessage,
                                null,
                                DateTimeOffset.UtcNow,
                                CancellationToken.None),
                        CancellationToken.None);
                }

                if (squareAttempt is not null)
                {
                    var squareStatus = ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                        ? LocalSquarePaymentAttemptStatus.Canceled
                        : LocalSquarePaymentAttemptStatus.Failed;
                    var squareMessage = ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                        ? "Card payment was canceled before terminal submission."
                        : "Card terminal request failed before terminal submission.";
                    await RunLocalStoreAsync(
                        () => refundSubmissionToken is not null
                            ? RequireRefundCasAsync(
                                squarePaymentAttemptRepository!.TryMarkRefundFailedAsync(
                                    squareAttempt.AttemptGuid,
                                    refundSubmissionToken,
                                    squareStatus,
                                    null,
                                    null,
                                    null,
                                    squareMessage,
                                    DateTimeOffset.UtcNow,
                                    CancellationToken.None,
                                    cancelReason: null))
                            : squarePaymentAttemptRepository!.MarkFailedAsync(
                                squareAttempt.AttemptGuid,
                                squareStatus,
                                null,
                                null,
                                null,
                                squareMessage,
                                DateTimeOffset.UtcNow,
                                CancellationToken.None),
                        CancellationToken.None);
                }
            }
            catch (Exception persistException) when (persistException is not OutOfMemoryException and not StackOverflowException)
            {
                LogCardRecoveryWarning("persist-failure", attempt?.AttemptGuid ?? squareAttempt?.AttemptGuid, persistException);
                if (definitelyNotSubmitted && cancellationToken.IsCancellationRequested)
                {
                    return CreateCardFailureResult("payment.status.cardCancelled");
                }

                return CreateCardFailureResult("payment.card.resultUnknown");
            }

            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                return CreateCardFailureResult("payment.status.cardCancelled");
            }

            throw;
        }

        if (squareAttempt is not null && !authorization.Approved && !authorization.ResultUnknown)
        {
            LocalSquarePaymentAttempt? squareAttemptAfterAuthorization;
            try
            {
                squareAttemptAfterAuthorization = await RunLocalStoreAsync(
                    () => squarePaymentAttemptRepository!.GetAttemptAsync(squareAttempt.AttemptGuid, CancellationToken.None),
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                LogCardRecoveryWarning("read-square-attempt", squareAttempt.AttemptGuid, ex);
                return CreateCardFailureResult("payment.card.resultUnknown");
            }

            var hasUnresolvedSquareAttempt = squareAttemptAfterAuthorization?.Status == LocalSquarePaymentAttemptStatus.Unknown ||
                (squareAttemptAfterAuthorization?.Status is not (
                    LocalSquarePaymentAttemptStatus.Canceled or
                    LocalSquarePaymentAttemptStatus.Failed or
                    LocalSquarePaymentAttemptStatus.TimedOut or
                    LocalSquarePaymentAttemptStatus.Abandoned or
                    LocalSquarePaymentAttemptStatus.OrderCompleted) &&
                 !string.IsNullOrWhiteSpace(squareAttemptAfterAuthorization?.CheckoutId));
            if (hasUnresolvedSquareAttempt)
            {
                const string squareResultUnknownMessage = "Square checkout result could not be confirmed. Recovery is required.";
                var squareRecoveryPersisted = false;
                try
                {
                    await MarkSquareAttemptRequiresRecoveryAsync(
                        squareAttempt,
                        authorization,
                        squareResultUnknownMessage,
                        refundSubmissionToken);
                    squareRecoveryPersisted = true;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    LogCardRecoveryWarning("persist-square-recovery", squareAttempt.AttemptGuid, ex);
                }

                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    squareResultUnknownMessage,
                    squareRecoveryAttempt: squareRecoveryPersisted ? squareAttempt : null);
            }

            await RunLocalStoreAsync(
                () => refundSubmissionToken is not null
                    ? RequireRefundCasAsync(
                        squarePaymentAttemptRepository!.TryMarkRefundFailedAsync(
                            squareAttempt.AttemptGuid,
                            refundSubmissionToken,
                            MapSquareAuthorizationFailureStatus(authorization.StatusKey, authorization.Message),
                            null,
                            authorization.ResponseText,
                            authorization.ResponseCode,
                            authorization.Message,
                            DateTimeOffset.UtcNow,
                            CancellationToken.None))
                    : squarePaymentAttemptRepository!.MarkFailedAsync(
                        squareAttempt.AttemptGuid,
                        MapSquareAuthorizationFailureStatus(authorization.StatusKey, authorization.Message),
                        null,
                        authorization.ResponseText,
                        authorization.ResponseCode,
                        authorization.Message,
                        DateTimeOffset.UtcNow,
                        CancellationToken.None),
                CancellationToken.None);
        }

        ConsoleLog.Write(
            "CardRefund",
            $"workflow terminal {operation} completed approved={authorization.Approved} reference={LogValue(authorization.Reference)} " +
            $"message={LogValue(authorization.Message)} authorizedAmount={authorization.AuthorizedAmount?.ToString("0.00") ?? "<null>"} " +
            $"cardTxCount={authorization.CardTransactions?.Count ?? 0}");

        if (authorization.ResultUnknown)
        {
            const string resultUnknownMessage = "Card terminal result could not be confirmed. Recovery is required.";
            LocalCardPaymentAttempt? persistedLinklyRecovery = null;
            LocalSquarePaymentAttempt? persistedSquareRecovery = null;
            try
            {
                if (attempt is not null)
                {
                    if (await TryPersistCardPaymentAttemptAfterFinancialResultAsync(
                        attempt.AttemptGuid,
                        authorization,
                        CancellationToken.None,
                        refundSubmissionToken: refundSubmissionToken))
                    {
                        persistedLinklyRecovery = attempt;
                    }
                }

                await MarkSquareAttemptRequiresRecoveryAsync(
                    squareAttempt,
                    authorization,
                    resultUnknownMessage,
                    refundSubmissionToken);
                persistedSquareRecovery = squareAttempt;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                LogCardRecoveryWarning("persist-result-unknown", attempt?.AttemptGuid ?? squareAttempt?.AttemptGuid, ex);
            }

            return CreateCardFailureResult(
                "payment.card.resultUnknown",
                resultUnknownMessage,
                recoveryAttempt: persistedLinklyRecovery,
                squareRecoveryAttempt: persistedSquareRecovery);
        }

        if (!authorization.Approved)
        {
            if (attempt is not null)
            {
                try
                {
                    await UpdateCardPaymentAttemptAfterAuthorizationAsync(
                        attempt.AttemptGuid,
                        authorization,
                        CancellationToken.None,
                        refundSubmissionToken: refundSubmissionToken);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    // 接管旧 session 失败时本次新交易确定未提交；本地终态写失败也不能伪造 ResultUnknown 锁。
                    LogCardRecoveryWarning("persist-definitive-not-submitted", attempt.AttemptGuid, ex);
                }
            }

            return CreateCardFailureResult(
                string.IsNullOrWhiteSpace(authorization.StatusKey) ? declinedStatusKey : authorization.StatusKey,
                authorization.Message,
                isTerminalDecline: HasTerminalDeclineEvidence(authorization));
        }

        var authorizedAmount = decimal.Round(
            authorization.AuthorizedAmount ?? amount,
            2,
            MidpointRounding.AwayFromZero);
        if (authorizedAmount <= 0m)
        {
            const string nonPositiveAmountMessage = "Card terminal approved a non-positive amount. Supervisor review is required.";
            LocalCardPaymentAttempt? persistedLinklyRecovery = null;
            if (attempt is not null)
            {
                if (!await TryPersistCardPaymentAttemptAfterFinancialResultAsync(
                    attempt.AttemptGuid,
                    authorization,
                    CancellationToken.None,
                    LocalCardPaymentAttemptStatus.RequiresReview,
                    nonPositiveAmountMessage,
                    refundSubmissionToken))
                {
                    return CreateCardFailureResult("payment.card.resultUnknown", nonPositiveAmountMessage);
                }

                persistedLinklyRecovery = attempt;
            }

            await MarkSquareAttemptRequiresRecoveryAsync(
                squareAttempt,
                authorization,
                nonPositiveAmountMessage,
                refundSubmissionToken);
            return CreateCardFailureResult(
                "payment.card.resultUnknown",
                nonPositiveAmountMessage,
                recoveryAttempt: persistedLinklyRecovery,
                squareRecoveryAttempt: squareAttempt);
        }

        if (authorizedAmount > remainingAmount)
        {
            const string exceedsRemainingMessage = "Card terminal authorized amount exceeded the remaining amount.";
            LocalCardPaymentAttempt? persistedLinklyRecovery = null;
            if (attempt is not null)
            {
                if (!await TryPersistCardPaymentAttemptAfterFinancialResultAsync(
                    attempt.AttemptGuid,
                    authorization,
                    CancellationToken.None,
                    LocalCardPaymentAttemptStatus.RequiresReview,
                    exceedsRemainingMessage,
                    refundSubmissionToken))
                {
                    return CreateCardFailureResult("payment.card.resultUnknown", exceedsRemainingMessage);
                }

                persistedLinklyRecovery = attempt;
            }

            await MarkSquareAttemptRequiresRecoveryAsync(
                squareAttempt,
                authorization,
                exceedsRemainingMessage,
                refundSubmissionToken);
            return CreateCardFailureResult(
                "payment.card.resultUnknown",
                exceedsRemainingMessage,
                recoveryAttempt: persistedLinklyRecovery,
                squareRecoveryAttempt: squareAttempt);
        }

        if (authorizedAmount != amount)
        {
            const string amountMismatchMessage = "Card terminal authorized amount did not match the requested amount.";
            LocalCardPaymentAttempt? persistedLinklyRecovery = null;
            if (attempt is not null)
            {
                if (!await TryPersistCardPaymentAttemptAfterFinancialResultAsync(
                    attempt.AttemptGuid,
                    authorization,
                    CancellationToken.None,
                    LocalCardPaymentAttemptStatus.RequiresReview,
                    amountMismatchMessage,
                    refundSubmissionToken))
                {
                    return CreateCardFailureResult("payment.card.resultUnknown", amountMismatchMessage);
                }

                persistedLinklyRecovery = attempt;
            }

            await MarkSquareAttemptRequiresRecoveryAsync(
                squareAttempt,
                authorization,
                amountMismatchMessage,
                refundSubmissionToken);
            return CreateCardFailureResult(
                "payment.card.resultUnknown",
                amountMismatchMessage,
                recoveryAttempt: persistedLinklyRecovery,
                squareRecoveryAttempt: squareAttempt);
        }

        if (attempt is not null)
        {
            if (!await TryPersistCardPaymentAttemptAfterFinancialResultAsync(
                attempt.AttemptGuid,
                authorization,
                CancellationToken.None,
                refundSubmissionToken: refundSubmissionToken))
            {
                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    "The card was approved but the local recovery record could not be confirmed.");
            }
        }

        if (squareAttempt is not null && refundSubmissionToken is not null)
        {
            var squareRefundTransaction = authorization.CardTransactions?.FirstOrDefault(transaction =>
                string.Equals(transaction.Processor, "Square", StringComparison.OrdinalIgnoreCase));
            var paymentId = squareRefundTransaction?.TxnRef;
            if (string.IsNullOrWhiteSpace(paymentId) &&
                authorization.Reference?.StartsWith("SQRF:", StringComparison.OrdinalIgnoreCase) == true)
            {
                paymentId = authorization.Reference["SQRF:".Length..];
            }

            var paymentStatus = squareRefundTransaction?.ResponseText ?? authorization.Message;
            if (string.IsNullOrWhiteSpace(paymentId) || string.IsNullOrWhiteSpace(paymentStatus))
            {
                const string missingSquareEvidenceMessage =
                    "Square approved the refund but did not return durable refund evidence. Recovery is required.";
                await MarkSquareAttemptRequiresRecoveryAsync(
                    squareAttempt,
                    authorization,
                    missingSquareEvidenceMessage,
                    refundSubmissionToken);
                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    missingSquareEvidenceMessage,
                    squareRecoveryAttempt: squareAttempt);
            }

            try
            {
                // 退款成功必须由当前 submission token 落库；旧 worker 迟到时不得生成成功 tender。
                await RunLocalStoreAsync(
                    () => RequireRefundCasAsync(
                        squarePaymentAttemptRepository!.TryMarkRefundPaymentVerifiedAsync(
                            squareAttempt.AttemptGuid,
                            refundSubmissionToken,
                            paymentId,
                            paymentStatus,
                            authorization.ResponseCode,
                            "Refund verified.",
                            DateTimeOffset.UtcNow,
                            CancellationToken.None)),
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                LogCardRecoveryWarning("persist-square-refund-approved", squareAttempt.AttemptGuid, ex);
                return CreateCardFailureResult(
                    "payment.card.resultUnknown",
                    "The Square refund was approved but its current attempt could not be confirmed. Recovery is required.");
            }
        }

        var reference = isRefund
            ? CardRefundReference.Format(authorization.Reference, referenceText!)
            : authorization.Reference;
        var successStatusKey = authorization.FallbackSucceeded
            ? "payment.linklyFallback.succeeded"
            : approvedStatusKey;
        var successStatusMessage = authorization.FallbackSucceeded
            ? string.Format(
                CultureInfo.CurrentCulture,
                T("payment.linklyFallback.succeeded"),
                FormatLinklyModeDisplayName(authorization.RequestedConnectionMode),
                FormatLinklyModeDisplayName(authorization.ActualConnectionMode),
                T("payment.linklyFallback.promotePrimary"))
            : null;

        return PaymentTenderAttemptResult.Success(
            new PaymentTender(
                PaymentMethodKind.Card,
                isRefund ? -authorizedAmount : authorizedAmount,
                reference,
                CardTransactions: authorization.CardTransactions,
                IdempotencyKey: attempt is not null
                    ? FormatCardAttemptTenderKey(attempt.AttemptGuid)
                    : squareAttempt is not null
                        ? FormatSquareAttemptTenderKey(squareAttempt.AttemptGuid)
                        : null),
            successStatusKey,
            successStatusMessage);
    }

    private PaymentTenderAttemptResult CreateCardFailureResult(
        string statusKey,
        string? statusMessage = null,
        bool isTerminalDecline = false,
        LocalCardPaymentAttempt? recoveryAttempt = null,
        LocalSquarePaymentAttempt? squareRecoveryAttempt = null)
    {
        var result = _cardPaymentResultPolicyResolver.Apply(
            PaymentTenderAttemptResult.Fail(statusKey, statusMessage, isTerminalDecline));
        if (TryCreateRecoveryIdentity(recoveryAttempt, squareRecoveryAttempt, out var key, out var orderGuid))
        {
            result = result with
            {
                RecoveryAttemptKey = key,
                RecoveryOrderGuid = orderGuid
            };
        }

        return result;
    }

    private static bool TryCreateRecoveryIdentity(
        LocalCardPaymentAttempt? attempt,
        LocalSquarePaymentAttempt? squareAttempt,
        out CardRecoveryAttemptKey key,
        out Guid orderGuid)
    {
        key = default;
        orderGuid = Guid.Empty;
        if ((attempt is null) == (squareAttempt is null))
        {
            return false;
        }

        var draftJson = attempt?.OrderDraftJson ?? squareAttempt!.OrderDraftJson;
        CardPaymentOrderDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(draftJson, CardAttemptJsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (draft is null ||
            draft.OrderGuid == Guid.Empty ||
            draft.Session is null ||
            draft.CartSnapshot?.Lines is not { Count: > 0 } ||
            draft.CurrentTenders is null ||
            draft.CreatedAt == default)
        {
            return false;
        }

        key = attempt is not null
            ? new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid)
            : new CardRecoveryAttemptKey(CardProcessorKind.Square, squareAttempt!.AttemptGuid);
        orderGuid = draft.OrderGuid;
        return true;
    }

    private async Task MarkSquareAttemptRequiresRecoveryAsync(
        LocalSquarePaymentAttempt? squareAttempt,
        PaymentAuthorizationResult authorization,
        string message,
        string? refundSubmissionToken = null)
    {
        if (squareAttempt is null)
        {
            return;
        }

        await RunLocalStoreAsync(
            () => refundSubmissionToken is not null
                ? RequireRefundCasAsync(
                    squarePaymentAttemptRepository!.TryMarkRefundFailedAsync(
                        squareAttempt.AttemptGuid,
                        refundSubmissionToken,
                        LocalSquarePaymentAttemptStatus.Unknown,
                        null,
                        authorization.ResponseText,
                        authorization.ResponseCode,
                        message,
                        DateTimeOffset.UtcNow,
                        CancellationToken.None))
                : squarePaymentAttemptRepository!.MarkFailedAsync(
                    squareAttempt.AttemptGuid,
                    LocalSquarePaymentAttemptStatus.Unknown,
                    null,
                    authorization.ResponseText,
                    authorization.ResponseCode,
                    message,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
            CancellationToken.None);
    }

    private static void LogCardRecoveryWarning(string stage, Guid? attemptGuid, Exception ex)
    {
        ConsoleLog.Write(
            "CardRecovery",
            $"conservative result-unknown stage={stage} attemptGuid={attemptGuid?.ToString("D") ?? "<none>"} error={ex.GetType().Name}");
    }

    private async Task<(LocalCardPaymentAttempt? Attempt, bool Reused)> TryCreateCardPaymentAttemptAsync(
        decimal amount,
        PosSessionState session,
        decimal actualAmount,
        IReadOnlyList<PaymentTender> currentTenders,
        PosCartSnapshot? cartSnapshot,
        string? referenceText,
        bool isRefund,
        CancellationToken cancellationToken)
    {
        if (cardPaymentAttemptRepository is null || cardTerminalSettingsProvider is null || cartSnapshot is null)
        {
            return (null, false);
        }

        var settings = await cardTerminalSettingsProvider.GetSettingsAsync(cancellationToken);
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode);
        if (settings.Processor != CardProcessorKind.Linkly ||
            (!isRefund && mode != LinklyConnectionMode.CloudBackendAsync && mode != LinklyConnectionMode.LocalIp))
        {
            return (null, false);
        }

        if (isRefund)
        {
            var openRefundAttempts = await cardPaymentAttemptRepository.GetOpenRefundAttemptsAsync(
                session.StoreCode,
                session.DeviceCode,
                settings.Environment.ToString(),
                cancellationToken);
            var existingAttempt = openRefundAttempts.FirstOrDefault(candidate =>
                candidate.Amount == amount &&
                RefundDraftMatches(candidate.OrderDraftJson, referenceText));
            if (existingAttempt is not null)
            {
                return (existingAttempt, true);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var refundBusinessKey = isRefund
            ? BuildRefundBusinessKey(
                CardProcessorKind.Linkly,
                settings.Environment.ToString(),
                session,
                amount,
                referenceText,
                "AUD")
            : null;
        var draft = new CardPaymentOrderDraft(
            Guid.NewGuid(),
            session,
            cartSnapshot,
            currentTenders.ToArray(),
            actualAmount,
            amount,
            isRefund ? "R" : "P",
            referenceText,
            now);
        var attemptGuid = Guid.NewGuid();
        var attempt = new LocalCardPaymentAttempt(
            attemptGuid,
            null,
            // 退款 TxnRef 必须与原销售引用分离并在终端调用前落库；普通 LocalIp 销售仍沿用既有生成规则。
            isRefund
                ? BuildRefundTxnRef(referenceText)
                : mode == LinklyConnectionMode.LocalIp
                    ? LinklyTerminalClient.BuildTxnRef(session)
                    : null,
            settings.Processor.ToString(),
            settings.Environment.ToString(),
            CardTerminalSettings.FormatLinklyConnectionMode(mode),
            isRefund ? "R" : "P",
            amount,
            LocalCardPaymentAttemptStatus.Pending,
            JsonSerializer.Serialize(draft, CardAttemptJsonOptions),
            session.StoreCode,
            session.DeviceCode,
            session.CashierId,
            null,
            null,
            null,
            now,
            now,
            null,
            null,
            isRefund ? "Refund" : "Sale",
            isRefund ? draft.OrderGuid : null,
            SubmissionToken: null,
            RefundBusinessKey: refundBusinessKey);

        var persistedAttempt = isRefund
            ? await cardPaymentAttemptRepository.CreateOrGetOpenRefundAsync(attempt, cancellationToken)
            : attempt;
        if (!isRefund)
        {
            await cardPaymentAttemptRepository.CreateAsync(attempt, cancellationToken);
        }

        LinklyJsonLog.Write(
            "CardRecovery",
            "card-recovery",
            "payment-attempt",
            "created",
            environment: settings.Environment,
            details: new
            {
                timestamp = DateTimeOffset.Now,
                attemptGuid = persistedAttempt.AttemptGuid,
                localStatus = persistedAttempt.Status.ToString(),
                txnType = persistedAttempt.TxnType,
                amount = persistedAttempt.Amount,
                processor = persistedAttempt.Processor,
                connectionMode = persistedAttempt.ConnectionMode,
                storeCode = persistedAttempt.StoreCode,
                deviceCode = persistedAttempt.DeviceCode,
                cashierId = persistedAttempt.CashierId,
                createdAt = persistedAttempt.CreatedAt,
                updatedAt = persistedAttempt.UpdatedAt
            });
        return (persistedAttempt, persistedAttempt.AttemptGuid != attempt.AttemptGuid);
    }

    private async Task<(LocalSquarePaymentAttempt? Attempt, bool Reused)> TryCreateSquarePaymentAttemptAsync(
        decimal amount,
        PosSessionState session,
        decimal actualAmount,
        IReadOnlyList<PaymentTender> currentTenders,
        PosCartSnapshot? cartSnapshot,
        string? referenceText,
        bool isRefund,
        CancellationToken cancellationToken)
    {
        if (squarePaymentAttemptRepository is null ||
            cardTerminalSettingsProvider is null ||
            cartSnapshot is null)
        {
            return (null, false);
        }

        var settings = await cardTerminalSettingsProvider.GetSettingsAsync(cancellationToken);
        if (settings.Processor != CardProcessorKind.Square ||
            string.IsNullOrWhiteSpace(settings.SquareDeviceId) ||
            string.IsNullOrWhiteSpace(settings.SquareLocationId))
        {
            return (null, false);
        }

        if (isRefund)
        {
            var openRefundAttempts = await squarePaymentAttemptRepository.GetOpenRefundAttemptsAsync(
                session.StoreCode,
                session.DeviceCode,
                settings.Environment.ToString(),
                cancellationToken);
            var existingAttempt = openRefundAttempts.FirstOrDefault(candidate =>
                candidate.Amount == amount &&
                RefundDraftMatches(candidate.OrderDraftJson, referenceText));
            if (existingAttempt is not null)
            {
                return (existingAttempt, true);
            }
        }

        const string currency = "AUD";
        var now = DateTimeOffset.UtcNow;
        var refundBusinessKey = isRefund
            ? BuildRefundBusinessKey(
                CardProcessorKind.Square,
                settings.Environment.ToString(),
                session,
                amount,
                referenceText,
                currency)
            : null;
        var saleSubmissionToken = isRefund ? null : Guid.NewGuid().ToString("N");
        var draft = new CardPaymentOrderDraft(
            Guid.NewGuid(),
            session,
            cartSnapshot,
            currentTenders.ToArray(),
            actualAmount,
            amount,
            isRefund ? "R" : "P",
            referenceText,
            now);
        var attempt = new LocalSquarePaymentAttempt(
            Guid.NewGuid(),
            null,
            Guid.NewGuid().ToString("N"),
            SquareDeviceIdNormalizer.NormalizeForTerminalCheckout(settings.SquareDeviceId) ?? settings.SquareDeviceId,
            settings.SquareLocationId,
            settings.Environment.ToString(),
            amount,
            ToMinorUnits(amount),
            currency,
            LocalSquarePaymentAttemptStatus.Pending,
            null,
            null,
            JsonSerializer.Serialize(draft, CardAttemptJsonOptions),
            session.StoreCode,
            session.DeviceCode,
            session.CashierId,
            null,
            null,
            null,
            null,
            now,
            now,
            null,
            null,
            null,
            isRefund ? "Refund" : "Sale",
            // 销售同样固化订单身份；恢复时不能只信任可被损坏的 draft JSON。
            draft.OrderGuid,
            SubmissionToken: saleSubmissionToken,
            RefundBusinessKey: refundBusinessKey);

        var persistedAttempt = isRefund
            ? await squarePaymentAttemptRepository.CreateOrGetOpenRefundAsync(attempt, cancellationToken)
            : attempt;
        if (!isRefund)
        {
            await squarePaymentAttemptRepository.CreateAsync(attempt, cancellationToken);
        }

        return (persistedAttempt, persistedAttempt.AttemptGuid != attempt.AttemptGuid);
    }

    private static bool RefundDraftMatches(string orderDraftJson, string? originalReference)
    {
        try
        {
            var draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(orderDraftJson, CardAttemptJsonOptions);
            return string.Equals(
                draft?.OriginalReference?.Trim(),
                originalReference?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // 同终端、同金额的未决退款草稿损坏时无法证明是另一笔交易，必须保守阻止重复退款。
            return true;
        }
    }

    private static string BuildRefundTxnRef(string? originalReference)
    {
        var normalizedOriginalReference = originalReference?.Trim();
        if (normalizedOriginalReference?.StartsWith("ANZ:", StringComparison.OrdinalIgnoreCase) == true)
        {
            normalizedOriginalReference = normalizedOriginalReference[4..].Trim();
        }

        string txnRef;
        do
        {
            txnRef = Guid.NewGuid().ToString("N");
        }
        while (string.Equals(txnRef, normalizedOriginalReference, StringComparison.OrdinalIgnoreCase));

        return txnRef;
    }

    private static string BuildRefundBusinessKey(
        CardProcessorKind processor,
        string environment,
        PosSessionState session,
        decimal amount,
        string? originalReference,
        string currency)
    {
        var canonical = string.Join(
            "\n",
            "ordinary-card-refund-v1",
            processor.ToString().ToUpperInvariant(),
            environment.Trim().ToUpperInvariant(),
            session.StoreCode.Trim().ToUpperInvariant(),
            session.DeviceCode.Trim().ToUpperInvariant(),
            originalReference?.Trim().ToUpperInvariant() ?? string.Empty,
            ToMinorUnits(amount).ToString(CultureInfo.InvariantCulture),
            currency.Trim().ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static DateTimeOffset NextAttemptTimestamp(DateTimeOffset current)
    {
        var now = DateTimeOffset.UtcNow;
        return now > current ? now : current.AddTicks(1);
    }

    private static async Task RequireRefundCasAsync(Task<bool> update)
    {
        if (!await update)
        {
            throw new InvalidOperationException("退款 attempt 已被其他任务推进或主管结案。");
        }
    }

    private async Task UpdateCardPaymentAttemptAfterAuthorizationAsync(
        Guid attemptGuid,
        PaymentAuthorizationResult authorization,
        CancellationToken cancellationToken,
        LocalCardPaymentAttemptStatus? statusOverride = null,
        string? responseTextOverride = null,
        string? refundSubmissionToken = null)
    {
        // 终端已经返回后，attempt 结果是金融恢复边界，不能受 UI 取消影响。
        var persistenceCancellationToken = CancellationToken.None;
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(authorization.SessionId))
        {
            await RunLocalStoreAsync(
                () => refundSubmissionToken is not null
                    ? RequireRefundCasAsync(
                        cardPaymentAttemptRepository!.TryUpdateRefundSessionAsync(
                            attemptGuid,
                            refundSubmissionToken,
                            authorization.SessionId,
                            authorization.TxnRef,
                            now,
                            persistenceCancellationToken))
                    : cardPaymentAttemptRepository!.UpdateSessionAsync(
                        attemptGuid,
                        authorization.SessionId,
                        authorization.TxnRef,
                        now,
                        persistenceCancellationToken),
                persistenceCancellationToken);
        }

        if (authorization.ResultUnknown)
        {
            // 已提交到终端但结果未知时必须保留为可恢复状态，避免被当作普通超时失败后允许重新刷卡。
            await RunLocalStoreAsync(
                () => refundSubmissionToken is not null
                    ? RequireRefundCasAsync(
                        cardPaymentAttemptRepository!.TryMarkRefundRecoveringAsync(
                            attemptGuid,
                            refundSubmissionToken,
                            now,
                            persistenceCancellationToken))
                    : cardPaymentAttemptRepository!.MarkRecoveringAsync(
                        attemptGuid,
                        now,
                        persistenceCancellationToken),
                persistenceCancellationToken);
            return;
        }

        var firstTransaction = authorization.CardTransactions?.FirstOrDefault();
        var status = statusOverride ?? (authorization.Approved
            ? LocalCardPaymentAttemptStatus.Approved
            : MapCardAttemptFailureStatus(
                authorization.Message,
                firstTransaction?.ResponseText,
                firstTransaction?.ResponseCode ?? authorization.ResponseCode));
        await RunLocalStoreAsync(
            () => refundSubmissionToken is not null
                ? RequireRefundCasAsync(
                    cardPaymentAttemptRepository!.TryUpdateRefundOutcomeAsync(
                        attemptGuid,
                        refundSubmissionToken,
                        status,
                        firstTransaction?.ResponseCode ?? authorization.ResponseCode,
                        responseTextOverride ?? firstTransaction?.ResponseText ?? authorization.ResponseText ?? authorization.Message,
                        authorization.Reference,
                        now,
                        persistenceCancellationToken))
                : cardPaymentAttemptRepository!.UpdateOutcomeAsync(
                    attemptGuid,
                    status,
                    firstTransaction?.ResponseCode ?? authorization.ResponseCode,
                    responseTextOverride ?? firstTransaction?.ResponseText ?? authorization.ResponseText ?? authorization.Message,
                    authorization.Reference,
                    now,
                    persistenceCancellationToken),
            persistenceCancellationToken);
    }

    private async Task<bool> TryPersistCardPaymentAttemptAfterFinancialResultAsync(
        Guid attemptGuid,
        PaymentAuthorizationResult authorization,
        CancellationToken cancellationToken,
        LocalCardPaymentAttemptStatus? statusOverride = null,
        string? responseTextOverride = null,
        string? refundSubmissionToken = null)
    {
        try
        {
            await UpdateCardPaymentAttemptAfterAuthorizationAsync(
                attemptGuid,
                authorization,
                cancellationToken,
                statusOverride,
                responseTextOverride,
                refundSubmissionToken);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LogCardRecoveryWarning("persist-financial-result", attemptGuid, ex);
            try
            {
                await RunLocalStoreAsync(
                    () => refundSubmissionToken is not null
                        ? RequireRefundCasAsync(
                            cardPaymentAttemptRepository!.TryMarkRefundRecoveringAsync(
                                attemptGuid,
                                refundSubmissionToken,
                                DateTimeOffset.UtcNow,
                                CancellationToken.None))
                        : cardPaymentAttemptRepository!.MarkRecoveringAsync(
                            attemptGuid,
                            DateTimeOffset.UtcNow,
                            CancellationToken.None),
                    CancellationToken.None);
            }
            catch (Exception recoveryException) when (recoveryException is not OutOfMemoryException and not StackOverflowException)
            {
                LogCardRecoveryWarning("persist-financial-result-recovery", attemptGuid, recoveryException);
            }

            return false;
        }
    }

    private static LocalCardPaymentAttemptStatus MapCardAttemptFailureStatus(
        string? message,
        string? responseText,
        string? responseCode)
    {
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

        var text = $"{message} {responseText}".ToUpperInvariant();
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

    private static LinklyConnectionMode ResolveAttemptConnectionMode(
        LocalCardPaymentAttempt attempt,
        LinklyConnectionMode fallback)
    {
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(attempt.ConnectionMode, fallback);
        // LocalIp 没有 backend session；若 attempt 已绑定 SessionId，说明实际已 fallback 到后端异步链路。
        return mode == LinklyConnectionMode.LocalIp && !string.IsNullOrWhiteSpace(attempt.SessionId)
            ? LinklyConnectionMode.CloudBackendAsync
            : mode;
    }

    private static LocalSquarePaymentAttemptStatus MapSquareAuthorizationFailureStatus(
        string? statusKey,
        string? message)
    {
        // Square 友好状态键比英文 message 更稳定，优先用它保留本地 attempt 的真实分类。
        switch (statusKey)
        {
            case "payment.card.squareTimedOut":
            case "payment.card.squareTerminalNotPickedUp":
                return LocalSquarePaymentAttemptStatus.TimedOut;
            case "payment.card.squareCanceled":
            case "payment.card.squareCanceledBuyer":
            case "payment.card.squareCanceledSeller":
                return LocalSquarePaymentAttemptStatus.Canceled;
            case "payment.card.squareTerminalOffline":
                return LocalSquarePaymentAttemptStatus.Failed;
        }

        var text = (message ?? string.Empty).ToUpperInvariant();
        if (text.Contains("TIMEOUT", StringComparison.Ordinal) ||
            text.Contains("TIMED OUT", StringComparison.Ordinal))
        {
            return LocalSquarePaymentAttemptStatus.TimedOut;
        }

        if (text.Contains("CANCEL", StringComparison.Ordinal))
        {
            return LocalSquarePaymentAttemptStatus.Canceled;
        }

        if (text.Contains("UNKNOWN", StringComparison.Ordinal) ||
            text.Contains("CONFIRM", StringComparison.Ordinal))
        {
            return LocalSquarePaymentAttemptStatus.Unknown;
        }

        return LocalSquarePaymentAttemptStatus.Failed;
    }

    private static long ToMinorUnits(decimal amount)
    {
        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string FormatCardAttemptTenderKey(Guid attemptGuid)
    {
        return $"CARD_ATTEMPT:{attemptGuid:N}";
    }

    private static string FormatSquareAttemptTenderKey(Guid attemptGuid)
    {
        return $"SQUARE_ATTEMPT:{attemptGuid:N}";
    }

    private static bool TryReadCardAttemptTenderKey(string? value, out Guid attemptGuid)
    {
        attemptGuid = Guid.Empty;
        const string prefix = "CARD_ATTEMPT:";
        return !string.IsNullOrWhiteSpace(value) &&
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(value[prefix.Length..], "N", out attemptGuid);
    }

    private static bool TryReadSquareAttemptTenderKey(string? value, out Guid attemptGuid)
    {
        attemptGuid = Guid.Empty;
        const string prefix = "SQUARE_ATTEMPT:";
        return !string.IsNullOrWhiteSpace(value) &&
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(value[prefix.Length..], "N", out attemptGuid);
    }

    private async Task MarkApprovedCardPersistenceRequiresReviewAsync(
        IReadOnlyList<PaymentTender> tenders,
        Exception persistenceException)
    {
        const string reviewMessage =
            "The card was approved, but the local order could not be safely persisted.";
        var now = DateTimeOffset.UtcNow;

        if (cardPaymentAttemptRepository is not null)
        {
            foreach (var tender in tenders.Where(tender => tender.Method == PaymentMethodKind.Card))
            {
                if (!TryReadCardAttemptTenderKey(tender.IdempotencyKey, out var attemptGuid))
                {
                    continue;
                }

                try
                {
                    await RunLocalStoreAsync(
                        () => cardPaymentAttemptRepository.UpdateOutcomeAsync(
                            attemptGuid,
                            LocalCardPaymentAttemptStatus.RequiresReview,
                            responseCode: null,
                            responseText: reviewMessage,
                            paymentReference: tender.Reference,
                            completedAt: now,
                            cancellationToken: CancellationToken.None),
                        CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    LogCardRecoveryWarning("mark-approved-order-persistence-review", attemptGuid, ex);
                }
            }
        }

        if (squarePaymentAttemptRepository is not null)
        {
            foreach (var tender in tenders.Where(tender => tender.Method == PaymentMethodKind.Card))
            {
                if (!TryReadSquareAttemptTenderKey(tender.IdempotencyKey, out var attemptGuid))
                {
                    continue;
                }

                try
                {
                    await RunLocalStoreAsync(
                        () => squarePaymentAttemptRepository.MarkFailedAsync(
                            attemptGuid,
                            LocalSquarePaymentAttemptStatus.Unknown,
                            checkoutStatus: null,
                            paymentStatus: null,
                            responseCode: null,
                            responseText: reviewMessage,
                            resolvedAt: now,
                            cancellationToken: CancellationToken.None),
                        CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    LogCardRecoveryWarning("mark-approved-square-order-persistence-review", attemptGuid, ex);
                }
            }
        }

        LogCardRecoveryWarning("approved-order-persistence", attemptGuid: null, persistenceException);
    }

    private async Task MarkCompletedCardAttemptsAsync(
        IReadOnlyList<PaymentTender> tenders,
        CancellationToken cancellationToken)
    {
        if (cardPaymentAttemptRepository is null)
        {
            return;
        }

        foreach (var attemptGuid in tenders
            .Select(tender => TryReadCardAttemptTenderKey(tender.IdempotencyKey, out var attemptGuid) ? attemptGuid : (Guid?)null)
            .Where(attemptGuid => attemptGuid is not null)
            .Select(attemptGuid => attemptGuid!.Value)
            .Distinct())
        {
            // 订单落本地后才把刷卡 attempt 标为完成，避免“刷卡成功但订单未写入”被误判为已恢复。
            await RunLocalStoreAsync(
                () => cardPaymentAttemptRepository.MarkOrderCompletedAsync(
                    attemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
            await AcknowledgeCompletedCardAttemptAsync(attemptGuid, cancellationToken);
        }
    }

    private async Task AcknowledgeCompletedCardAttemptAsync(
        Guid attemptGuid,
        CancellationToken cancellationToken)
    {
        var backendTerminalClient = linklyBackendTerminalClient;
        var settingsProvider = cardTerminalSettingsProvider;
        var attemptRepository = cardPaymentAttemptRepository;
        if (backendTerminalClient is null || settingsProvider is null || attemptRepository is null)
        {
            return;
        }

        var attempt = await RunLocalStoreAsync(
            () => attemptRepository.GetAttemptAsync(attemptGuid, cancellationToken),
            cancellationToken);
        if (attempt is null ||
            attempt.AcknowledgedAt is not null ||
            string.IsNullOrWhiteSpace(attempt.SessionId))
        {
            return;
        }

        var sessionId = attempt.SessionId;
        var settings = await RunLocalStoreAsync(
            () => settingsProvider.GetSettingsAsync(cancellationToken),
            cancellationToken);
        var mode = ResolveAttemptConnectionMode(
            attempt,
            CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode));
        if (settings.Processor != CardProcessorKind.Linkly ||
            mode != LinklyConnectionMode.CloudBackendAsync ||
            !Enum.TryParse<CardTerminalEnvironment>(attempt.Environment, ignoreCase: true, out var environment))
        {
            return;
        }

        try
        {
            // 订单已经落地后才确认 Linkly session，避免恢复列表提前丢失成功交易。
            await backendTerminalClient.AcknowledgeSessionAsync(
                settings with { Environment = environment },
                sessionId,
                cancellationToken);
            await RunLocalStoreAsync(
                () => attemptRepository.MarkAcknowledgedAsync(
                    attemptGuid,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConsoleLog.Write(
                "CardRecovery",
                $"payment completion acknowledge failed attemptGuid={attemptGuid} sessionId={sessionId} error={ex.GetType().Name}");
        }
    }

    /// <summary>
    /// 接管旧 Linkly 活动会话：精确关联权威 attempt、查询至终态、完整证据落库、
    /// 最后 acknowledge/release。落库失败或 ack 失败都返回 Failed，绝不发起新扣款。
    /// </summary>
    private async Task<LinklyActiveSessionTakeoverResult> TakeOverActiveSessionForNewPaymentAsync(
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse activeStatus,
        PosSessionState session,
        CancellationToken cancellationToken)
    {
        if (cardPaymentAttemptRepository is null || linklyBackendTerminalClient is null)
        {
            return LinklyActiveSessionTakeoverResult.Failed(
                "The card recovery store is unavailable, so the previous Linkly session could not be cleared.");
        }

        try
        {
            EnsureActiveSessionScopeMatches(settings, session, activeStatus, expectedSessionId: activeStatus.SessionId);

            // 1. 优先关联已有 Sale/Refund/ActiveSession；仅无权威记录时创建 generic ActiveSession。
            var activeAttempt = await FindOrCreateActiveSessionAttemptAsync(
                settings,
                session,
                activeStatus,
                cancellationToken);

            // 2. Resume/query 至确定终态。
            var finalStatus = await linklyBackendTerminalClient.ResumeSessionUntilFinalAsync(
                settings,
                activeStatus,
                cancellationToken);
            if (!IsTerminalActiveSessionStatus(finalStatus))
            {
                return LinklyActiveSessionTakeoverResult.Failed(
                    "The previous Linkly session is still pending and could not be cleared safely.");
            }

            EnsureActiveSessionScopeMatches(settings, session, finalStatus, activeStatus.SessionId);
            if (!string.IsNullOrWhiteSpace(activeStatus.TxnRef) &&
                !string.IsNullOrWhiteSpace(finalStatus.TxnRef) &&
                !string.Equals(
                    activeStatus.TxnRef.Trim(),
                    finalStatus.TxnRef.Trim(),
                    StringComparison.Ordinal))
            {
                return LinklyActiveSessionTakeoverResult.Failed(
                    "The previous Linkly session identity changed during recovery and was not acknowledged.");
            }

            var finalTxnRef = NormalizeOptional(finalStatus.TxnRef) ??
                NormalizeOptional(activeStatus.TxnRef) ??
                NormalizeOptional(activeAttempt.TxnRef);
            var isRefundAttempt = string.Equals(
                activeAttempt.OperationKind,
                "Refund",
                StringComparison.Ordinal);
            var refundSubmissionToken = NormalizeOptional(activeAttempt.SubmissionToken);
            if (isRefundAttempt && refundSubmissionToken is null)
            {
                return LinklyActiveSessionTakeoverResult.Failed(
                    "The previous Linkly refund is missing its submission identity and was not acknowledged.");
            }

            var outcome = MapActiveSessionOutcome(finalStatus);
            if (outcome == LocalCardPaymentAttemptStatus.Approved &&
                !LinklyBackendTerminalClient.HasPendingApprovalEvidenceMatchingAttempt(
                    finalStatus,
                    finalTxnRef,
                    activeAttempt.Amount,
                    activeAttempt.TxnType))
            {
                return LinklyActiveSessionTakeoverResult.Failed(
                    "The previous Linkly approval evidence does not match the persisted transaction and was not acknowledged.");
            }

            if (string.Equals(activeAttempt.OperationKind, "ActiveSession", StringComparison.Ordinal) &&
                outcome == LocalCardPaymentAttemptStatus.Approved)
            {
                // 无订单草稿的 generic 记录不能自动完成旧单；ack 后继续留在异常中心等待主管核实。
                outcome = LocalCardPaymentAttemptStatus.RequiresReview;
            }

            // 3. 完整最终 status/response/session/txn 证据落库成功后才允许 ack。
            var completedAt = DateTimeOffset.UtcNow;
            await RunLocalStoreAsync(
                () => isRefundAttempt
                    ? RequireRefundCasAsync(
                        cardPaymentAttemptRepository.TryUpdateRefundSessionAsync(
                            activeAttempt.AttemptGuid,
                            refundSubmissionToken!,
                            finalStatus.SessionId,
                            finalTxnRef,
                            completedAt,
                            CancellationToken.None))
                    : cardPaymentAttemptRepository.UpdateSessionAsync(
                        activeAttempt.AttemptGuid,
                        finalStatus.SessionId,
                        finalTxnRef,
                        completedAt,
                        CancellationToken.None),
                CancellationToken.None);

            await RunLocalStoreAsync(
                () => isRefundAttempt
                    ? RequireRefundCasAsync(
                        cardPaymentAttemptRepository.TryUpdateRefundOutcomeAsync(
                            activeAttempt.AttemptGuid,
                            refundSubmissionToken!,
                            outcome,
                            finalStatus.ResponseCode,
                            finalStatus.ResponseText,
                            finalTxnRef,
                            completedAt,
                            CancellationToken.None))
                    : cardPaymentAttemptRepository.UpdateOutcomeAsync(
                        activeAttempt.AttemptGuid,
                        outcome,
                        finalStatus.ResponseCode,
                        finalStatus.ResponseText,
                        finalTxnRef,
                        completedAt,
                        CancellationToken.None),
                CancellationToken.None);

            // 4. acknowledge/release；先终端 ack 再本地标记，失败即禁止新扣款。
            await linklyBackendTerminalClient.AcknowledgeSessionAsync(
                settings,
                finalStatus.SessionId,
                cancellationToken);
            await RunLocalStoreAsync(
                () => cardPaymentAttemptRepository.MarkAcknowledgedAsync(
                    activeAttempt.AttemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);

            return LinklyActiveSessionTakeoverResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 真实 caller 取消不能被吞为失败；向上传播由 backend client 的异常处理决定语义。
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LogCardRecoveryWarning("take-over-active-session", null, ex);
            return LinklyActiveSessionTakeoverResult.Failed(
                "The previous Linkly session could not be cleared safely. Do not charge again until it is resolved.");
        }
    }

    private async Task<LocalCardPaymentAttempt> FindOrCreateActiveSessionAttemptAsync(
        CardTerminalSettings settings,
        PosSessionState session,
        LinklyCloudBackendSessionResponse status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(status.SessionId))
        {
            throw new InvalidOperationException("Linkly active session does not contain a SessionId.");
        }

        var openAttempts = await RunLocalStoreAsync(
            () => cardPaymentAttemptRepository!.GetOpenAttemptsAsync(
                session.StoreCode,
                session.DeviceCode,
                settings.Environment.ToString(),
                CancellationToken.None),
            CancellationToken.None);
        var normalizedSessionId = status.SessionId.Trim();
        var sessionMatches = openAttempts
            .Where(candidate => string.Equals(
                NormalizeOptional(candidate.SessionId),
                normalizedSessionId,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (sessionMatches.Length > 1)
        {
            throw new InvalidOperationException("Multiple open Linkly attempts match the active SessionId.");
        }

        if (sessionMatches.Length == 1)
        {
            var matched = sessionMatches[0];
            if (!string.IsNullOrWhiteSpace(status.TxnRef) &&
                !string.IsNullOrWhiteSpace(matched.TxnRef) &&
                !string.Equals(status.TxnRef.Trim(), matched.TxnRef.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The active Linkly SessionId matches an attempt with a different TxnRef.");
            }

            return matched;
        }

        var normalizedTxnRef = NormalizeOptional(status.TxnRef);
        if (normalizedTxnRef is not null)
        {
            var txnMatches = openAttempts
                .Where(candidate => string.Equals(
                    NormalizeOptional(candidate.TxnRef),
                    normalizedTxnRef,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (txnMatches.Length > 1)
            {
                throw new InvalidOperationException("Multiple open Linkly attempts match the active TxnRef.");
            }

            if (txnMatches.Length == 1)
            {
                if (!string.IsNullOrWhiteSpace(txnMatches[0].SessionId))
                {
                    throw new InvalidOperationException(
                        "The active Linkly TxnRef matches an attempt with a different SessionId.");
                }

                return txnMatches[0];
            }
        }

        var now = DateTimeOffset.UtcNow;
        var attempt = new LocalCardPaymentAttempt(
            Guid.NewGuid(),
            normalizedSessionId,
            normalizedTxnRef,
            CardProcessorKind.Linkly.ToString(),
            settings.Environment.ToString(),
            nameof(LinklyConnectionMode.CloudBackendAsync),
            "U",
            ToDecimalAmount(status.CardTransaction?.AmountCents),
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
            () => cardPaymentAttemptRepository!.CreateOrGetActiveSessionAsync(attempt, CancellationToken.None),
            CancellationToken.None);
    }

    private static bool IsTerminalActiveSessionStatus(LinklyCloudBackendSessionResponse status)
    {
        return LinklyBackendTerminalClient.IsApprovedFinalTransaction(status) ||
            string.Equals(status.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, "NotSubmitted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Status, "Canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static LocalCardPaymentAttemptStatus MapActiveSessionOutcome(LinklyCloudBackendSessionResponse status)
    {
        if (LinklyBackendTerminalClient.IsApprovedFinalTransaction(status))
        {
            return LocalCardPaymentAttemptStatus.Approved;
        }

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

    private static void EnsureActiveSessionScopeMatches(
        CardTerminalSettings settings,
        PosSessionState session,
        LinklyCloudBackendSessionResponse status,
        string? expectedSessionId)
    {
        if (string.IsNullOrWhiteSpace(status.SessionId) ||
            !string.Equals(status.Environment, settings.Environment.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(status.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(status.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(expectedSessionId) &&
             !string.Equals(status.SessionId, expectedSessionId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The Linkly active session does not belong to the current store, device, environment, and session identity.");
        }
    }

    private static decimal ToDecimalAmount(long? amountCents)
    {
        return amountCents is long cents ? decimal.Round(cents / 100m, 2, MidpointRounding.AwayFromZero) : 0m;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task MarkCompletedSquareAttemptsAsync(
        IReadOnlyList<PaymentTender> tenders,
        CancellationToken cancellationToken)
    {
        if (squarePaymentAttemptRepository is null)
        {
            return;
        }

        foreach (var attemptGuid in tenders
            .Select(tender => TryReadSquareAttemptTenderKey(tender.IdempotencyKey, out var attemptGuid) ? attemptGuid : (Guid?)null)
            .Where(attemptGuid => attemptGuid is not null)
            .Select(attemptGuid => attemptGuid!.Value)
            .Distinct())
        {
            // 只有订单真正保存后，Square attempt 才能标为完成，避免恢复时漏救订单。
            await RunLocalStoreAsync(
                () => squarePaymentAttemptRepository.MarkOrderCompletedAsync(
                    attemptGuid,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None),
                CancellationToken.None);
        }
    }

    private static async Task<PaymentTenderAttemptResult> AuthorizeExternalTenderAsync(
        decimal amount,
        decimal remainingAmount,
        PosSessionState session,
        string? referenceText,
        CancellationToken cancellationToken,
        Func<decimal, PosSessionState, string?, CancellationToken, Task<PaymentAuthorizationResult>> authorizeAsync,
        PaymentMethodKind method,
        string exceedsRemainingStatusKey,
        string declinedStatusKey,
        string approvedStatusKey)
    {
        if (amount > remainingAmount)
        {
            if (method == PaymentMethodKind.Card)
            {
                ConsoleLog.Write(
                    "CardRefund",
                    $"workflow blocked card refund reason=amount-exceeds-remaining amount={amount:0.00} remaining={remainingAmount:0.00} originalReference={LogValue(referenceText)}");
            }

            return PaymentTenderAttemptResult.Fail(exceedsRemainingStatusKey);
        }

        if (method == PaymentMethodKind.Card)
        {
            var operation = string.IsNullOrWhiteSpace(referenceText) ? "payment" : "refund";
            ConsoleLog.Write(
                "CardRefund",
                $"workflow terminal {operation} start amount={amount:0.00} remaining={remainingAmount:0.00} originalReference={LogValue(referenceText)}");
        }

        var authorization = await authorizeAsync(amount, session, referenceText, cancellationToken);
        if (method == PaymentMethodKind.Card)
        {
            var operation = string.IsNullOrWhiteSpace(referenceText) ? "payment" : "refund";
            ConsoleLog.Write(
                "CardRefund",
                $"workflow terminal {operation} completed approved={authorization.Approved} reference={LogValue(authorization.Reference)} " +
                $"message={LogValue(authorization.Message)} authorizedAmount={authorization.AuthorizedAmount?.ToString("0.00") ?? "<null>"} " +
                $"cardTxCount={authorization.CardTransactions?.Count ?? 0}");
        }

        if (!authorization.Approved)
        {
            return PaymentTenderAttemptResult.Fail(
                string.IsNullOrWhiteSpace(authorization.StatusKey) ? declinedStatusKey : authorization.StatusKey,
                authorization.Message);
        }

        var authorizedAmount = decimal.Round(
            authorization.AuthorizedAmount ?? amount,
            2,
            MidpointRounding.AwayFromZero);
        if (authorizedAmount <= 0m)
        {
            return PaymentTenderAttemptResult.Fail(declinedStatusKey, authorization.Message);
        }

        if (authorizedAmount > remainingAmount)
        {
            return PaymentTenderAttemptResult.Fail(exceedsRemainingStatusKey);
        }

        if (method == PaymentMethodKind.Card && authorizedAmount != amount)
        {
            return PaymentTenderAttemptResult.Fail(
                declinedStatusKey,
                "Card terminal authorized amount did not match the requested amount.");
        }

        return PaymentTenderAttemptResult.Success(
            new PaymentTender(method, authorizedAmount, authorization.Reference, CardTransactions: authorization.CardTransactions),
            approvedStatusKey);
    }

    private static async Task<PaymentTenderAttemptResult> AuthorizeRefundTenderAsync(
        decimal amount,
        decimal remainingAmount,
        PosSessionState session,
        string? referenceText,
        CancellationToken cancellationToken,
        Func<decimal, PosSessionState, string?, CancellationToken, Task<PaymentAuthorizationResult>> authorizeAsync,
        PaymentMethodKind method,
        string exceedsRemainingStatusKey,
        string declinedStatusKey,
        string approvedStatusKey)
    {
        if (amount > remainingAmount)
        {
            return PaymentTenderAttemptResult.Fail(exceedsRemainingStatusKey);
        }

        var authorization = await authorizeAsync(amount, session, referenceText, cancellationToken);
        if (!authorization.Approved)
        {
            return PaymentTenderAttemptResult.Fail(
                declinedStatusKey,
                authorization.Message);
        }

        var authorizedAmount = decimal.Round(
            authorization.AuthorizedAmount ?? amount,
            2,
            MidpointRounding.AwayFromZero);
        if (authorizedAmount <= 0m)
        {
            return PaymentTenderAttemptResult.Fail(declinedStatusKey, authorization.Message);
        }

        if (authorizedAmount > remainingAmount)
        {
            return PaymentTenderAttemptResult.Fail(exceedsRemainingStatusKey);
        }

        if (method == PaymentMethodKind.Card && authorizedAmount != amount)
        {
            return PaymentTenderAttemptResult.Fail(
                declinedStatusKey,
                "Card terminal authorized amount did not match the requested amount.");
        }

        var reference = method == PaymentMethodKind.Card
            ? CardRefundReference.Format(authorization.Reference, referenceText!)
            : authorization.Reference;
        return PaymentTenderAttemptResult.Success(
            new PaymentTender(method, -authorizedAmount, reference, CardTransactions: authorization.CardTransactions),
            approvedStatusKey);
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
    }

    private static PaymentTenderAttemptResult AuthorizeRefundTenderAsync(
        decimal amount,
        decimal remainingAmount,
        PosSessionState session,
        string? referenceText,
        CancellationToken cancellationToken,
        PaymentMethodKind method,
        string exceedsRemainingStatusKey,
        string approvedStatusKey)
    {
        _ = session;
        _ = referenceText;
        cancellationToken.ThrowIfCancellationRequested();

        if (amount > remainingAmount)
        {
            return PaymentTenderAttemptResult.Fail(exceedsRemainingStatusKey);
        }

        return PaymentTenderAttemptResult.Success(
            new PaymentTender(method, -RoundCurrency(amount), "VOUCHER_REFUND_PENDING", IdempotencyKey: Guid.NewGuid().ToString("N")),
            approvedStatusKey);
    }

    private static LocalOrder PrepareOrderForVoucherRefundPersistence(LocalOrder order)
    {
        var updatedPayments = new List<LocalPayment>(order.Payments.Count);
        var changed = false;

        foreach (var payment in order.Payments)
        {
            if (!IsVoucherRefundPayment(payment))
            {
                updatedPayments.Add(payment);
                continue;
            }

            var normalizedIdempotencyKey = EnsureRefundVoucherIdempotencyKey(order.OrderGuid, payment);
            var normalizedReference = HasIssuedVoucherRefundReference(payment.Reference)
                ? payment.Reference?.Trim()
                : "VOUCHER_REFUND_PENDING";
            var updatedPayment = payment with
            {
                Reference = normalizedReference,
                IdempotencyKey = normalizedIdempotencyKey
            };
            updatedPayments.Add(updatedPayment);
            changed |= !Equals(updatedPayment, payment);
        }

        return changed
            ? order with { Payments = updatedPayments }
            : order;
    }

    private async Task<LocalOrder> PrepareOrderForRecoverableCardPersistenceAsync(
        LocalOrder order,
        IReadOnlyList<PaymentTender> tenders,
        CancellationToken cancellationToken)
    {
        var attemptGuid = tenders
            .Select(tender => TryReadCardAttemptTenderKey(tender.IdempotencyKey, out var attemptGuid) ? attemptGuid : (Guid?)null)
            .FirstOrDefault(value => value is not null);
        if (attemptGuid is not null && cardPaymentAttemptRepository is not null)
        {
            var attempt = await RunLocalStoreAsync(
                () => cardPaymentAttemptRepository.GetAttemptAsync(attemptGuid.Value, cancellationToken),
                cancellationToken);
            if (attempt is not null)
            {
                var draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(attempt.OrderDraftJson, CardAttemptJsonOptions);
                // 正常落单和重启恢复必须使用同一个订单 GUID，避免崩溃后恢复重复保存订单。
                return draft is null ? order : order with { OrderGuid = draft.OrderGuid };
            }
        }

        var squareAttemptGuid = tenders
            .Select(tender => TryReadSquareAttemptTenderKey(tender.IdempotencyKey, out var attemptGuid) ? attemptGuid : (Guid?)null)
            .FirstOrDefault(value => value is not null);
        if (squareAttemptGuid is not null && squarePaymentAttemptRepository is not null)
        {
            var attempt = await RunLocalStoreAsync(
                () => squarePaymentAttemptRepository.GetAttemptAsync(squareAttemptGuid.Value, cancellationToken),
                cancellationToken);
            if (attempt is not null)
            {
                var draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(attempt.OrderDraftJson, CardAttemptJsonOptions);
                // Square 使用独立 attempt 表，但订单 GUID 仍必须和刷卡前草稿保持一致。
                return draft is null ? order : order with { OrderGuid = draft.OrderGuid };
            }
        }

        return order;
    }

    private async Task<LocalOrder> IssuePendingRefundVouchersAsync(
        LocalOrder order,
        PosSessionState session,
        CancellationToken cancellationToken)
    {
        var updatedPayments = new List<LocalPayment>(order.Payments.Count);
        var changed = false;

        foreach (var payment in order.Payments)
        {
            if (!IsPendingVoucherRefundPayment(payment))
            {
                updatedPayments.Add(payment);
                continue;
            }

            var idempotencyKey = EnsureRefundVoucherIdempotencyKey(order.OrderGuid, payment);
            var authorization = await _voucherTenderClient.IssueRefundAsync(
                Math.Abs(payment.Amount),
                session,
                order.OrderGuid.ToString("D"),
                idempotencyKey,
                "Refund",
                cancellationToken);
            if (!authorization.Approved || string.IsNullOrWhiteSpace(authorization.Reference))
            {
                throw new InvalidOperationException(authorization.Message ?? "Voucher refund issuing failed.");
            }

            // 每张退款券发券成功后立刻回写本地引用，避免后续步骤失败时再次展示为待处理。
            await RunLocalStoreAsync(
                () => orderRepository.UpdatePaymentReferenceAsync(payment.PaymentGuid, authorization.Reference, cancellationToken),
                cancellationToken);
            updatedPayments.Add(payment with
            {
                Reference = authorization.Reference,
                IdempotencyKey = idempotencyKey
            });
            changed = true;
        }

        return changed
            ? order with { Payments = updatedPayments }
            : order;
    }

    private static bool IsVoucherRefundPayment(LocalPayment payment)
    {
        return payment.Method == PaymentMethodKind.Voucher && payment.Amount < 0m;
    }

    private static bool IsPendingVoucherRefundPayment(LocalPayment payment)
    {
        return IsVoucherRefundPayment(payment) && !HasIssuedVoucherRefundReference(payment.Reference);
    }

    private static bool HasIssuedVoucherRefundReference(string? reference)
    {
        return !string.IsNullOrWhiteSpace(reference) &&
            !string.Equals(reference.Trim(), "VOUCHER_REFUND_PENDING", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureRefundVoucherIdempotencyKey(Guid orderGuid, LocalPayment payment)
    {
        return string.IsNullOrWhiteSpace(payment.IdempotencyKey)
            ? $"{orderGuid:D}:{payment.PaymentGuid:D}"
            : payment.IdempotencyKey.Trim();
    }

    private static bool HasExistingVoucherTender(
        IReadOnlyList<PaymentTender> currentTenders,
        string? voucherCode)
    {
        var normalizedVoucherCode = NormalizeVoucherCode(voucherCode);
        if (string.IsNullOrWhiteSpace(normalizedVoucherCode))
        {
            return false;
        }

        return currentTenders
            .Where(tender => tender.Method == PaymentMethodKind.Voucher)
            .Select(tender => NormalizeVoucherCode(ParseVoucherCodeFromReference(tender.Reference)))
            .Any(existing => string.Equals(existing, normalizedVoucherCode, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ParseVoucherCodeFromReference(string? reference)
    {
        var parts = (reference ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
            (parts[0].Equals("VOUCHER", StringComparison.OrdinalIgnoreCase) ||
             parts[0].Equals("VOUCHER_REFUND", StringComparison.OrdinalIgnoreCase))
                ? parts[1]
                : reference;
    }

    private static (string? VoucherCode, string? ReservationToken) ParseVoucherReservationFromReference(string? reference)
    {
        var parts = (reference ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        return parts.Length >= 3 && parts[0].Equals("VOUCHER", StringComparison.OrdinalIgnoreCase)
            ? (parts[1], parts[2])
            : (null, null);
    }

    private static string? NormalizeVoucherCode(string? voucherCode)
    {
        return string.IsNullOrWhiteSpace(voucherCode) ? null : voucherCode.Trim();
    }

    private PaymentTenderAttemptResult CreateCashTenderAttempt(decimal amount)
    {
        var normalizedAmount = _cashRoundingPolicy.NormalizeCashTender(amount);
        return normalizedAmount <= 0m
            ? PaymentTenderAttemptResult.Fail("payment.status.invalidAmount")
            : PaymentTenderAttemptResult.Success(
                new PaymentTender(PaymentMethodKind.Cash, normalizedAmount),
                "payment.status.cashTenderAdded");
    }

    private PaymentTenderAttemptResult CreateRefundCashTenderAttempt(decimal amount)
    {
        var normalizedAmount = _cashRoundingPolicy.NormalizeCashTender(amount);
        return normalizedAmount <= 0m
            ? PaymentTenderAttemptResult.Fail("payment.status.invalidAmount")
            : PaymentTenderAttemptResult.Success(
                new PaymentTender(PaymentMethodKind.Cash, -normalizedAmount),
                "payment.status.cashTenderAdded");
    }

    private decimal CalculateExternalRemainingAmount(decimal actualAmount, IReadOnlyList<PaymentTender> currentTenders)
    {
        var remaining = RoundCurrency(RoundCurrency(actualAmount) - CalculateTenderedAmountForActualBalance(currentTenders));
        return Math.Abs(remaining);
    }

    private decimal CalculateTenderedAmountForActualBalance(IReadOnlyList<PaymentTender> tenders)
    {
        return RoundCurrency(tenders.Sum(tender => NormalizeTender(tender).Amount));
    }

    private PaymentTender NormalizeTender(PaymentTender tender)
    {
        var normalizedAmount = tender.Method == PaymentMethodKind.Cash
            ? NormalizeCashTender(tender.Amount)
            : RoundCurrency(tender.Amount);
        return tender with { Amount = normalizedAmount };
    }

    private decimal CalculateRefundRemainingAmount(decimal actualAmount, IReadOnlyList<PaymentTender> tenders)
    {
        var normalizedTenders = tenders.Select(NormalizeTender).ToList();
        var nonCashTotal = RoundCurrency(normalizedTenders
            .Where(tender => tender.Method != PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount));
        var cashTotal = RoundCurrency(normalizedTenders
            .Where(tender => tender.Method == PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount));
        if (cashTotal >= 0m)
        {
            return RoundCurrency(actualAmount - nonCashTotal);
        }

        var roundedCashRefund = _cashRoundingPolicy.CalculateRoundedCashDue(Math.Abs(actualAmount), Math.Abs(nonCashTotal));
        return RoundCurrency(cashTotal + roundedCashRefund);
    }

    private decimal NormalizeCashTender(decimal amount)
    {
        return amount < 0m
            ? -_cashRoundingPolicy.NormalizeCashTender(Math.Abs(amount))
            : _cashRoundingPolicy.NormalizeCashTender(amount);
    }

    private static decimal RoundCurrency(decimal amount)
    {
        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static bool HasTerminalDeclineEvidence(PaymentAuthorizationResult authorization)
    {
        // 只有终端或银行真实返回的代码/文案才触发“银行未批准”弹窗，避免本地校验错误被误报为银行拒付。
        return !string.IsNullOrWhiteSpace(authorization.ResponseCode) ||
            !string.IsNullOrWhiteSpace(authorization.ResponseText) ||
            authorization.CardTransactions?.Any(HasCardTransactionResponseEvidence) == true;
    }

    private static bool HasCardTransactionResponseEvidence(CardTransactionDto transaction)
    {
        return !string.IsNullOrWhiteSpace(transaction.ResponseCode) ||
            !string.IsNullOrWhiteSpace(transaction.ResponseText);
    }

    private static string T(string key)
    {
        return LocalizationResourceProvider.Instance[key];
    }

    private static string FormatLinklyModeDisplayName(string? modeText)
    {
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(modeText, LinklyConnectionMode.LocalIp);
        var key = mode switch
        {
            LinklyConnectionMode.CloudDirectSync => "settings.linkly.mode.cloudDirectSync",
            LinklyConnectionMode.CloudBackendAsync => "settings.linkly.mode.cloudBackendAsync",
            _ => "settings.linkly.mode.localIp"
        };

        // 支付页提示面向收银员，不能暴露 CloudBackendAsync 这类内部配置值。
        return T(key);
    }
}

public interface ICardTerminalClient
{
    Task<PaymentAuthorizationResult> AuthorizeAsync(
        decimal amount,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<PaymentAuthorizationResult> RefundAsync(
        decimal amount,
        PosSessionState session,
        string? originalReference,
        CancellationToken cancellationToken = default);
}

/// <summary>供需要跨重启重放退款的金融流程传入已持久化幂等键。</summary>
public interface IIdempotentCardRefundClient
{
    Task<PaymentAuthorizationResult> RefundAsync(
        decimal amount,
        PosSessionState session,
        string? originalReference,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

// 中文注释：分期恢复只能查询已持久化的终端 attempt，绝不能再次发起收款。
public interface IInstallmentTerminalRecoveryClient
{
    Task<PaymentAuthorizationResult> RecoverLinklyAsync(
        LocalCardPaymentAttempt attempt,
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<PaymentAuthorizationResult> RecoverSquareAsync(
        LocalSquarePaymentAttempt attempt,
        PosSessionState session,
        CancellationToken cancellationToken = default);
}

public interface IVoucherTenderClient
{
    Task<PaymentAuthorizationResult> RedeemAsync(
        decimal amount,
        PosSessionState session,
        string? voucherCode,
        CancellationToken cancellationToken = default);

    Task<PaymentAuthorizationResult> IssueRefundAsync(
        decimal amount,
        PosSessionState session,
        string orderReference,
        string idempotencyKey,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        PosSessionState session,
        string voucherCode,
        string reservationToken,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentAuthorizationResult(
    bool Approved,
    string? Reference = null,
    string? Message = null,
    decimal? AuthorizedAmount = null,
    IReadOnlyList<CardTransactionDto>? CardTransactions = null,
    string? Processor = null,
    string? Environment = null,
    string? ConnectionMode = null,
    string? TxnType = null,
    string? SessionId = null,
    string? TxnRef = null,
    string? ResponseCode = null,
    string? ResponseText = null,
    string? StatusKey = null,
    string? RequestedConnectionMode = null,
    string? ActualConnectionMode = null,
    IReadOnlyList<string>? FallbackAttemptedModes = null,
    bool FallbackSucceeded = false,
    bool FallbackAllowed = false,
    bool ResultUnknown = false);

public sealed record CardPaymentOrderDraft(
    Guid OrderGuid,
    PosSessionState Session,
    PosCartSnapshot CartSnapshot,
    IReadOnlyList<PaymentTender> CurrentTenders,
    decimal ActualAmount,
    decimal CardAmount,
    string TxnType,
    string? OriginalReference,
    DateTimeOffset CreatedAt);

public sealed record PaymentTenderAttemptResult(
    bool Succeeded,
    string StatusKey,
    PaymentTender? Tender = null,
    string? StatusMessage = null,
    bool IsTerminalDecline = false,
    CardPaymentResultDisposition? CardResult = null,
    CardRecoveryAttemptKey? RecoveryAttemptKey = null,
    Guid? RecoveryOrderGuid = null)
{
    public static PaymentTenderAttemptResult Success(PaymentTender tender, string statusKey, string? statusMessage = null)
    {
        return new PaymentTenderAttemptResult(true, statusKey, tender, statusMessage);
    }

    public static PaymentTenderAttemptResult Fail(
        string statusKey,
        string? statusMessage = null,
        bool isTerminalDecline = false)
    {
        return new PaymentTenderAttemptResult(false, statusKey, null, statusMessage, isTerminalDecline);
    }
}

public sealed class UnavailableCardTerminalClient : ICardTerminalClient
{
    public static UnavailableCardTerminalClient Instance { get; } = new();

    private UnavailableCardTerminalClient()
    {
    }

    public Task<PaymentAuthorizationResult> AuthorizeAsync(
        decimal amount,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentAuthorizationResult(false));
    }

    public Task<PaymentAuthorizationResult> RefundAsync(
        decimal amount,
        PosSessionState session,
        string? originalReference,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentAuthorizationResult(false));
    }
}

public sealed class UnavailableVoucherTenderClient : IVoucherTenderClient
{
    public static UnavailableVoucherTenderClient Instance { get; } = new();

    private UnavailableVoucherTenderClient()
    {
    }

    public Task<PaymentAuthorizationResult> RedeemAsync(
        decimal amount,
        PosSessionState session,
        string? voucherCode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentAuthorizationResult(false));
    }

    public Task<PaymentAuthorizationResult> IssueRefundAsync(
        decimal amount,
        PosSessionState session,
        string orderReference,
        string idempotencyKey,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentAuthorizationResult(false));
    }

    public Task<bool> ReleaseAsync(
        PosSessionState session,
        string voucherCode,
        string reservationToken,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}

public sealed record CashPaymentWorkflowResult(
    LocalOrder Order,
    decimal TenderedAmount,
    decimal ChangeAmount,
    int PendingSyncCount,
    PosSessionState UpdatedSession,
    bool HasPostCommitWarning = false);

public sealed class PaymentUploadFailedException : InvalidOperationException
{
    public PaymentUploadFailedException(
        Guid orderGuid,
        decimal tenderedAmount,
        decimal changeAmount,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        OrderGuid = orderGuid;
        TenderedAmount = tenderedAmount;
        ChangeAmount = changeAmount;
    }

    public Guid OrderGuid { get; }

    public decimal TenderedAmount { get; }

    public decimal ChangeAmount { get; }
}

public sealed class CardPaymentPersistenceUnknownException : InvalidOperationException
{
    public CardPaymentPersistenceUnknownException(
        Guid orderGuid,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        OrderGuid = orderGuid;
    }

    public Guid OrderGuid { get; }
}
