using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Services;

internal sealed class CardRecoveryPresenter
{
    private enum ActiveSessionRecoveryDialogAction
    {
        Close,
        Retry,
        ManualConfirm
    }

    private readonly ICardPaymentRecoveryService? _cardPaymentRecoveryService;
    private readonly ICardRecoveryResultDialogService? _cardRecoveryResultDialogService;
    private readonly IReceiptQueryService _receiptQueryService;
    private readonly IReceiptPrinterSettingsStore? _receiptPrinterSettingsStore;
    private readonly IReceiptTextFormatter _receiptTextFormatter;
    private readonly ILocalizationService _localization;
    private readonly ILinklyFallbackPromptCoordinator? _linklyFallbackPromptCoordinator;
    private readonly ILinklyBankReceiptPrinter? _linklyBankReceiptPrinter;
    private readonly MainChildViewModelFactory _mainChildViewModelFactory;
    private readonly PosCartService _cart;
    private readonly Action<string?>? _setStatusMessage;
    private readonly Func<Window?>? _getOwner;
    private readonly Func<Task>? _navigateToPaymentOnDraft;
    private readonly Func<PosSessionState>? _getSession;
    private readonly Action<PosSessionState>? _setSession;
    private readonly Action<LocalOrder>? _onCardRecoveryOrderCompleted;
    private readonly Action<IReadOnlyList<PaymentTender>?, string?>? _onCardRecoveryDraftRestored;
    private readonly Func<Task>? _refreshPendingSyncAsync;
    private readonly Func<ReceiptDetails, ReceiptPrintReason, Task<ReceiptPrintResult>>? _printReceiptAsync;
    private readonly Func<bool>? _canPrintReceipt;
    private readonly Action? _notifyShowCashPaymentCanExecuteChanged;
    private readonly Action? _notifyPrintRecoveredReceiptCanExecuteChanged;
    private readonly Action<string>? _notifyPropertyChanged;

    private Task<CardPaymentRecoveryResult>? _cardPaymentRecoveryTask;
    private ReceiptDetails? _cardRecoveryDialogReceipt;
    private TaskCompletionSource<ActiveSessionRecoveryDialogAction>? _activeSessionRecoveryDialogActionSource;

    public CardRecoveryPresenter(
        ICardPaymentRecoveryService? cardPaymentRecoveryService,
        ICardRecoveryResultDialogService? cardRecoveryResultDialogService,
        IReceiptQueryService receiptQueryService,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore,
        IReceiptTextFormatter receiptTextFormatter,
        ILocalizationService localization,
        ILinklyFallbackPromptCoordinator? linklyFallbackPromptCoordinator,
        ILinklyBankReceiptPrinter? linklyBankReceiptPrinter,
        MainChildViewModelFactory mainChildViewModelFactory,
        PosCartService cart,
        Action<string?>? setStatusMessage = null,
        Func<Window?>? getOwner = null,
        Func<Task>? navigateToPaymentOnDraft = null,
        Func<PosSessionState>? getSession = null,
        Action<PosSessionState>? setSession = null,
        Action<LocalOrder>? onCardRecoveryOrderCompleted = null,
        Action<IReadOnlyList<PaymentTender>?, string?>? onCardRecoveryDraftRestored = null,
        Func<Task>? refreshPendingSyncAsync = null,
        Func<ReceiptDetails, ReceiptPrintReason, Task<ReceiptPrintResult>>? printReceiptAsync = null,
        Func<bool>? canPrintReceipt = null,
        Action? notifyShowCashPaymentCanExecuteChanged = null,
        Action? notifyPrintRecoveredReceiptCanExecuteChanged = null,
        Action<string>? notifyPropertyChanged = null)
    {
        _cardPaymentRecoveryService = cardPaymentRecoveryService;
        _cardRecoveryResultDialogService = cardRecoveryResultDialogService;
        _receiptQueryService = receiptQueryService;
        _receiptPrinterSettingsStore = receiptPrinterSettingsStore;
        _receiptTextFormatter = receiptTextFormatter;
        _localization = localization;
        _linklyFallbackPromptCoordinator = linklyFallbackPromptCoordinator;
        _linklyBankReceiptPrinter = linklyBankReceiptPrinter;
        _mainChildViewModelFactory = mainChildViewModelFactory;
        _cart = cart;
        _setStatusMessage = setStatusMessage;
        _getOwner = getOwner;
        _navigateToPaymentOnDraft = navigateToPaymentOnDraft;
        _getSession = getSession;
        _setSession = setSession;
        _onCardRecoveryOrderCompleted = onCardRecoveryOrderCompleted;
        _onCardRecoveryDraftRestored = onCardRecoveryDraftRestored;
        _refreshPendingSyncAsync = refreshPendingSyncAsync;
        _printReceiptAsync = printReceiptAsync;
        _canPrintReceipt = canPrintReceipt;
        _notifyShowCashPaymentCanExecuteChanged = notifyShowCashPaymentCanExecuteChanged;
        _notifyPrintRecoveredReceiptCanExecuteChanged = notifyPrintRecoveredReceiptCanExecuteChanged;
        _notifyPropertyChanged = notifyPropertyChanged;

        CloseCardRecoveryResultDialogCommand = new RelayCommand(CloseCardRecoveryResultDialog);
        PrintRecoveredReceiptCommand = new AsyncRelayCommand(PrintRecoveredReceiptAsync, CanPrintRecoveredReceipt);
        RetryActiveSessionRecoveryCommand = new RelayCommand(
            () => CompleteActiveSessionRecoveryDialog(ActiveSessionRecoveryDialogAction.Retry));
        ManualConfirmActiveSessionRecoveryCommand = new RelayCommand(
            () => CompleteActiveSessionRecoveryDialog(ActiveSessionRecoveryDialogAction.ManualConfirm));

        if (_cardRecoveryResultDialogService is not null)
        {
            _cardRecoveryResultDialogService.DialogRequested += OnCardRecoveryResultDialogRequested;
        }
    }

    // ---- State properties ----

    public CardRecoveryResultDialogViewModel? CardRecoveryResultDialog { get; set; }

    public bool IsCardRecoveryResultDialogOpen { get; set; }

    // ---- Commands ----

    public IRelayCommand CloseCardRecoveryResultDialogCommand { get; }

    public IAsyncRelayCommand PrintRecoveredReceiptCommand { get; }

    public IRelayCommand RetryActiveSessionRecoveryCommand { get; }

    public IRelayCommand ManualConfirmActiveSessionRecoveryCommand { get; }

    // ---- Public methods ----

    public async Task<bool> RecoverCardPaymentAttemptAsync(bool navigateToPaymentOnDraft)
    {
        if (_cardPaymentRecoveryService is null)
        {
            return false;
        }

        var recoveryTask = _cardPaymentRecoveryTask;
        if (recoveryTask is null)
        {
            recoveryTask = _cardPaymentRecoveryService.RecoverLatestAsync(_cart, GetSession(), CancellationToken.None);
            _cardPaymentRecoveryTask = recoveryTask;
        }

        CardPaymentRecoveryResult result;
        try
        {
            result = await recoveryTask;
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_cardPaymentRecoveryTask, recoveryTask))
            {
                _cardPaymentRecoveryTask = null;
            }

            throw;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_cardPaymentRecoveryTask, recoveryTask))
            {
                _cardPaymentRecoveryTask = null;
            }

            ConsoleLog.WriteError(
                "CardRecovery",
                $"recover latest card payment failed error={ex.GetType().Name} message={ex.Message}",
                exception: ex);
            throw;
        }

        if (ShouldRetryCardPaymentRecovery(result.Outcome) &&
            ReferenceEquals(_cardPaymentRecoveryTask, recoveryTask))
        {
            _cardPaymentRecoveryTask = null;
        }

        if (result.Outcome == CardPaymentRecoveryOutcome.None)
        {
            return false;
        }

        _setStatusMessage?.Invoke(result.Message);
        if (result.UpdatedSession is not null)
        {
            _setSession?.Invoke(result.UpdatedSession);
        }

        if (result.Outcome == CardPaymentRecoveryOutcome.OrderCompleted && result.Order is not null)
        {
            if (_refreshPendingSyncAsync is not null)
            {
                await _refreshPendingSyncAsync();
            }

            LogRecoveredCardOrderCompleted(result.Order);
            var printResult = await PrintRecoveredCardReceiptAsync(result.Order);
            _onCardRecoveryOrderCompleted?.Invoke(result.Order);
            await ShowRecoveredCardOrderDialogAsync(result, printResult);
            _notifyShowCashPaymentCanExecuteChanged?.Invoke();
            return true;
        }

        if (result.Outcome == CardPaymentRecoveryOutcome.DraftRestored)
        {
            var restoredTenders = result.RestoredTenders;
            var hasRestoredTenders = restoredTenders is { Count: > 0 };

            if ((navigateToPaymentOnDraft || hasRestoredTenders) && _navigateToPaymentOnDraft is not null)
            {
                await _navigateToPaymentOnDraft();
            }

            _onCardRecoveryDraftRestored?.Invoke(restoredTenders, hasRestoredTenders ? result.Message : null);
            _notifyShowCashPaymentCanExecuteChanged?.Invoke();

            ShowRecoveredCardDraftDialog(result);
            return true;
        }

        if (result.Outcome == CardPaymentRecoveryOutcome.Unknown)
        {
            ShowRecoveredCardFailureDialog(result);
        }

        return false;
    }

    public async Task<bool> RecoverActiveCardPaymentSessionFromPaymentAsync()
    {
        if (_cardPaymentRecoveryService is null)
        {
            return false;
        }

        while (true)
        {
            var result = await _cardPaymentRecoveryService.RecoverActiveSessionAsync(_cart, GetSession(), CancellationToken.None);
            ApplyRecoveryStatus(result);

            if (result.Outcome == CardPaymentRecoveryOutcome.None)
            {
                return true;
            }

            if (result.Outcome == CardPaymentRecoveryOutcome.DraftRestored)
            {
                // 付款页主动恢复的是旧 active session，不能把恢复结果自动混入当前购物车。
                ShowRecoveredCardDraftDialog(result);
                return true;
            }

            if (result.Outcome is CardPaymentRecoveryOutcome.ActiveSessionApproved or CardPaymentRecoveryOutcome.ActiveSessionNotPaid)
            {
                var printResult = await PrintActiveSessionBankReceiptAsync(result);
                if (!printResult.Succeeded)
                {
                    _setStatusMessage?.Invoke(FormatActiveSessionPrintFailureMessage(result.Message, printResult.Message));
                }

                ShowRecoveredActiveSessionDialog(result, printResult);
                return true;
            }

            if (result.Outcome == CardPaymentRecoveryOutcome.ActiveSessionManuallyCleared)
            {
                return true;
            }

            if (result.Outcome is CardPaymentRecoveryOutcome.Unknown or CardPaymentRecoveryOutcome.Checking)
            {
                var action = await ShowActiveSessionUnresolvedDialogAsync(result);
                if (action == ActiveSessionRecoveryDialogAction.Retry)
                {
                    continue;
                }

                if (action == ActiveSessionRecoveryDialogAction.ManualConfirm)
                {
                    return await ManuallyClearActiveSessionAsync(result);
                }

                return false;
            }

            // 其它已确认结果表示上一笔不再处于未知状态，付款页可以解除本地阻塞。
            return true;
        }
    }

    private void ApplyRecoveryStatus(CardPaymentRecoveryResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            _setStatusMessage?.Invoke(result.Message);
        }

        if (result.UpdatedSession is not null)
        {
            _setSession?.Invoke(result.UpdatedSession);
        }
    }

    public void DetachDialogService()
    {
        if (_cardRecoveryResultDialogService is not null)
        {
            _cardRecoveryResultDialogService.DialogRequested -= OnCardRecoveryResultDialogRequested;
        }
    }

    // ---- Private methods ----

    private PosSessionState GetSession()
    {
        return _getSession?.Invoke() ?? new PosSessionState("HB POS", "1002", "Main Branch", "Terminal 04", "C001", "Alice", false, 0);
    }

    private async Task<ReceiptPrintResult> PrintRecoveredCardReceiptAsync(LocalOrder order)
    {
        var evidence = GetCardRecoveryEvidence(order);
        LinklyJsonLog.Write(
            "CardRecovery",
            "card-recovery",
            "power-fail-recovery-print",
            "request",
            direction: "request",
            sessionId: evidence.SessionId,
            request: new
            {
                reason = ReceiptPrintReason.CardAuto.ToString(),
                orderGuid = order.OrderGuid
            },
            details: new
            {
                timestamp = DateTimeOffset.Now,
                certCase = "4.1.3",
                orderGuid = order.OrderGuid,
                transactionReference = evidence.TransactionReference,
                evidence.TxnRef,
                evidence.SessionId,
                reason = "4.1.3"
            });

        var printResult = await PrintReceiptAsync(ReceiptQueryService.CreateReceipt(order), ReceiptPrintReason.CardAuto);
        LinklyJsonLog.Write(
            "CardRecovery",
            "card-recovery",
            "power-fail-recovery-print",
            "response",
            direction: "response",
            sessionId: evidence.SessionId,
            success: printResult.Succeeded,
            reason: printResult.Succeeded ? null : "receipt-print-failed",
            response: new
            {
                printResult.Succeeded,
                printResult.Message,
                printResult.OrderGuid
            },
            details: new
            {
                timestamp = DateTimeOffset.Now,
                certCase = "4.1.3",
                orderGuid = order.OrderGuid,
                transactionReference = evidence.TransactionReference,
                evidence.TxnRef,
                evidence.SessionId,
                reason = "4.1.3"
            });
        return printResult;
    }

    private async Task ShowRecoveredCardOrderDialogAsync(
        CardPaymentRecoveryResult result,
        ReceiptPrintResult printResult)
    {
        if (result.Order is null)
        {
            return;
        }

        var receipt = ReceiptQueryService.CreateReceipt(result.Order);
        _cardRecoveryDialogReceipt = receipt;
        var previewRows = await BuildReceiptPreviewRowsAsync(receipt);
        var details = result.DialogDetails;
        var printMessage = printResult.Succeeded
            ? _localization.T("cardRecovery.dialog.message.autoPrintSucceeded")
            : string.Format(
                _localization.CurrentCulture,
                _localization.T("cardRecovery.dialog.message.autoPrintFailed"),
                printResult.Message);

        ShowCardRecoveryResultDialog(new CardRecoveryResultDialogViewModel(
            _localization.T("cardRecovery.dialog.title.completed"),
            printMessage,
            printResult.Succeeded ? CardRecoveryResultSeverity.Success : CardRecoveryResultSeverity.Warning,
            result.Order.OrderGuid,
            result.Order.ActualAmount,
            details?.SessionId ?? GetCardRecoveryEvidence(result.Order).SessionId,
            details?.TxnRef ?? GetCardRecoveryEvidence(result.Order).TxnRef,
            details?.ResponseCode ?? GetCardRecoveryResponseCode(result.Order),
            details?.ResponseText ?? GetCardRecoveryResponseText(result.Order),
            details?.Timestamp ?? DateTimeOffset.Now,
            previewRows,
            canPrintReceipt: CanUsePrintReceiptPermission(),
            printButtonText: _localization.T("cardRecovery.dialog.action.printReceipt")));
    }

    private void ShowRecoveredCardDraftDialog(CardPaymentRecoveryResult result)
    {
        var details = result.DialogDetails;
        ShowCardRecoveryResultDialog(new CardRecoveryResultDialogViewModel(
            _localization.T("cardRecovery.dialog.title.draftRestored"),
            string.IsNullOrWhiteSpace(result.Message)
                ? _localization.T("cardRecovery.dialog.message.draftRestoredFallback")
                : result.Message,
            CardRecoveryResultSeverity.Warning,
            orderGuid: null,
            amount: details?.Amount,
            sessionId: details?.SessionId,
            txnRef: details?.TxnRef,
            responseCode: details?.ResponseCode,
            responseText: details?.ResponseText,
            timestamp: details?.Timestamp ?? DateTimeOffset.Now));
    }

    private void ShowRecoveredCardFailureDialog(CardPaymentRecoveryResult result)
    {
        var details = result.DialogDetails;
        ShowCardRecoveryResultDialog(new CardRecoveryResultDialogViewModel(
            _localization.T("cardRecovery.dialog.title.unknown"),
            string.IsNullOrWhiteSpace(result.Message)
                ? _localization.T("cardRecovery.dialog.message.failedFallback")
                : result.Message,
            CardRecoveryResultSeverity.Error,
            orderGuid: null,
            amount: details?.Amount,
            sessionId: details?.SessionId,
            txnRef: details?.TxnRef,
            responseCode: details?.ResponseCode,
            responseText: details?.ResponseText,
            timestamp: details?.Timestamp ?? DateTimeOffset.Now));
    }

    private Task<ActiveSessionRecoveryDialogAction> ShowActiveSessionUnresolvedDialogAsync(CardPaymentRecoveryResult result)
    {
        var details = result.DialogDetails;
        var source = new TaskCompletionSource<ActiveSessionRecoveryDialogAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeSessionRecoveryDialogActionSource?.TrySetResult(ActiveSessionRecoveryDialogAction.Close);
        _activeSessionRecoveryDialogActionSource = source;

        ShowCardRecoveryResultDialog(new CardRecoveryResultDialogViewModel(
            _localization.T("cardRecovery.dialog.title.unknown"),
            string.IsNullOrWhiteSpace(result.Message)
                ? _localization.T("cardRecovery.dialog.message.failedFallback")
                : result.Message,
            CardRecoveryResultSeverity.Error,
            orderGuid: null,
            amount: details?.Amount,
            sessionId: details?.SessionId,
            txnRef: details?.TxnRef,
            responseCode: details?.ResponseCode,
            responseText: details?.ResponseText,
            timestamp: details?.Timestamp ?? DateTimeOffset.Now,
            canRetryRecovery: true,
            retryButtonText: _localization.T("cardRecovery.dialog.action.retryRecovery"),
            canManualConfirm: true,
            manualConfirmButtonText: _localization.T("cardRecovery.dialog.action.confirmCheckedContinue")));

        return source.Task;
    }

    private async Task<bool> ManuallyClearActiveSessionAsync(CardPaymentRecoveryResult result)
    {
        var sessionId = result.DialogDetails?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _setStatusMessage?.Invoke(_localization.T("cardRecovery.linkly.activeSessionManualClearMissing"));
            return false;
        }

        // 关键逻辑：人工确认只负责清掉旧 active session，不保存订单、不添加 tender。
        var clearResult = await _cardPaymentRecoveryService!.ManuallyClearActiveSessionAsync(
            sessionId,
            GetSession(),
            CancellationToken.None);
        ApplyRecoveryStatus(clearResult);
        if (clearResult.Outcome == CardPaymentRecoveryOutcome.ActiveSessionManuallyCleared)
        {
            return true;
        }

        ShowRecoveredCardFailureDialog(clearResult);
        return false;
    }

    private async Task<ReceiptPrintResult> PrintActiveSessionBankReceiptAsync(CardPaymentRecoveryResult result)
    {
        var receipt = result.BankReceipt;
        if (receipt is null)
        {
            return new ReceiptPrintResult(false, _localization.T("cardRecovery.linkly.activeSessionReceiptMissing"));
        }

        if (_linklyBankReceiptPrinter is null)
        {
            return new ReceiptPrintResult(false, _localization.T("cardRecovery.linkly.activeSessionReceiptPrinterMissing"));
        }

        // 关键逻辑：active session 没有本地订单，恢复时只能打印 Linkly 银行凭证，不能拼接 POS 商品小票。
        return await _linklyBankReceiptPrinter.PrintAsync(
            receipt.Environment,
            receipt.SessionId,
            receipt.ReceiptText,
            receipt.Kind,
            responseCode: receipt.ResponseCode,
            responseText: receipt.ResponseText);
    }

    private void ShowRecoveredActiveSessionDialog(
        CardPaymentRecoveryResult result,
        ReceiptPrintResult printResult)
    {
        var details = result.DialogDetails;
        var isApproved = result.Outcome == CardPaymentRecoveryOutcome.ActiveSessionApproved;
        var message = printResult.Succeeded
            ? result.Message
            : FormatActiveSessionPrintFailureMessage(result.Message, printResult.Message);

        ShowCardRecoveryResultDialog(new CardRecoveryResultDialogViewModel(
            isApproved
                ? _localization.T("cardRecovery.dialog.title.activeSessionApproved")
                : _localization.T("cardRecovery.dialog.title.activeSessionNotPaid"),
            message,
            isApproved && printResult.Succeeded ? CardRecoveryResultSeverity.Success : CardRecoveryResultSeverity.Warning,
            orderGuid: null,
            amount: details?.Amount,
            sessionId: details?.SessionId,
            txnRef: details?.TxnRef,
            responseCode: details?.ResponseCode,
            responseText: details?.ResponseText,
            timestamp: details?.Timestamp ?? DateTimeOffset.Now));
    }

    private string FormatActiveSessionPrintFailureMessage(string recoveryMessage, string printMessage)
    {
        var failure = string.Format(
            _localization.CurrentCulture,
            _localization.T("cardRecovery.linkly.activeSessionReceiptPrintFailed"),
            printMessage);

        return string.IsNullOrWhiteSpace(recoveryMessage)
            ? failure
            : $"{recoveryMessage} {failure}";
    }

    private void OnCardRecoveryResultDialogRequested(object? sender, CardRecoveryResultDialogViewModel dialog)
    {
        ShowCardRecoveryResultDialog(dialog);
    }

    private void ShowCardRecoveryResultDialog(CardRecoveryResultDialogViewModel dialog)
    {
        CardRecoveryResultDialog = dialog;
        IsCardRecoveryResultDialogOpen = true;
        _notifyPropertyChanged?.Invoke(nameof(CardRecoveryResultDialog));
        _notifyPropertyChanged?.Invoke(nameof(IsCardRecoveryResultDialogOpen));
        _notifyPrintRecoveredReceiptCanExecuteChanged?.Invoke();
    }

    private void CloseCardRecoveryResultDialog()
    {
        var activeSessionSource = _activeSessionRecoveryDialogActionSource;
        _activeSessionRecoveryDialogActionSource = null;
        IsCardRecoveryResultDialogOpen = false;
        CardRecoveryResultDialog = null;
        _cardRecoveryDialogReceipt = null;
        _notifyPropertyChanged?.Invoke(nameof(IsCardRecoveryResultDialogOpen));
        _notifyPropertyChanged?.Invoke(nameof(CardRecoveryResultDialog));
        _notifyPrintRecoveredReceiptCanExecuteChanged?.Invoke();
        activeSessionSource?.TrySetResult(ActiveSessionRecoveryDialogAction.Close);
    }

    private void CompleteActiveSessionRecoveryDialog(ActiveSessionRecoveryDialogAction action)
    {
        var activeSessionSource = _activeSessionRecoveryDialogActionSource;
        _activeSessionRecoveryDialogActionSource = null;
        CloseCardRecoveryResultDialog();
        activeSessionSource?.TrySetResult(action);
    }

    private bool CanPrintRecoveredReceipt()
    {
        return CardRecoveryResultDialog?.CanPrintReceipt == true &&
            _cardRecoveryDialogReceipt is not null &&
            CanUsePrintReceiptPermission();
    }

    private bool CanUsePrintReceiptPermission()
    {
        // 关键逻辑：恢复弹窗可能在主页面之外出现，打印按钮也必须服从当前收银员权限。
        return _canPrintReceipt?.Invoke() ?? true;
    }

    private async Task PrintRecoveredReceiptAsync()
    {
        // 关键逻辑：命令可能被测试或脚本直接调用，不能只依赖按钮禁用来拦截无权限手动打印。
        var receipt = _cardRecoveryDialogReceipt;
        if (receipt is null || !CanPrintRecoveredReceipt())
        {
            return;
        }

        await PrintReceiptAsync(receipt, ReceiptPrintReason.CardAuto);
    }

    private async Task<IReadOnlyList<ReceiptPreviewRow>> BuildReceiptPreviewRowsAsync(ReceiptDetails receipt)
    {
        var settings = ReceiptPrinterSettings.Default;
        if (_receiptPrinterSettingsStore is not null)
        {
            try
            {
                settings = await _receiptPrinterSettingsStore.LoadAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                settings = ReceiptPrinterSettings.Default;
            }
        }

        try
        {
            return _receiptTextFormatter.Build(receipt, settings, receipt.SoldAt).PreviewRows;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private async Task<ReceiptPrintResult> PrintReceiptAsync(ReceiptDetails receipt, ReceiptPrintReason reason)
    {
        return await _printReceiptAsync!(receipt, reason);
    }

    private static string? GetCardRecoveryResponseCode(LocalOrder order)
    {
        return order.Payments
            .FirstOrDefault(payment => payment.Method == PaymentMethodKind.Card)?
            .CardTransactions?
            .FirstOrDefault()?
            .ResponseCode;
    }

    private static string? GetCardRecoveryResponseText(LocalOrder order)
    {
        return order.Payments
            .FirstOrDefault(payment => payment.Method == PaymentMethodKind.Card)?
            .CardTransactions?
            .FirstOrDefault()?
            .ResponseText;
    }

    private static void LogRecoveredCardOrderCompleted(LocalOrder order)
    {
        var evidence = GetCardRecoveryEvidence(order);
        LinklyJsonLog.Write(
            "CardRecovery",
            "card-recovery",
            "power-fail-recovery",
            "order-completed",
            sessionId: evidence.SessionId,
            success: true,
            details: new
            {
                timestamp = DateTimeOffset.Now,
                certCase = "4.1.2",
                orderGuid = order.OrderGuid,
                transactionReference = evidence.TransactionReference,
                evidence.TxnRef,
                evidence.SessionId,
                reason = "4.1.2"
            });
    }

    private static CardRecoveryEvidence GetCardRecoveryEvidence(LocalOrder order)
    {
        var cardPayment = order.Payments.FirstOrDefault(payment => payment.Method == PaymentMethodKind.Card);
        var cardTransaction = cardPayment?.CardTransactions?.FirstOrDefault();
        var txnRef = NormalizeEvidenceValue(cardTransaction?.TxnRef) ?? TryReadLinklyBackendTxnRef(cardPayment?.Reference);
        var sessionId = LinklyBackendPaymentReference.TryGetPrintMarker(cardPayment?.Reference, out _, out var markerSessionId)
            ? NormalizeEvidenceValue(markerSessionId)
            : null;
        return new CardRecoveryEvidence(
            NormalizeEvidenceValue(sessionId) ?? NormalizeEvidenceValue(txnRef) ?? order.OrderGuid.ToString("D"),
            NormalizeEvidenceValue(txnRef),
            NormalizeEvidenceValue(sessionId));
    }

    private static string? TryReadLinklyBackendTxnRef(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            !reference.StartsWith($"{LinklyBackendPaymentReference.Prefix}:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? NormalizeEvidenceValue(parts[1]) : null;
    }

    private static string? NormalizeEvidenceValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record CardRecoveryEvidence(string TransactionReference, string? TxnRef, string? SessionId);

    private static bool ShouldRetryCardPaymentRecovery(CardPaymentRecoveryOutcome outcome)
    {
        return outcome is CardPaymentRecoveryOutcome.None or
            CardPaymentRecoveryOutcome.Checking or
            CardPaymentRecoveryOutcome.Unknown;
    }
}
