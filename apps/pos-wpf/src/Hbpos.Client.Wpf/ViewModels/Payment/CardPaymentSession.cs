using System.Globalization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.ViewModels;

/// <summary>
/// Encapsulates Card payment state machine — CTS lifecycle, cancellation tracking,
/// error overlay classification, Linkly fallback prompts, and result recovery.
/// PaymentViewModel delegates all Card-specific logic here so that
/// <c>AddTenderByMethodAsync</c> no longer mixes three payment methods inline.
/// </summary>
internal sealed class CardPaymentSession
{
    private CancellationTokenSource? _activeCardPaymentCts;
    private CancellationTokenSource? _manuallyCancelledCardPaymentCts;
    private bool _cardPaymentCancellationRequested;
    private bool _awaitingLateCardResultAfterManualCancel;
    private bool _discardLateCardResultAfterManualCancel;
    private bool _cardPaymentResultUnknownRequiresRecovery;
    private TaskCompletionSource<bool>? _pendingLinklyFallbackPrompt;

    private readonly PaymentViewModel _vm;

    public CardPaymentSession(PaymentViewModel vm)
    {
        _vm = vm;
    }

    // ── Read-only queries ──

    public bool IsActive => _activeCardPaymentCts is not null;
    public bool HasUnknownResult => _cardPaymentResultUnknownRequiresRecovery;
    public bool IsAwaitingLateResult => _awaitingLateCardResultAfterManualCancel;
    public bool HasPendingFallbackPrompt => _pendingLinklyFallbackPrompt is not null;

    // ── State accessors (used by PaymentViewModel) ──

    public void SetResultUnknownRecoveryRequired(bool value)
    {
        _cardPaymentResultUnknownRequiresRecovery = value;
    }

    public CancellationTokenSource? ActiveCardPaymentCts => _activeCardPaymentCts;

    // ── Card payment entry: BEGIN ──

    public CancellationTokenSource BeginCardPayment()
    {
        _cardPaymentCancellationRequested = false;
        _awaitingLateCardResultAfterManualCancel = false;
        _discardLateCardResultAfterManualCancel = false;
        _activeCardPaymentCts?.Dispose();
        _activeCardPaymentCts = new CancellationTokenSource();
        _vm.IsCardPaymentInProgress = true;
        _vm.IsPaymentInteractionLocked = true;
        _vm.SetStatus("payment.status.cardProcessing");
        return _activeCardPaymentCts;
    }

    // ── Card payment exit: END (finally block) ──

    public void EndCardPayment(CancellationTokenSource? cardPaymentCts)
    {
        if (!ReferenceEquals(_activeCardPaymentCts, cardPaymentCts))
        {
            return;
        }

        var active = _activeCardPaymentCts;
        _activeCardPaymentCts = null;
        if (ReferenceEquals(_manuallyCancelledCardPaymentCts, cardPaymentCts))
        {
            _manuallyCancelledCardPaymentCts = null;
        }

        _cardPaymentCancellationRequested = false;
        _vm.IsCardPaymentInProgress = false;
        _vm.IsPaymentInteractionLocked = false;
        active?.Dispose();
        _vm.NotifyPaymentCommandStates();
    }

    // ── Manual cancellation ──

    public void Cancel()
    {
        if (_activeCardPaymentCts is null || _activeCardPaymentCts.IsCancellationRequested)
        {
            return;
        }

        _cardPaymentCancellationRequested = true;
        _awaitingLateCardResultAfterManualCancel = true;
        _discardLateCardResultAfterManualCancel = false;
        _manuallyCancelledCardPaymentCts = _activeCardPaymentCts;
        _activeCardPaymentCts.Cancel();
        _vm.IsCardPaymentInProgress = false;
        _vm.IsPaymentInteractionLocked = false;
        _vm.SetStatus("payment.status.cardCancelled");
        _vm.NotifyPaymentCommandStates();
    }

    public void DetachCanceledActiveCardPayment()
    {
        if (_activeCardPaymentCts?.IsCancellationRequested != true)
        {
            return;
        }

        if (ReferenceEquals(_manuallyCancelledCardPaymentCts, _activeCardPaymentCts))
        {
            _manuallyCancelledCardPaymentCts = null;
        }

        _activeCardPaymentCts = null;
        _cardPaymentCancellationRequested = false;
    }

    // ── Cancellation classification ──

    public bool IsManualCancellation(CancellationTokenSource? cardPaymentCts)
    {
        return _cardPaymentCancellationRequested || ReferenceEquals(_manuallyCancelledCardPaymentCts, cardPaymentCts);
    }

    public void SetCancellationStatus(bool wasManuallyCancelled)
    {
        _vm.SetStatus(wasManuallyCancelled ? "payment.status.cardCancelled" : "payment.status.cardTimedOut");
    }

    public void ResetManualCancellationState()
    {
        _awaitingLateCardResultAfterManualCancel = false;
        _discardLateCardResultAfterManualCancel = false;
    }

    public bool ShouldDiscardLateResult
    {
        get => _discardLateCardResultAfterManualCancel;
        set => _discardLateCardResultAfterManualCancel = value;
    }

    // ── Exception handling ──

    public void HandleOperationCanceledException(CancellationTokenSource? cardPaymentCts, int paymentEntryVersion)
    {
        if (!_vm.IsCurrentPaymentEntry(paymentEntryVersion))
        {
            return;
        }

        SetCancellationStatus(IsManualCancellation(cardPaymentCts));
        ResetManualCancellationState();
        _vm.NotifyPaymentCommandStates();
    }

    public void HandleUnexpectedException(Exception ex, int paymentEntryVersion)
    {
        ConsoleLog.Write("CardPayment", $"unexpected card payment exception: {ex}");
        if (!_vm.IsCurrentPaymentEntry(paymentEntryVersion))
        {
            return;
        }

        var overlay = CardPaymentErrorOverlayViewModel.Unexpected();
        overlay.IsOpen = true;
        _vm.CardPaymentErrorOverlay = overlay;
        _vm.SetStatus("payment.card.status.failed", ex.Message);
        _vm.NotifyPaymentCommandStates();
    }

    // ── Result classification ──

    public bool TryHandleCancelledResult(
        PaymentTenderAttemptResult result,
        CancellationTokenSource? cardPaymentCts,
        bool cardPaymentWasManuallyCancelled)
    {
        if (cardPaymentCts?.IsCancellationRequested != true)
        {
            return false;
        }

        if (result.Succeeded && result.Tender is not null)
        {
            return false;
        }

        if (cardPaymentWasManuallyCancelled && IsConfirmedCardCancellation(result.StatusMessage))
        {
            SetCancellationStatus(wasManuallyCancelled: true);
        }
        else if (!cardPaymentWasManuallyCancelled)
        {
            SetCancellationStatus(wasManuallyCancelled: false);
        }
        else
        {
            _vm.SetStatus(result.StatusKey, result.StatusMessage);
        }

        ResetManualCancellationState();
        _vm.NotifyPaymentCommandStates();
        return true;
    }

    public bool TryHandleFailedResult(PaymentTenderAttemptResult result)
    {
        ShowOverlayIfTerminalError(result);
        if (IsCardResultUnknownStatusKey(result.StatusKey))
        {
            _cardPaymentResultUnknownRequiresRecovery = true;
            _vm.IsPaymentInteractionLocked = true;
            _vm.SetStatus(result.StatusKey, result.StatusMessage);
            ResetManualCancellationState();
            _vm.NotifyPaymentCommandStates();
            return true;
        }

        if (TrySetCardTerminalFailureStatus(result))
        {
            ResetManualCancellationState();
            _vm.NotifyPaymentCommandStates();
            return true;
        }

        _vm.SetStatus(result.StatusKey, result.StatusMessage);
        ResetManualCancellationState();
        _vm.NotifyPaymentCommandStates();
        return true;
    }

    private bool TrySetCardTerminalFailureStatus(PaymentTenderAttemptResult result)
    {
        if (IsSquareFriendlyStatusKey(result.StatusKey))
        {
            return false;
        }

        if (IsConfirmedCardCancellation(result.StatusMessage))
        {
            _vm.SetStatus("payment.status.cardCancelled");
            return true;
        }

        if (IsTimeoutMessage(result.StatusMessage))
        {
            _vm.SetStatus("payment.status.cardTimedOut");
            return true;
        }

        return false;
    }

    // ── Error overlay ──

    public void CloseErrorOverlay()
    {
        CompletePendingLinklyFallbackPrompt(confirmed: false);
        if (_vm.CardPaymentErrorOverlay is not null)
        {
            _vm.CardPaymentErrorOverlay.IsOpen = false;
        }

        ReleaseFallbackPromptLockIfIdle();
        _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    public bool CanExecuteErrorPrimaryAction()
    {
        return _vm.CardPaymentErrorOverlay is { IsOpen: true, HasPrimaryAction: true } overlay &&
            (overlay.PrimaryActionKind == CardPaymentErrorOverlayPrimaryActionKind.ConfirmFallback ||
             _vm.NavigationActions.CanRecoverPreviousCardTransaction);
    }

    public async Task ExecuteErrorPrimaryActionAsync()
    {
        if (_vm.CardPaymentErrorOverlay?.PrimaryActionKind == CardPaymentErrorOverlayPrimaryActionKind.ConfirmFallback)
        {
            CompletePendingLinklyFallbackPrompt(confirmed: true);
            _vm.CardPaymentErrorOverlay.IsOpen = false;
            ReleaseFallbackPromptLockIfIdle();
            _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
            return;
        }

        if (!_vm.NavigationActions.CanRecoverPreviousCardTransaction)
        {
            return;
        }

        _vm.SetStatus("payment.card.error.overlay.activeSession.recovering");
        var recoveryResolved = await (_vm.NavigationActions.RecoverPreviousCardTransactionAsync?.Invoke() ?? Task.FromResult(false));
        if (recoveryResolved)
        {
            _cardPaymentResultUnknownRequiresRecovery = false;
            _vm.IsPaymentInteractionLocked = false;
            _vm.NotifyPaymentCommandStates();
        }

        if (_vm.CardPaymentErrorOverlay is not null && !_cardPaymentResultUnknownRequiresRecovery)
        {
            _vm.CardPaymentErrorOverlay.IsOpen = false;
        }

        _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    // ── Linkly fallback ──

    public Task<bool> ConfirmLinklyFallbackAsync(
        LinklyFallbackPromptRequest request,
        CancellationToken cancellationToken)
    {
        CompletePendingLinklyFallbackPrompt(confirmed: false);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var prompt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLinklyFallbackPrompt = prompt;
        _vm.CardPaymentErrorOverlay = CardPaymentErrorOverlayViewModel.Fallback(FormatLinklyFallbackPromptMessage(request));
        _vm.CardPaymentErrorOverlay.IsOpen = true;
        _vm.IsPaymentInteractionLocked = true;
        _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
        _vm.NotifyPaymentCommandStates();
        cancellationToken.Register(() => CompletePendingLinklyFallbackPrompt(confirmed: false));
        return prompt.Task;
    }

    public void CompletePendingLinklyFallbackPrompt(bool confirmed)
    {
        var prompt = _pendingLinklyFallbackPrompt;
        if (prompt is null)
        {
            return;
        }

        _pendingLinklyFallbackPrompt = null;
        prompt.TrySetResult(confirmed);
    }

    public void ReleaseFallbackPromptLockIfIdle()
    {
        if (_activeCardPaymentCts is null && !_cardPaymentResultUnknownRequiresRecovery)
        {
            _vm.IsPaymentInteractionLocked = false;
            _vm.NotifyPaymentCommandStates();
        }
    }

    private string FormatLinklyFallbackPromptMessage(LinklyFallbackPromptRequest request)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            _vm.T("payment.card.error.overlay.fallback.message"),
            FormatLinklyModeDisplayName(request.NextMode.ToString()));
    }

    private string FormatLinklyModeDisplayName(string? modeText)
    {
        var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(modeText, LinklyConnectionMode.LocalIp);
        var key = mode switch
        {
            LinklyConnectionMode.CloudDirectSync => "settings.linkly.mode.cloudDirectSync",
            LinklyConnectionMode.CloudBackendAsync => "settings.linkly.mode.cloudBackendAsync",
            _ => "settings.linkly.mode.localIp"
        };

        return _vm.T(key);
    }

    // ── Static classifiers ──

    public static bool IsCardResultUnknownStatusKey(string statusKey)
    {
        return statusKey is
            "linkly.backend.resultUnknown" or
            "linkly.backend.cancelledUnknown" or
            "linkly.cloud.resultUnknown";
    }

    private static bool IsCardDeclinedStatusKey(string statusKey)
    {
        return string.Equals(statusKey, "payment.status.cardDeclined", StringComparison.Ordinal);
    }

    private static bool IsSquareFriendlyStatusKey(string statusKey)
    {
        // Square 已映射状态要保留原文案，避免再被通用取消/超时识别覆盖。
        return statusKey is
            "payment.card.squareCanceled" or
            "payment.card.squareCanceledBuyer" or
            "payment.card.squareCanceledSeller" or
            "payment.card.squareTimedOut" or
            "payment.card.squareTerminalOffline" or
            "payment.card.squareTerminalNotPickedUp";
    }

    private static bool IsTimeoutMessage(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
            (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConfirmedCardCancellation(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
            message.Contains("cancel", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("could not be confirmed", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowOverlayIfTerminalError(PaymentTenderAttemptResult result)
    {
        var overlay = ClassifyCardPaymentError(result.StatusKey, result.StatusMessage, result.IsTerminalDecline);
        if (overlay is null)
            return;

        overlay.IsOpen = true;
        _vm.CardPaymentErrorOverlay = overlay;
        _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private static CardPaymentErrorOverlayViewModel? ClassifyCardPaymentError(
        string statusKey,
        string? statusMessage,
        bool isTerminalDecline)
    {
        var overlay = ClassifyCardPaymentErrorByStatusKey(statusKey);
        if (overlay is not null)
        {
            return overlay;
        }

        if (string.IsNullOrWhiteSpace(statusMessage))
        {
            return isTerminalDecline && IsCardDeclinedStatusKey(statusKey)
                ? CardPaymentErrorOverlayViewModel.CardDeclined(null)
                : null;
        }

        var message = statusMessage;

        if (message.Contains("unfinished card transaction", StringComparison.OrdinalIgnoreCase))
            return CardPaymentErrorOverlayViewModel.ActiveSessionRequiresRecovery();

        if (message.Contains("connection failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection was closed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("could not be sent", StringComparison.OrdinalIgnoreCase))
            return CardPaymentErrorOverlayViewModel.ConnectionFailed();

        if (message.Contains("communication failed", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("Square", StringComparison.OrdinalIgnoreCase))
                return CardPaymentErrorOverlayViewModel.SquareCommunicationFailed();
            return CardPaymentErrorOverlayViewModel.CloudCommunicationFailed();
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return CardPaymentErrorOverlayViewModel.Timeout();

        if (message.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return CardPaymentErrorOverlayViewModel.ConnectionFailed();

        // 普通银行拒付不是系统故障，但需要给收银员一个醒目的原因弹窗。
        if (isTerminalDecline && IsCardDeclinedStatusKey(statusKey) && !IsConfirmedCardCancellation(message))
            return CardPaymentErrorOverlayViewModel.CardDeclined(message);

        return null;
    }

    private static CardPaymentErrorOverlayViewModel? ClassifyCardPaymentErrorByStatusKey(string statusKey)
    {
        return statusKey switch
        {
            "linkly.local.connectionFailed" or
            "payment.card.linklyUnavailable" => CardPaymentErrorOverlayViewModel.ConnectionFailed(),

            "linkly.cloud.communicationFailed" or
            "linkly.backend.communicationFailed" => CardPaymentErrorOverlayViewModel.CloudCommunicationFailed(),

            "linkly.backend.activeSessionRequiresRecovery" or
            "linkly.backend.resultUnknown" or
            "linkly.backend.cancelledUnknown" or
            "linkly.cloud.resultUnknown" => CardPaymentErrorOverlayViewModel.ActiveSessionRequiresRecovery(),

            "payment.card.squareCommunicationFailed" => CardPaymentErrorOverlayViewModel.SquareCommunicationFailed(),
            "payment.card.squareTimedOut" => CardPaymentErrorOverlayViewModel.Timeout(),
            "payment.card.squareTerminalOffline" or
            "payment.card.squareTerminalNotPickedUp" => CardPaymentErrorOverlayViewModel.SquareCommunicationFailed(),

            "linkly.local.timeout" or
            "linkly.cloud.timeout" or
            "linkly.backend.timeout" => CardPaymentErrorOverlayViewModel.Timeout(),

            _ => null
        };
    }

    // ── Dispose support ──

    public void Dispose()
    {
        CompletePendingLinklyFallbackPrompt(confirmed: false);
        _activeCardPaymentCts?.Dispose();
        _activeCardPaymentCts = null;
        _manuallyCancelledCardPaymentCts?.Dispose();
        _manuallyCancelledCardPaymentCts = null;
    }
}
