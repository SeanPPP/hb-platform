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

/// <summary>
/// 恢复中心列表的纯展示行。原始队列快照仍由 <see cref="CardRecoveryCenterViewModel.OpenAttempts" />
/// 保留用于定点操作，界面只绑定这里的本地化文本，避免把 provider/数据库枚举直接显示给收银员。
/// </summary>
public sealed class CardRecoveryQueueRowViewModel
{
    public CardRecoveryQueueRowViewModel(
        CardRecoveryQueueItem source,
        string operationTypeText,
        string channelText,
        string updatedAtText,
        string amountText,
        string statusText)
    {
        Source = source;
        OperationTypeText = operationTypeText;
        ChannelText = channelText;
        UpdatedAtText = updatedAtText;
        AmountText = amountText;
        StatusText = statusText;
    }

    public CardRecoveryQueueItem Source { get; }

    public CardRecoveryAttemptKey Key => Source.Key;

    public string OperationTypeText { get; }

    public string ChannelText { get; }

    public string UpdatedAtText { get; }

    public string AmountText { get; }

    public string StatusText { get; }
}

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
    private readonly Func<CardPaymentRecoveryResult, Task>? _recoveryResultHandledAsync;
    private Task? _initialLoadTask;
    private CardRecoveryQueueItem? _selectedAttempt;
    private CardRecoveryQueueRowViewModel? _selectedRow;
    private bool _isBusy;
    private string _resolutionReason = string.Empty;
    private string _resolutionEvidence = string.Empty;
    private string _resolutionReference = string.Empty;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<PosCartLineSnapshot> _selectedProductLines = [];
    private bool _isRebuildingRows;

    public CardRecoveryCenterViewModel(
        ICardPaymentRecoveryService recoveryService,
        PosCartService cart,
        PosSessionState session,
        IOperationAuthorizationService authorizationService,
        ILocalizationService? localization = null,
        Action? back = null,
        Action<int>? openCountChanged = null,
        Func<CardPaymentRecoveryResult, Task>? recoveryResultHandledAsync = null)
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
            CanResolveSelection);
        ConfirmNotPaidCommand = new AsyncRelayCommand(
            () => ResolveSelectedAsync(
                CardRecoverySupervisorDecision.ConfirmNotProcessed,
                "resolve/confirm-not-paid"),
            CanResolveSelection);
        ContinueWaitingCommand = new AsyncRelayCommand(
            () => ResolveSelectedAsync(
                CardRecoverySupervisorDecision.ContinueWaiting,
                "resolve/continue-waiting"),
            CanResolveSelection);
        if (_localization is not null)
        {
            _localization.CultureChanged += OnCultureChanged;
        }

        SetStatusResource("cardRecovery.center.status.ready", "Review an open card transaction.");
    }

    public ObservableCollection<CardRecoveryQueueItem> OpenAttempts { get; } = [];

    public ObservableCollection<CardRecoveryQueueRowViewModel> OpenAttemptRows { get; } = [];

    public CardRecoveryQueueItem? SelectedAttempt
    {
        get => _selectedAttempt;
        set
        {
            if (SetProperty(ref _selectedAttempt, value))
            {
                var matchingRow = value is null
                    ? null
                    : OpenAttemptRows.FirstOrDefault(row => row.Key == value.Key);
                if (!ReferenceEquals(_selectedRow, matchingRow))
                {
                    _selectedRow = matchingRow;
                    OnPropertyChanged(nameof(SelectedRow));
                }
                UpdateSelectedProductLines(value?.OrderDraftJson);
                NotifySelectedAttemptProperties();
                NotifySelectionCommands();
            }
        }
    }

    public CardRecoveryQueueRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (value is null && _isRebuildingRows)
            {
                // ItemsSource 重建时 WPF TwoWay 绑定会短暂回写 null；保留 provider+AttemptGuid 选择，待重建完成后同步。
                return;
            }

            if (SetProperty(ref _selectedRow, value))
            {
                // ListBox 选择的是展示行，定点恢复/结案仍使用原始快照的 Key。
                if (!ReferenceEquals(_selectedAttempt, value?.Source))
                {
                    SelectedAttempt = value?.Source;
                }
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
    public bool IsSquareRefundProcessing =>
        SelectedAttempt is { } attempt && HasSquareRefundPaymentEvidence(attempt);
    public bool CanShowSupervisorResolution => HasSelection && !IsSquareRefundProcessing;
    public string SquareRefundProcessingMessage => T(
        "cardRecovery.center.squareRefund.processing",
        "Square refund is already processing. Use Recover to check the latest status. Do not submit another refund.");
    private bool IsRefundSelection => string.Equals(
        SelectedAttempt?.OperationKind,
        "Refund",
        StringComparison.OrdinalIgnoreCase);
    private bool IsPaymentSelection =>
        string.Equals(SelectedAttempt?.OperationKind, "Sale", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(SelectedAttempt?.OperationKind, "ActiveSession", StringComparison.OrdinalIgnoreCase);
    private bool IsSquareRefundSelection =>
        IsRefundSelection && SelectedAttempt?.Processor == CardProcessorKind.Square;
    public string ResolutionSectionTitleText => IsRefundSelection
        ? T("cardRecovery.refund.section.title", "Supervisor refund reconciliation")
        : IsPaymentSelection
            ? T("cardRecovery.payment.section.title", "Supervisor payment reconciliation")
            : T("cardRecovery.center.resolution.title", "Supervisor resolution");
    public string ResolutionInstructionsText => IsSquareRefundSelection
        ? T(
            "cardRecovery.refund.section.squareInstructions",
            "Check the Square refund record before choosing an outcome. Confirm refunded requires a real Square refund reference; confirm not refunded requires bank evidence; continue waiting requires a supervisor note.")
        : IsRefundSelection
            ? T(
                "cardRecovery.refund.section.instructions",
                "Check the bank or terminal record before choosing one outcome. The refund remains locked until a supervisor decision is saved.")
            : IsPaymentSelection
                ? T(
                    "cardRecovery.payment.section.instructions",
                    "Check the bank result before unlocking this payment. Confirming paid requires a reference or evidence; confirming not paid requires evidence. A supervisor note is optional.")
                : T(
                    "cardRecovery.center.resolution.instructions",
                    "Confirm the bank or terminal evidence for this selected transaction. Each manual decision requires one-time supervisor authorization.");
    public string ResolutionReasonLabelText => IsSquareRefundSelection
        ? T(
            "cardRecovery.refund.field.squareNote",
            "Supervisor note (required when continuing to wait)")
        : IsRefundSelection
            ? T(
                "cardRecovery.refund.field.note",
                "Supervisor note (required when waiting; reference or note required when refunded)")
            : IsPaymentSelection
                ? T("cardRecovery.payment.field.note", "Supervisor note (optional)")
                : T("cardRecovery.center.input.reason", "Supervisor reason or note");
    public string ResolutionEvidenceLabelText => IsRefundSelection
        ? T(
            "cardRecovery.refund.field.evidence",
            "Bank evidence (required when no refund was processed)")
        : IsPaymentSelection
            ? T(
                "cardRecovery.payment.field.evidence",
                "Bank evidence (required when confirming not paid)")
            : T("cardRecovery.center.input.evidence", "Bank or terminal evidence");
    public string ResolutionReferenceLabelText => IsSquareRefundSelection
        ? T(
            "cardRecovery.refund.field.squareRefundReference",
            "Square refund reference (required when confirming refunded)")
        : IsRefundSelection
            ? T("cardRecovery.refund.field.refundReference", "Refund reference (when available)")
            : IsPaymentSelection
                ? T("cardRecovery.payment.field.paymentReference", "Payment reference (when available)")
                : T("cardRecovery.center.input.reference", "Payment or settlement reference");
    public string SelectedTypeText => MapOperationType(SelectedAttempt?.OperationKind);
    public string SelectedChannelText => MapChannel(SelectedAttempt?.Processor);
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
    public string SelectedStatusText => MapStatus(SelectedAttempt?.Status);
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
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
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
            if (SelectedAttempt?.Key != selected.Key)
            {
                SetStatusResource(
                    "cardRecovery.center.status.selectionChanged",
                    "The selected transaction changed. Select it again before continuing.");
                return;
            }

            var result = await _recoveryService.RecoverAsync(
                selected.Key,
                _cart,
                _session);
            var actionMessage = string.IsNullOrWhiteSpace(result.Message)
                ? T("cardRecovery.center.status.recoverNoResult", "The selected transaction is no longer open.")
                : result.Message;
            await RunPostCommitActionAsync(
                () => RefreshListCoreAsync(actionMessage),
                actionMessage,
                $"targeted recovery refresh processor={selected.Processor} attempt={selected.AttemptGuid:D}");
            if (_recoveryResultHandledAsync is not null)
            {
                await RunPostCommitActionAsync(
                    () => _recoveryResultHandledAsync(result),
                    actionMessage,
                    $"targeted recovery callback processor={selected.Processor} attempt={selected.AttemptGuid:D}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
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

    private bool CanResolveSelection() =>
        !IsBusy &&
        SelectedAttempt is { } attempt &&
        IsSupervisorResolutionAllowed(attempt);

    private static bool IsSupervisorResolutionAllowed(CardRecoveryQueueItem attempt)
    {
        if (string.Equals(
                attempt.Status,
                CardRecoveryPhases.FinalizePending,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var isRefund = string.Equals(
            attempt.OperationKind,
            "Refund",
            StringComparison.OrdinalIgnoreCase);
        if (isRefund && HasSquareRefundPaymentEvidence(attempt))
        {
            return false;
        }

        if (attempt.Processor == CardProcessorKind.Linkly)
        {
            if (isRefund)
            {
                return StatusIs(
                    attempt.Status,
                    nameof(LocalCardPaymentAttemptStatus.Recovering),
                    nameof(LocalCardPaymentAttemptStatus.RequiresReview),
                    nameof(LocalCardPaymentAttemptStatus.SessionStarted));
            }

            return StatusIs(
                attempt.Status,
                nameof(LocalCardPaymentAttemptStatus.Pending),
                nameof(LocalCardPaymentAttemptStatus.SessionStarted),
                nameof(LocalCardPaymentAttemptStatus.Recovering),
                nameof(LocalCardPaymentAttemptStatus.RequiresReview));
        }

        if (attempt.Processor != CardProcessorKind.Square)
        {
            return false;
        }

        if (isRefund)
        {
            return StatusIs(
                attempt.Status,
                nameof(LocalSquarePaymentAttemptStatus.Recovering),
                nameof(LocalSquarePaymentAttemptStatus.Unknown),
                nameof(LocalSquarePaymentAttemptStatus.CheckoutCreated));
        }

        return string.Equals(attempt.OperationKind, "Sale", StringComparison.OrdinalIgnoreCase) &&
            StatusIs(
                attempt.Status,
                nameof(LocalSquarePaymentAttemptStatus.Pending),
                nameof(LocalSquarePaymentAttemptStatus.CheckoutCreated),
                nameof(LocalSquarePaymentAttemptStatus.Recovering),
                nameof(LocalSquarePaymentAttemptStatus.CheckoutCompleted),
                nameof(LocalSquarePaymentAttemptStatus.Unknown));
    }

    private static bool StatusIs(string status, params string[] allowed) =>
        allowed.Any(candidate => string.Equals(status, candidate, StringComparison.OrdinalIgnoreCase));

    private static bool HasSquareRefundPaymentEvidence(CardRecoveryQueueItem attempt) =>
        attempt.Processor == CardProcessorKind.Square &&
        string.Equals(attempt.OperationKind, "Refund", StringComparison.OrdinalIgnoreCase) &&
        (Normalize(attempt.PaymentId) is not null || Normalize(attempt.PaymentStatus) is not null);

    private async Task ResolveSelectedAsync(
        CardRecoverySupervisorDecision decision,
        string action)
    {
        var selected = SelectedAttempt;
        if (selected is null)
        {
            return;
        }

        var reason = Normalize(ResolutionReason) ?? string.Empty;
        var evidence = Normalize(ResolutionEvidence);
        var reference = Normalize(ResolutionReference);
        IsBusy = true;
        try
        {
            var permissionCode = string.Equals(
                selected.OperationKind,
                "Refund",
                StringComparison.OrdinalIgnoreCase)
                ? Permissions.PosTerminal.Returns.Confirm
                : Permissions.PosTerminal.Payment.Confirm;
            using var authorization = await _authorizationService.AuthorizeAsync(
                permissionCode,
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
            if (SelectedAttempt?.Key != selected.Key)
            {
                SetStatusResource(
                    "cardRecovery.center.status.selectionChanged",
                    "The selected transaction changed. Select it again before continuing.");
                return;
            }

            var result = await _recoveryService.ResolveAsync(
                selected.Key,
                decision,
                reason,
                evidence,
                reference,
                _cart,
                _session);
            var actionMessage = string.IsNullOrWhiteSpace(result.Message)
                ? T("cardRecovery.center.status.resolveNoResult", "The resolution returned no message.")
                : result.Message;
            await RunPostCommitActionAsync(
                () => RefreshListCoreAsync(actionMessage),
                actionMessage,
                $"targeted resolution refresh processor={selected.Processor} attempt={selected.AttemptGuid:D} decision={decision}");
            if (result.RecoveryResult is not null && _recoveryResultHandledAsync is not null)
            {
                await RunPostCommitActionAsync(
                    () => _recoveryResultHandledAsync(result.RecoveryResult),
                    actionMessage,
                    $"targeted resolution callback processor={selected.Processor} attempt={selected.AttemptGuid:D} decision={decision}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
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

    private async Task RunPostCommitActionAsync(
        Func<Task> action,
        string committedMessage,
        string context)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // 服务结果已经确定，刷新或壳层回调失败不能伪装成主管决定保存失败。
            try
            {
                SetLiteralStatus(committedMessage);
            }
            catch (Exception statusException) when (
                statusException is not OutOfMemoryException and
                not StackOverflowException)
            {
                TryWritePostCommitWarning(
                    $"post-commit status restore failed context={context} error={statusException.GetType().Name}",
                    statusException);
            }

            TryWritePostCommitWarning(
                $"post-commit action failed context={context} error={ex.GetType().Name}",
                ex);
        }
    }

    private static void TryWritePostCommitWarning(string message, Exception exception)
    {
        try
        {
            ConsoleLog.WriteError("CardRecoveryCenter", message, exception: exception);
        }
        catch (Exception loggingException) when (
            loggingException is not OutOfMemoryException and
            not StackOverflowException)
        {
            // 提交后诊断日志自身失败只能被忽略，不能触发外层“保存失败”路径。
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
        if (!string.IsNullOrWhiteSpace(orderDraftJson) &&
            CardRecoveryCartMaterializer.TryPrepare(
                orderDraftJson,
                DraftJsonOptions,
                out var draft) &&
            draft is not null)
        {
            // 与实际恢复共用隔离物化规则，历史 JSON 即使语法合法但语义缺失也不能击穿异常中心。
            lines = draft.CartSnapshot.Lines;
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
        OnPropertyChanged(nameof(IsSquareRefundProcessing));
        OnPropertyChanged(nameof(CanShowSupervisorResolution));
        OnPropertyChanged(nameof(SquareRefundProcessingMessage));
        OnPropertyChanged(nameof(ResolutionSectionTitleText));
        OnPropertyChanged(nameof(ResolutionInstructionsText));
        OnPropertyChanged(nameof(ResolutionReasonLabelText));
        OnPropertyChanged(nameof(ResolutionEvidenceLabelText));
        OnPropertyChanged(nameof(ResolutionReferenceLabelText));
    }

    private async Task RefreshListCoreAsync(string? actionMessage = null)
    {
        var selectedKey = SelectedAttempt?.Key ?? SelectedRow?.Key;
        var items = await _recoveryService.ListOpenAsync(_session);
        OpenAttempts.Clear();
        foreach (var item in items)
        {
            OpenAttempts.Add(item);
        }

        RefreshDisplayRows(selectedKey);

        SelectedAttempt = selectedKey is null
            ? OpenAttempts.FirstOrDefault()
            : OpenAttempts.FirstOrDefault(item => item.Key == selectedKey.Value) ??
              OpenAttempts.FirstOrDefault();
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

    private void RefreshDisplayRows(CardRecoveryAttemptKey? preservedSelectionKey = null)
    {
        var selectedKey = preservedSelectionKey ?? SelectedAttempt?.Key ?? SelectedRow?.Key;
        _isRebuildingRows = true;
        try
        {
            OpenAttemptRows.Clear();
            foreach (var item in OpenAttempts)
            {
                OpenAttemptRows.Add(CreateDisplayRow(item));
            }
        }
        finally
        {
            _isRebuildingRows = false;
        }

        var selectedRow = selectedKey is null
            ? null
            : OpenAttemptRows.FirstOrDefault(row => row.Key == selectedKey.Value);
        if (!ReferenceEquals(_selectedRow, selectedRow))
        {
            _selectedRow = selectedRow;
            OnPropertyChanged(nameof(SelectedRow));
        }

        var selectedAttempt = selectedKey is null
            ? null
            : OpenAttempts.FirstOrDefault(item => item.Key == selectedKey.Value);
        if (!ReferenceEquals(_selectedAttempt, selectedAttempt))
        {
            SelectedAttempt = selectedAttempt;
        }
    }

    private CardRecoveryQueueRowViewModel CreateDisplayRow(CardRecoveryQueueItem item) =>
        new(
            item,
            MapOperationType(item.OperationKind),
            MapChannel(item.Processor),
            item.UpdatedAt.ToString("g", GetCulture()),
            item.Amount.ToString("C2", GetCulture()),
            MapStatus(item.Status));

    private string MapOperationType(string? value)
    {
        var compact = Normalize(value) is { } normalized
            ? new string(normalized.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()
            : string.Empty;
        var key = compact switch
        {
            "sale" => "cardRecovery.operation.sale",
            "refund" => "cardRecovery.operation.refund",
            "activesession" => "cardRecovery.operation.activeSession",
            _ => "cardRecovery.operation.unknown"
        };
        return T(key, key == "cardRecovery.operation.sale"
            ? "Sale"
            : key == "cardRecovery.operation.refund"
                ? "Refund"
                : key == "cardRecovery.operation.activeSession"
                    ? "Active session"
                    : "Unknown operation");
    }

    private string MapChannel(CardProcessorKind? value)
    {
        var key = value switch
        {
            CardProcessorKind.Linkly => "cardRecovery.channel.linkly",
            CardProcessorKind.Square => "cardRecovery.channel.square",
            _ => "cardRecovery.channel.unknown"
        };
        return T(key, key switch
        {
            "cardRecovery.channel.linkly" => "Linkly",
            "cardRecovery.channel.square" => "Square",
            _ => "Unknown channel"
        });
    }

    private string MapStatus(string? value)
    {
        var normalized = Normalize(value);
        var compact = normalized is not null
            ? new string(normalized.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()
            : string.Empty;
        var key = compact.Length == 0
            ? "cardRecovery.status.none"
            : compact switch
        {
            "none" => "cardRecovery.status.none",
            "pending" => "cardRecovery.status.pending",
            "sessionstarted" => "cardRecovery.status.sessionStarted",
            "recovering" => "cardRecovery.status.recovering",
            "approved" => "cardRecovery.status.approved",
            "requiresreview" => "cardRecovery.status.requiresReview",
            "declined" => "cardRecovery.status.declined",
            "timedout" => "cardRecovery.status.timedOut",
            "cancelled" => "cardRecovery.status.cancelled",
            "canceled" => "cardRecovery.status.canceled",
            "failed" => "cardRecovery.status.failed",
            "ordercompleted" => "cardRecovery.status.orderCompleted",
            "abandoned" => "cardRecovery.status.abandoned",
            "checkoutcreated" => "cardRecovery.status.checkoutCreated",
            "checkoutcompleted" => "cardRecovery.status.checkoutCompleted",
            "paymentverified" => "cardRecovery.status.paymentVerified",
            // Provider 队列会把该阶段放入 Status；它不是数据库状态枚举。
            "finalizepending" => "cardRecovery.status.finalizePending",
            _ => "cardRecovery.status.unknown"
        };
        return T(key, key switch
        {
            "cardRecovery.status.none" => "None",
            "cardRecovery.status.pending" => "Pending",
            "cardRecovery.status.sessionStarted" => "Session started",
            "cardRecovery.status.recovering" => "Recovering",
            "cardRecovery.status.approved" => "Approved",
            "cardRecovery.status.requiresReview" => "Requires review",
            "cardRecovery.status.declined" => "Declined",
            "cardRecovery.status.timedOut" => "Timed out",
            "cardRecovery.status.cancelled" or "cardRecovery.status.canceled" => "Cancelled",
            "cardRecovery.status.failed" => "Failed",
            "cardRecovery.status.orderCompleted" => "Order completed",
            "cardRecovery.status.abandoned" => "Abandoned",
            "cardRecovery.status.checkoutCreated" => "Checkout created",
            "cardRecovery.status.checkoutCompleted" => "Checkout completed",
            "cardRecovery.status.paymentVerified" => "Payment verified",
            "cardRecovery.status.finalizePending" => "Finalization pending",
            _ => "Unknown status"
        });
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshDisplayRows();
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
