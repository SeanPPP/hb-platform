using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using BlazorApp.Shared.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed class CardRecoveryCenterViewModel : ObservableObject, IDisposable
{
    private const string ScreenName = "card-recovery-center";
    private static readonly JsonSerializerOptions DraftJsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly ICardPaymentRecoveryService _recoveryService;
    private readonly PosCartService _cart;
    private readonly PosSessionState _session;
    private readonly IOperationAuthorizationService _authorizationService;
    private readonly ILocalizationService? _localization;
    private readonly Action? _back;
    private readonly Action<int>? _openCountChanged;
    private readonly Func<CardRecoveryAttemptKey, CardPaymentRecoveryResult, Task>? _recoveryResultHandledAsync;
    private Task? _initialLoadTask;
    private CardRecoveryQueueItem? _selectedAttempt;
    private bool _isBusy;
    private string _resolutionReason = string.Empty;
    private string _resolutionEvidence = string.Empty;
    private string _resolutionReference = string.Empty;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<CardRecoveryQueueItem> _sourceAttempts = [];
    private IReadOnlyList<PosCartLineSnapshot> _selectedProductLines = [];

    public CardRecoveryCenterViewModel(
        ICardPaymentRecoveryService recoveryService,
        PosCartService cart,
        PosSessionState session,
        IOperationAuthorizationService authorizationService,
        ILocalizationService? localization = null,
        Action? back = null,
        Action<int>? openCountChanged = null,
        Func<CardRecoveryAttemptKey, CardPaymentRecoveryResult, Task>? recoveryResultHandledAsync = null)
    {
        ArgumentNullException.ThrowIfNull(recoveryService);
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(authorizationService);

        _recoveryService = recoveryService;
        _cart = cart;
        _session = session;
        _authorizationService = authorizationService;
        _localization = localization;
        _back = back;
        _openCountChanged = openCountChanged;
        _recoveryResultHandledAsync = recoveryResultHandledAsync;
        BackCommand = new RelayCommand(
            () => _back?.Invoke(),
            () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(
            () => RefreshWithAuthorizationAsync("refresh"),
            () => !IsBusy);
        RecoverCommand = new AsyncRelayCommand(RecoverSelectedAsync, CanOperateOnSelection);
        ConfirmPaidCommand = new AsyncRelayCommand(
            () => ResolveSelectedAsync(
                CardRecoverySupervisorDecision.ConfirmProcessed,
                "resolve/confirm-paid"),
            CanOperateOnSelection);
        ConfirmNotPaidCommand = new AsyncRelayCommand(
            () => ResolveSelectedAsync(
                CardRecoverySupervisorDecision.ConfirmNotProcessed,
                "resolve/confirm-not-paid"),
            CanOperateOnSelection);
        ContinueWaitingCommand = new AsyncRelayCommand(
            () => ResolveSelectedAsync(
                CardRecoverySupervisorDecision.ContinueWaiting,
                "resolve/continue-waiting"),
            CanOperateOnSelection);
        if (_localization is not null)
        {
            _localization.CultureChanged += OnCultureChanged;
        }

        SetStatusResource("cardRecovery.center.status.ready", "Review an open card transaction.");
    }

    public ObservableCollection<CardRecoveryQueueItem> OpenAttempts { get; } = [];

    public CardRecoveryQueueItem? SelectedAttempt
    {
        get => _selectedAttempt;
        set
        {
            if (SetProperty(ref _selectedAttempt, value))
            {
                UpdateSelectedProductLines(value?.OrderDraftJson);
                NotifySelectedAttemptProperties();
                NotifySelectionCommands();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifySelectionCommands();
            }
        }
    }

    public string ResolutionReason
    {
        get => _resolutionReason;
        set => SetProperty(ref _resolutionReason, value);
    }

    public string ResolutionEvidence
    {
        get => _resolutionEvidence;
        set => SetProperty(ref _resolutionEvidence, value);
    }

    public string ResolutionReference
    {
        get => _resolutionReference;
        set => SetProperty(ref _resolutionReference, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string OpenCountText => string.Format(
        GetCulture(),
        T("cardRecovery.center.openCount", "{0} card transactions need attention"),
        OpenAttempts.Count);

    public bool HasSelection => SelectedAttempt is not null;
    public bool HasNoSelection => !HasSelection;
    public bool HasOpenAttempts => OpenAttempts.Count > 0;
    public bool HasNoOpenAttempts => !HasOpenAttempts;
    public bool HasProductSnapshot => SelectedProductLines.Count > 0;
    public bool HasNoProductSnapshot => !HasProductSnapshot;
    public IReadOnlyList<PosCartLineSnapshot> SelectedProductLines => _selectedProductLines;
    public string SelectedTypeText => SelectedSourceAttempt is { } selected
        ? FormatOperationKind(selected.OperationKind)
        : NoneText;
    public string SelectedChannelText => SelectedSourceAttempt is { } selected
        ? FormatProcessor(selected.Processor)
        : NoneText;
    public string SelectedAmountText => SelectedAttempt is null
        ? NoneText
        : SelectedAttempt.Amount.ToString("C2", GetCulture());
    public string SelectedCashierText => ValueOrNone(SelectedAttempt?.CashierId);
    public string SelectedTimeText => SelectedAttempt is null
        ? NoneText
        : SelectedAttempt.UpdatedAt.ToString("g", GetCulture());
    public string SelectedSessionText => ValueOrNone(
        Normalize(SelectedAttempt?.SessionId) ?? Normalize(SelectedAttempt?.CheckoutId));
    public string SelectedTxnText => ValueOrNone(
        Normalize(SelectedAttempt?.TxnRef) ?? Normalize(SelectedAttempt?.PaymentId));
    public string SelectedResponseCodeText => ValueOrNone(SelectedAttempt?.ResponseCode);
    public string SelectedResponseText => ValueOrNone(SelectedAttempt?.ResponseText);
    public string SelectedStatusText => SelectedSourceAttempt is { } selected
        ? FormatAttemptStatus(selected.Status)
        : NoneText;
    public string SelectedAttemptText => SelectedAttempt?.AttemptGuid.ToString("D") ?? NoneText;
    public string SelectedEnvironmentText => ValueOrNone(SelectedAttempt?.Environment);
    public string SelectedReferenceText => ValueOrNone(
        Normalize(SelectedAttempt?.PaymentReference) ?? Normalize(SelectedAttempt?.PaymentId));

    public IRelayCommand BackCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RecoverCommand { get; }
    public IAsyncRelayCommand ConfirmPaidCommand { get; }
    public IAsyncRelayCommand ConfirmNotPaidCommand { get; }
    public IAsyncRelayCommand ContinueWaitingCommand { get; }

    public Task LoadAsync() =>
        _initialLoadTask ??= RefreshWithAuthorizationAsync("view");

    private async Task RefreshWithAuthorizationAsync(string action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            using var authorization = await _authorizationService.AuthorizeAsync(
                Permissions.PosTerminal.Payment.View,
                ScreenName,
                action,
                _session);
            if (authorization is null)
            {
                SetStatusResource(
                    "cardRecovery.center.status.authorizationRequired",
                    "Authorization is required.");
                return;
            }

            using var activation = authorization.Activate();
            await RefreshListCoreAsync();
        }
        catch (Exception ex)
        {
            SetStatusResource(
                "cardRecovery.center.status.refreshFailed",
                "Could not refresh card transactions. {0}",
                ex.Message);
            ConsoleLog.WriteError(
                "CardRecoveryCenter",
                $"refresh failed action={action} error={ex.GetType().Name}",
                exception: ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecoverSelectedAsync()
    {
        var selected = SelectedAttempt;
        if (selected is null)
        {
            return;
        }

        var selectedKey = selected.Key;
        IsBusy = true;
        try
        {
            using var authorization = await _authorizationService.AuthorizeAsync(
                Permissions.PosTerminal.Payment.View,
                ScreenName,
                "recover",
                _session);
            if (authorization is null)
            {
                SetStatusResource(
                    "cardRecovery.center.status.authorizationRequired",
                    "Authorization is required.");
                return;
            }

            using var activation = authorization.Activate();
            if (SelectedAttempt?.Key != selectedKey)
            {
                SetStatusResource(
                    "cardRecovery.center.status.selectionChanged",
                    "The selected transaction changed. Select it again before continuing.");
                return;
            }

            var result = await _recoveryService.RecoverAsync(
                selectedKey,
                _cart,
                _session);
            var actionMessage = string.IsNullOrWhiteSpace(result.Message)
                ? T("cardRecovery.center.status.recoverNoResult", "The selected transaction is no longer open.")
                : result.Message;
            await TryRefreshListAfterOperationAsync(actionMessage, "recover");
            if (_recoveryResultHandledAsync is not null)
            {
                await _recoveryResultHandledAsync(selectedKey, result);
            }
        }
        catch (Exception ex)
        {
            SetStatusResource(
                "cardRecovery.center.status.recoverFailed",
                "Could not check the selected transaction. {0}",
                ex.Message);
            ConsoleLog.WriteError(
                "CardRecoveryCenter",
                $"targeted recovery failed processor={selected.Processor} attempt={selected.AttemptGuid:D} error={ex.GetType().Name}",
                exception: ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanOperateOnSelection() => !IsBusy && SelectedAttempt is not null;

    private async Task ResolveSelectedAsync(
        CardRecoverySupervisorDecision decision,
        string action)
    {
        var selected = SelectedAttempt;
        if (selected is null)
        {
            return;
        }

        var selectedKey = selected.Key;
        var reason = Normalize(ResolutionReason) ?? string.Empty;
        var evidence = Normalize(ResolutionEvidence);
        var reference = Normalize(ResolutionReference);
        IsBusy = true;
        try
        {
            using var authorization = await _authorizationService.AuthorizeAsync(
                Permissions.PosTerminal.Audit.View,
                ScreenName,
                action,
                _session);
            if (authorization is null)
            {
                SetStatusResource(
                    "cardRecovery.center.status.authorizationRequired",
                    "Authorization is required.");
                return;
            }

            using var activation = authorization.Activate();
            // 主管扫码期间列表选择可能变化，旧授权不得落到另一笔金融交易。
            if (SelectedAttempt?.Key != selectedKey)
            {
                SetStatusResource(
                    "cardRecovery.center.status.selectionChanged",
                    "The selected transaction changed. Select it again before continuing.");
                return;
            }

            var result = await _recoveryService.ResolveAsync(
                selectedKey,
                decision,
                reason,
                evidence,
                reference,
                _cart,
                _session);
            var actionMessage = string.IsNullOrWhiteSpace(result.Message)
                ? T("cardRecovery.center.status.resolveNoResult", "The resolution returned no message.")
                : result.Message;
            await TryRefreshListAfterOperationAsync(actionMessage, action);
            if (result.RecoveryResult is not null && _recoveryResultHandledAsync is not null)
            {
                await _recoveryResultHandledAsync(selectedKey, result.RecoveryResult);
            }
        }
        catch (Exception ex)
        {
            SetStatusResource(
                "cardRecovery.center.status.resolveFailed",
                "Could not save the supervisor decision. {0}",
                ex.Message);
            ConsoleLog.WriteError(
                "CardRecoveryCenter",
                $"targeted resolution failed processor={selected.Processor} attempt={selected.AttemptGuid:D} decision={decision} error={ex.GetType().Name}",
                exception: ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TryRefreshListAfterOperationAsync(string actionMessage, string action)
    {
        try
        {
            await RefreshListCoreAsync(actionMessage);
        }
        catch (Exception ex)
        {
            // 金融操作结果已经取得，队列刷新失败不得阻断结果回调或被误报为操作失败。
            SetStatusResource(
                "cardRecovery.center.status.refreshFailed",
                "Could not refresh card transactions. {0}",
                ex.Message);
            try
            {
                ConsoleLog.WriteError(
                    "CardRecoveryCenter",
                    $"refresh failed action={action} error={ex.GetType().Name}",
                    exception: ex);
            }
            catch (Exception)
            {
                // 日志基础设施失败不能再次阻断已经取得的金融结果回调。
            }
        }
    }

    private void NotifySelectionCommands()
    {
        BackCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        RecoverCommand.NotifyCanExecuteChanged();
        ConfirmPaidCommand.NotifyCanExecuteChanged();
        ConfirmNotPaidCommand.NotifyCanExecuteChanged();
        ContinueWaitingCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSelectedProductLines(string? orderDraftJson)
    {
        IReadOnlyList<PosCartLineSnapshot> lines = [];
        if (!string.IsNullOrWhiteSpace(orderDraftJson))
        {
            try
            {
                lines = JsonSerializer.Deserialize<CardPaymentOrderDraft>(
                            orderDraftJson,
                            DraftJsonOptions)
                        ?.CartSnapshot.Lines ?? [];
            }
            catch (JsonException)
            {
                // 历史草稿损坏时仍展示交易证据，商品区明确显示快照不可用。
            }
            catch (NotSupportedException)
            {
                // 不支持的旧格式不得阻断异常中心的其他定点操作。
            }
        }

        _selectedProductLines = lines;
        OnPropertyChanged(nameof(SelectedProductLines));
        OnPropertyChanged(nameof(HasProductSnapshot));
        OnPropertyChanged(nameof(HasNoProductSnapshot));
    }

    private void NotifySelectedAttemptProperties()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(SelectedTypeText));
        OnPropertyChanged(nameof(SelectedChannelText));
        OnPropertyChanged(nameof(SelectedAmountText));
        OnPropertyChanged(nameof(SelectedCashierText));
        OnPropertyChanged(nameof(SelectedTimeText));
        OnPropertyChanged(nameof(SelectedSessionText));
        OnPropertyChanged(nameof(SelectedTxnText));
        OnPropertyChanged(nameof(SelectedResponseCodeText));
        OnPropertyChanged(nameof(SelectedResponseText));
        OnPropertyChanged(nameof(SelectedStatusText));
        OnPropertyChanged(nameof(SelectedAttemptText));
        OnPropertyChanged(nameof(SelectedEnvironmentText));
        OnPropertyChanged(nameof(SelectedReferenceText));
    }

    private async Task RefreshListCoreAsync(string? actionMessage = null)
    {
        var selectedKey = SelectedAttempt?.Key;
        var items = await _recoveryService.ListOpenAsync(_session);
        _sourceAttempts = items;
        RefreshLocalizedOpenAttempts(selectedKey);
        OnPropertyChanged(nameof(OpenCountText));
        OnPropertyChanged(nameof(HasOpenAttempts));
        OnPropertyChanged(nameof(HasNoOpenAttempts));
        _openCountChanged?.Invoke(OpenAttempts.Count);
        if (actionMessage is not null)
        {
            SetLiteralStatus(actionMessage);
        }
        else if (OpenAttempts.Count == 0)
        {
            SetStatusResource(
                "cardRecovery.center.status.empty",
                "No card transactions need attention.");
        }
        else
        {
            SetStatusResource(
                "cardRecovery.center.status.loaded",
                "Loaded {0} open card transactions.",
                OpenAttempts.Count);
        }
    }

    private void SetLiteralStatus(string value) => StatusMessage = value;

    private void SetStatusResource(string key, string fallback, params object[] args)
    {
        var format = T(key, fallback);
        StatusMessage = args.Length == 0
            ? format
            : string.Format(GetCulture(), format, args);
    }

    private string T(string key, string fallback)
    {
        var value = _localization?.T(key);
        return string.IsNullOrWhiteSpace(value) ||
               value == key ||
               (value.StartsWith("[[", StringComparison.Ordinal) &&
                value.EndsWith("]]", StringComparison.Ordinal))
            ? fallback
            : value;
    }

    private IFormatProvider GetCulture() =>
        _localization?.CurrentCulture ?? CultureInfo.CurrentCulture;

    private string NoneText => T("cardRecovery.center.value.none", "-");

    private string ValueOrNone(string? value) => Normalize(value) ?? NoneText;

    private string UnknownText => T("cardRecovery.center.value.unknown", "Unknown");

    private CardRecoveryQueueItem? SelectedSourceAttempt => SelectedAttempt is null
        ? null
        : _sourceAttempts.FirstOrDefault(item => item.Key == SelectedAttempt.Key) ?? SelectedAttempt;

    private void RefreshLocalizedOpenAttempts(CardRecoveryAttemptKey? selectedKey)
    {
        OpenAttempts.Clear();
        foreach (var item in _sourceAttempts)
        {
            OpenAttempts.Add(item with
            {
                OperationKind = FormatOperationKind(item.OperationKind),
                Status = FormatAttemptStatus(item.Status)
            });
        }

        SelectedAttempt = selectedKey is null
            ? OpenAttempts.FirstOrDefault()
            : OpenAttempts.FirstOrDefault(item => item.Key == selectedKey.Value) ??
              OpenAttempts.FirstOrDefault();
    }

    private string FormatOperationKind(string? operationKind) =>
        Normalize(operationKind)?.ToUpperInvariant() switch
        {
            "SALE" => T("cardRecovery.center.type.sale", "Card sale"),
            "REFUND" => T("cardRecovery.center.type.refund", "Card refund"),
            "ACTIVESESSION" => T("cardRecovery.center.type.activeSession", "Active terminal session"),
            _ => UnknownText
        };

    private string FormatProcessor(CardProcessorKind processor) => processor switch
    {
        CardProcessorKind.Linkly => T("cardRecovery.center.channel.linkly", "Linkly"),
        CardProcessorKind.Square => T("cardRecovery.center.channel.square", "Square"),
        _ => UnknownText
    };

    private string FormatAttemptStatus(string? status) =>
        Normalize(status)?.ToUpperInvariant() switch
        {
            "PENDING" => T("cardRecovery.center.transactionStatus.pending", "Pending"),
            "SESSIONSTARTED" => T("cardRecovery.center.transactionStatus.sessionStarted", "Session started"),
            "RECOVERING" => T("cardRecovery.center.transactionStatus.recovering", "Checking result"),
            "APPROVED" => T("cardRecovery.center.transactionStatus.approved", "Payment approved"),
            "REQUIRESREVIEW" => T("cardRecovery.center.transactionStatus.requiresReview", "Needs supervisor review"),
            "ORDERCOMPLETED" => T("cardRecovery.center.transactionStatus.orderCompleted", "Order completed"),
            "CHECKOUTCREATED" => T("cardRecovery.center.transactionStatus.checkoutCreated", "Checkout created"),
            "CHECKOUTCOMPLETED" => T("cardRecovery.center.transactionStatus.checkoutCompleted", "Checkout completed"),
            "PAYMENTVERIFIED" => T("cardRecovery.center.transactionStatus.paymentVerified", "Payment verified"),
            "UNKNOWN" => T("cardRecovery.center.transactionStatus.unknown", "Result unknown"),
            "DECLINED" => T("cardRecovery.center.transactionStatus.declined", "Declined"),
            "TIMEDOUT" => T("cardRecovery.center.transactionStatus.timedOut", "Timed out"),
            "CANCELLED" or "CANCELED" => T("cardRecovery.center.transactionStatus.cancelled", "Cancelled"),
            "FAILED" => T("cardRecovery.center.transactionStatus.failed", "Failed"),
            "ABANDONED" => T("cardRecovery.center.transactionStatus.abandoned", "Abandoned"),
            _ => UnknownText
        };

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        var selectedKey = SelectedAttempt?.Key;
        RefreshLocalizedOpenAttempts(selectedKey);
        OnPropertyChanged(nameof(OpenCountText));
        NotifySelectedAttemptProperties();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose()
    {
        if (_localization is not null)
        {
            _localization.CultureChanged -= OnCultureChanged;
        }
    }
}
