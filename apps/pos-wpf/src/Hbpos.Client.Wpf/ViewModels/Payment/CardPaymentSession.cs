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
    private readonly object _lifetimeSync = new();
    private CancellationTokenSource? _activeCardPaymentCts;
    private CancellationTokenSource? _manuallyCancelledCardPaymentCts;
    private CancellationTokenSource? _shutdownCancellationSource;
    private Task _shutdownCancellationTask = Task.CompletedTask;
    private bool _cardPaymentCancellationRequested;
    private bool _awaitingLateCardResultAfterManualCancel;
    private bool _discardLateCardResultAfterManualCancel;
    private bool _cardPaymentResultUnknownRequiresRecovery;
    private CardPaymentHandoffCandidate? _cardPaymentHandoffCandidate;
    private CardRecoveryAttemptKey? _recoveryAttemptKey;
    private Guid? _recoveryOrderGuid;
    private string? _unknownResultStatusKey;
    private string? _unknownResultStatusMessage;
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

    public CardRecoveryAttemptKey? RecoveryAttemptKey => _recoveryAttemptKey;

    public Guid? RecoveryOrderGuid => _recoveryOrderGuid;

    // ── State accessors (used by PaymentViewModel) ──

    public void SetResultUnknownRecoveryRequired(bool value)
    {
        _cardPaymentResultUnknownRequiresRecovery = value;
        if (!value)
        {
            _cardPaymentHandoffCandidate = null;
            _recoveryAttemptKey = null;
            _recoveryOrderGuid = null;
            _unknownResultStatusKey = null;
            _unknownResultStatusMessage = null;
        }
    }

    public CancellationTokenSource? ActiveCardPaymentCts => _activeCardPaymentCts;

    // ── Card payment entry: BEGIN ──

    public CancellationTokenSource BeginCardPayment()
    {
        _cardPaymentHandoffCandidate = null;
        _recoveryAttemptKey = null;
        _recoveryOrderGuid = null;
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
        CancellationTokenSource? active;
        Task shutdownCancellationTask;
        lock (_lifetimeSync)
        {
            if (!ReferenceEquals(_activeCardPaymentCts, cardPaymentCts))
            {
                return;
            }

            active = _activeCardPaymentCts;
            _activeCardPaymentCts = null;
            if (ReferenceEquals(_manuallyCancelledCardPaymentCts, cardPaymentCts))
            {
                _manuallyCancelledCardPaymentCts = null;
            }

            shutdownCancellationTask = ReferenceEquals(_shutdownCancellationSource, active)
                ? _shutdownCancellationTask
                : Task.CompletedTask;
            if (ReferenceEquals(_shutdownCancellationSource, active))
            {
                _shutdownCancellationSource = null;
            }
        }

        _cardPaymentCancellationRequested = false;
        _vm.IsCardPaymentInProgress = false;
        _vm.IsPaymentInteractionLocked = _vm.IsShuttingDown || HasUnknownResult;
        DisposeAfterCancellation(active, shutdownCancellationTask);
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

    public Task CancelForShutdownAsync()
    {
        lock (_lifetimeSync)
        {
            if (_activeCardPaymentCts is null || _activeCardPaymentCts.IsCancellationRequested)
            {
                return _shutdownCancellationTask;
            }

            var cancellation = _activeCardPaymentCts;
            _shutdownCancellationSource = cancellation;
            // 退出取消不是人工撤销：不清空 session、不释放交互锁，让工作流把已越过提交边界的结果持久化为待恢复。
            _shutdownCancellationTask = Task.Run(() =>
            {
                try
                {
                    cancellation.Cancel();
                }
                catch (Exception ex)
                {
                    ConsoleLog.WriteError(
                        "Shutdown",
                        $"card payment shutdown cancellation failed error={ex.GetType().Name} message={ex.Message}",
                        exception: ex);
                }
            });
            return _shutdownCancellationTask;
        }
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

    public async Task<bool> TryHandleCancelledResultAsync(
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

        if (result.CardResult?.RequiresRecovery == true)
        {
            return await TryHandleFailedResultAsync(result);
        }

        if (cardPaymentWasManuallyCancelled && result.CardResult?.Outcome == CardPaymentTerminalOutcome.Cancelled)
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

    public async Task<bool> TryHandleFailedResultAsync(PaymentTenderAttemptResult result)
    {
        if (result.CardResult?.RequiresRecovery == true)
        {
            _cardPaymentResultUnknownRequiresRecovery = true;
            _cardPaymentHandoffCandidate = null;
            _recoveryAttemptKey = result.RecoveryAttemptKey is { AttemptGuid: var attemptGuid } key &&
                attemptGuid != Guid.Empty
                ? key
                : null;
            _recoveryOrderGuid = result.RecoveryOrderGuid is { } orderGuid && orderGuid != Guid.Empty
                ? orderGuid
                : null;
            _unknownResultStatusKey = result.StatusKey;
            _unknownResultStatusMessage = result.StatusMessage;
            _vm.IsPaymentInteractionLocked = true;
            RestoreUnknownResultStatus();
            ResetManualCancellationState();
            ShowOverlayIfTerminalError(result);
            if (_recoveryAttemptKey is not null && _recoveryOrderGuid is not null)
            {
                await PrepareCardPaymentHandoffAsync();
            }
            _vm.NotifyPaymentCommandStates();
            return true;
        }

        ShowOverlayIfTerminalError(result);

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
        var disposition = result.CardResult;
        if (disposition?.PreserveStatus == true)
        {
            return false;
        }

        if (disposition?.Outcome == CardPaymentTerminalOutcome.Cancelled)
        {
            _vm.SetStatus("payment.status.cardCancelled");
            return true;
        }

        if (disposition?.Outcome == CardPaymentTerminalOutcome.TimedOut)
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
        if (_vm.CardPaymentErrorOverlay is not { HasPrimaryAction: true } overlay)
        {
            return false;
        }

        return overlay.PrimaryActionKind switch
        {
            CardPaymentErrorOverlayPrimaryActionKind.ConfirmFallback => overlay.IsOpen,
            CardPaymentErrorOverlayPrimaryActionKind.RecoverPrevious =>
                _cardPaymentResultUnknownRequiresRecovery &&
                _cardPaymentHandoffCandidate is not null &&
                !_vm.IsShuttingDown &&
                _vm.NavigationActions.HandoffCardPaymentAsync is not null,
            _ => false
        };
    }

    private async Task PrepareCardPaymentHandoffAsync()
    {
        var prepare = _vm.NavigationActions.PrepareCardPaymentHandoffAsync;
        if (prepare is null)
        {
            return;
        }

        try
        {
            _cardPaymentHandoffCandidate = await prepare(_vm.CreateCardPaymentHandoffRequest());
        }
        catch (Exception ex)
        {
            // 查询失败必须保持当前订单锁定；不能把持久化异常升级成清单或导航动作。
            ConsoleLog.WriteError(
                "CardPayment",
                $"card payment handoff qualification failed error={ex.GetType().Name}",
                exception: ex);
            _cardPaymentHandoffCandidate = null;
            RestoreUnknownResultStatus();
        }

        if (_cardPaymentHandoffCandidate is not null &&
            _vm.CardPaymentErrorOverlay?.PrimaryActionKind ==
            CardPaymentErrorOverlayPrimaryActionKind.RecoverPrevious)
        {
            _vm.CardPaymentErrorOverlay.HasPrimaryAction = true;
            _vm.CardPaymentErrorOverlay.PrimaryButtonTextKey =
                "payment.card.error.overlay.activeSession.handoff";
        }

        _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    public async Task ExecuteErrorPrimaryActionAsync()
    {
        if (!CanExecuteErrorPrimaryAction())
        {
            return;
        }

        if (_vm.CardPaymentErrorOverlay?.PrimaryActionKind == CardPaymentErrorOverlayPrimaryActionKind.ConfirmFallback)
        {
            CompletePendingLinklyFallbackPrompt(confirmed: true);
            _vm.CardPaymentErrorOverlay.IsOpen = false;
            ReleaseFallbackPromptLockIfIdle();
            _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
            return;
        }

        var candidate = _cardPaymentHandoffCandidate;
        var handoff = _vm.NavigationActions.HandoffCardPaymentAsync;
        if (candidate is null || handoff is null)
        {
            return;
        }

        try
        {
            _vm.SetStatus("payment.card.error.overlay.activeSession.handingOff");
            if (!await handoff(candidate, _vm.CreateCardPaymentHandoffRequest()))
            {
                RestoreUnknownResultStatus();
                return;
            }

            _cardPaymentResultUnknownRequiresRecovery = false;
            _cardPaymentHandoffCandidate = null;
            _recoveryAttemptKey = null;
            _recoveryOrderGuid = null;
            _unknownResultStatusKey = null;
            _unknownResultStatusMessage = null;
            _vm.CompleteCardPaymentHandoff();
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteError(
                "CardPayment",
                $"card payment handoff failed error={ex.GetType().Name}",
                exception: ex);
            _cardPaymentResultUnknownRequiresRecovery = true;
            _vm.IsPaymentInteractionLocked = true;
            RestoreUnknownResultStatus();
            _vm.NotifyPaymentCommandStates();
        }
        finally
        {
            _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
        }
    }

    private void RestoreUnknownResultStatus()
    {
        _vm.SetStatus(
            _unknownResultStatusKey ?? "payment.card.resultUnknown",
            _unknownResultStatusMessage);
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

    private void ShowOverlayIfTerminalError(PaymentTenderAttemptResult result)
    {
        var overlay = result.CardResult?.ErrorKind switch
        {
            CardPaymentErrorKind.ConnectionFailed => CardPaymentErrorOverlayViewModel.ConnectionFailed(),
            CardPaymentErrorKind.CloudCommunicationFailed => CardPaymentErrorOverlayViewModel.CloudCommunicationFailed(),
            CardPaymentErrorKind.ActiveSessionRequiresRecovery => CreateUnqualifiedRecoveryOverlay(),
            CardPaymentErrorKind.SquareCommunicationFailed => CardPaymentErrorOverlayViewModel.SquareCommunicationFailed(),
            CardPaymentErrorKind.Timeout => CardPaymentErrorOverlayViewModel.Timeout(),
            CardPaymentErrorKind.CardDeclined => CardPaymentErrorOverlayViewModel.CardDeclined(result.StatusMessage),
            _ => null
        };
        if (overlay is null)
            return;

        overlay.IsOpen = true;
        _vm.CardPaymentErrorOverlay = overlay;
        _vm.CardPaymentErrorPrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private static CardPaymentErrorOverlayViewModel CreateUnqualifiedRecoveryOverlay()
    {
        var overlay = CardPaymentErrorOverlayViewModel.ActiveSessionRequiresRecovery();
        // 持久化 attempt 与完整草稿尚未核验前，只允许关闭提示，不暴露失效的恢复按钮。
        overlay.HasPrimaryAction = false;
        return overlay;
    }

    // ── Dispose support ──

    public void Dispose()
    {
        CompletePendingLinklyFallbackPrompt(confirmed: false);
        CancellationTokenSource? orphanedManuallyCancelled;
        CancellationTokenSource? orphanedShutdownCancellation;
        Task shutdownCancellationTask;
        lock (_lifetimeSync)
        {
            shutdownCancellationTask = _shutdownCancellationTask;
            // active CTS 仍可能被 AddTenderAsync 使用；只有真实工作流 finally 的 EndCardPayment 可以释放它。
            orphanedManuallyCancelled = ReferenceEquals(
                _manuallyCancelledCardPaymentCts,
                _activeCardPaymentCts)
                ? null
                : _manuallyCancelledCardPaymentCts;
            if (orphanedManuallyCancelled is not null)
            {
                _manuallyCancelledCardPaymentCts = null;
            }

            orphanedShutdownCancellation = ReferenceEquals(
                _shutdownCancellationSource,
                _activeCardPaymentCts)
                ? null
                : _shutdownCancellationSource;
            if (orphanedShutdownCancellation is not null)
            {
                _shutdownCancellationSource = null;
            }
        }

        DisposeAfterCancellation(orphanedManuallyCancelled, shutdownCancellationTask);
        if (!ReferenceEquals(orphanedShutdownCancellation, orphanedManuallyCancelled))
        {
            DisposeAfterCancellation(orphanedShutdownCancellation, shutdownCancellationTask);
        }
    }

    private static void DisposeAfterCancellation(
        CancellationTokenSource? cancellation,
        Task cancellationTask)
    {
        if (cancellation is null)
        {
            return;
        }

        if (cancellationTask.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        _ = cancellationTask.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
