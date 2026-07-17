using System.Collections.ObjectModel;
using System.Globalization;
using BlazorApp.Shared.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.ViewModels;

public partial class PaymentViewModel : ObservableObject, IDisposable
{
    private const decimal MinimumInstallmentTotalAmount = 50m;
    private const decimal MinimumInstallmentFirstPaymentAmount = 20m;

    private readonly PosCartService _cart;
    private readonly ICashPaymentWorkflowService _workflowService;
    private readonly IInstallmentOrderService _installmentOrderService;
    private readonly ILocalizationService? _localization;
    private readonly ICashierSessionContext _cashierSessionContext;
    private readonly bool _enforcePermissions;
    private readonly PaymentNavigationActions _navigationActions;
    private readonly IOperationAuditLogger? _operationAuditLogger;
    private readonly IOperationAuthorizationService? _operationAuthorizationService;

    internal PaymentNavigationActions NavigationActions => _navigationActions;
    private readonly PaymentTenderController _tenderController = new();
    private readonly CardPaymentSession _cardSession;
    private readonly ILinklyFallbackPromptCoordinator? _linklyFallbackPromptCoordinator;

    private static readonly Dictionary<PaymentMethodKind, IPaymentMethodStrategy> PaymentStrategies = new()
    {
        [PaymentMethodKind.Cash] = CashStrategy.Instance,
        [PaymentMethodKind.Card] = CardStrategy.Instance,
        [PaymentMethodKind.Voucher] = VoucherStrategy.Instance
    };
    private string _statusKey = "payment.status.ready";
    private string? _statusTextOverride;
    private Guid? _pendingVoucherUploadOrderGuid;
    private decimal _pendingVoucherTenderedAmount;
    private decimal _pendingVoucherChangeAmount;
    private int _paymentEntryVersion;
    private decimal _workflowRemainingAmount;
    private InstallmentOrderSummary? _installmentRepaymentOrder;
    private bool _disposed;

    [ObservableProperty]
    private PosSessionState _session;

    [ObservableProperty]
    private PaymentMethodKind _selectedPaymentMethod = PaymentMethodKind.Cash;

    [ObservableProperty]
    private string _tenderAmountText = string.Empty;

    [ObservableProperty]
    private string _voucherCodeText = string.Empty;

    [ObservableProperty]
    private string _voucherEntryText = string.Empty;

    [ObservableProperty]
    private bool _isVoucherEntryDialogOpen;

    [ObservableProperty]
    private decimal _changeDue;

    [ObservableProperty]
    private decimal _remainingAmount;

    [ObservableProperty]
    private decimal _totalTendered;

    [ObservableProperty]
    private int _pendingSyncCount;

    [ObservableProperty]
    private bool _isCardPaymentInProgress;

    [ObservableProperty]
    private bool _isPaymentInteractionLocked;

    [ObservableProperty]
    private PaymentEntryMode _paymentMode;

    [ObservableProperty]
    private CardPaymentErrorOverlayViewModel? _cardPaymentErrorOverlay;

    [ObservableProperty]
    private bool _isInstallmentPaymentEnabled;

    [ObservableProperty]
    private bool _isInstallmentSwitchLocked;

    [ObservableProperty]
    private string _installmentCustomerName = string.Empty;

    [ObservableProperty]
    private string _installmentCustomerPhone = string.Empty;

    [ObservableProperty]
    private bool _isInstallmentCustomerDialogOpen;

    [ObservableProperty]
    private string _installmentCustomerDraftName = string.Empty;

    [ObservableProperty]
    private string _installmentCustomerDraftPhone = string.Empty;

    [ObservableProperty]
    private string _installmentCustomerEditTarget = "Name";

    public PaymentViewModel(
        PosCartService cart,
        CashCheckoutService checkout,
        ILocalOrderRepository orderRepository,
        ISyncQueueRepository syncQueueRepository,
        PosSessionState session,
        ILocalizationService? localization = null,
        Action? onBackToPos = null,
        Action? onShowInstallmentCenter = null,
        Func<Task<bool>>? recoverPreviousCardTransactionAsync = null,
        ILinklyFallbackPromptCoordinator? linklyFallbackPromptCoordinator = null,
        ICashierSessionContext? cashierSessionContext = null,
        bool enforcePermissionsWhenNoCashier = false,
        IInstallmentOrderService? installmentOrderService = null,
        Func<InstallmentOrderSummary, Task>? onInstallmentOrderCreatedAsync = null,
        Func<Task<bool>>? confirmInstallmentFullFirstPaymentAsync = null,
        IOperationAuditLogger? operationAuditLogger = null,
        IOperationAuthorizationService? operationAuthorizationService = null)
        : this(
            cart,
            new CashPaymentWorkflowService(checkout, orderRepository, syncQueueRepository),
            session,
            localization,
            onBackToPos,
            onShowInstallmentCenter,
            recoverPreviousCardTransactionAsync,
            linklyFallbackPromptCoordinator,
            cashierSessionContext,
            enforcePermissionsWhenNoCashier,
            installmentOrderService,
            onInstallmentOrderCreatedAsync,
            confirmInstallmentFullFirstPaymentAsync,
            operationAuditLogger,
            operationAuthorizationService)
    {
    }

    public PaymentViewModel(
        PosCartService cart,
        ICashPaymentWorkflowService workflowService,
        PosSessionState session,
        ILocalizationService? localization = null,
        Action? onBackToPos = null,
        Action? onShowInstallmentCenter = null,
        Func<Task<bool>>? recoverPreviousCardTransactionAsync = null,
        ILinklyFallbackPromptCoordinator? linklyFallbackPromptCoordinator = null,
        ICashierSessionContext? cashierSessionContext = null,
        bool enforcePermissionsWhenNoCashier = false,
        IInstallmentOrderService? installmentOrderService = null,
        Func<InstallmentOrderSummary, Task>? onInstallmentOrderCreatedAsync = null,
        Func<Task<bool>>? confirmInstallmentFullFirstPaymentAsync = null,
        IOperationAuditLogger? operationAuditLogger = null,
        IOperationAuthorizationService? operationAuthorizationService = null)
    {
        _cart = cart;
        _workflowService = workflowService;
        _installmentOrderService = installmentOrderService ?? NoopInstallmentOrderService.Instance;
        _session = session;
        _localization = localization;
        _cashierSessionContext = cashierSessionContext ?? new CashierSessionContext();
        _enforcePermissions = enforcePermissionsWhenNoCashier;
        _operationAuditLogger = operationAuditLogger;
        _operationAuthorizationService = operationAuthorizationService;
        if (session.CashierSession is not null)
        {
            _cashierSessionContext.SetCurrent(session.CashierSession);
        }

        _navigationActions = PaymentNavigationActions.FromLegacyCallbacks(
            onBackToPos,
            onShowInstallmentCenter,
            recoverPreviousCardTransactionAsync,
            onInstallmentOrderCreatedAsync,
            confirmInstallmentFullFirstPaymentAsync);
        _linklyFallbackPromptCoordinator = linklyFallbackPromptCoordinator;
        _cardSession = new CardPaymentSession(this);
        _linklyFallbackPromptCoordinator?.SetPromptHandler(_cardSession.ConfirmLinklyFallbackAsync);
        if (_localization is not null)
        {
            _localization.CultureChanged += OnCultureChanged;
        }

        NumberInputCommand = new RelayCommand<string>(AppendTenderAmount, _ => IsPaymentInteractionEnabled);
        QuickCashCommand = new AsyncRelayCommand<QuickCashOption>(ApplyQuickCashAsync, CanApplyQuickCash);
        SelectCashCommand = new AsyncRelayCommand(() => AddTenderByMethodAsync(PaymentMethodKind.Cash), () => CanAddTender(PaymentMethodKind.Cash, allowDefaultAmount: true));
        SelectCardCommand = new AsyncRelayCommand(
            () => AddTenderByMethodAsync(PaymentMethodKind.Card),
            () => CanAddTender(PaymentMethodKind.Card, allowDefaultAmount: true));
        SelectVoucherCommand = new AsyncRelayCommand(() => AddTenderByMethodAsync(PaymentMethodKind.Voucher), () => CanAddTender(PaymentMethodKind.Voucher, allowDefaultAmount: true));
        OpenVoucherEntryCommand = new RelayCommand(OpenVoucherEntry, CanOpenVoucherEntry);
        VoucherEntryKeyCommand = new RelayCommand<string>(ApplyVoucherEntryKey, _ => CanUseVoucherEntryDialog());
        ConfirmVoucherEntryCommand = new AsyncRelayCommand(ConfirmVoucherEntryAsync, CanConfirmVoucherEntry);
        CancelVoucherEntryCommand = new RelayCommand(CancelVoucherEntry, () => IsVoucherEntryDialogOpen);
        OpenInstallmentCustomerDialogCommand = new RelayCommand(OpenInstallmentCustomerDialog, CanOpenInstallmentCustomerDialog);
        SelectInstallmentCustomerFieldCommand = new RelayCommand<string>(SelectInstallmentCustomerField, _ => IsInstallmentCustomerDialogOpen);
        InstallmentCustomerKeyCommand = new RelayCommand<string>(ApplyInstallmentCustomerKey, _ => CanUseInstallmentCustomerDialog());
        ConfirmInstallmentCustomerDialogCommand = new RelayCommand(ConfirmInstallmentCustomerDialog, CanUseInstallmentCustomerDialog);
        CancelInstallmentCustomerDialogCommand = new RelayCommand(CancelInstallmentCustomerDialog, () => IsInstallmentCustomerDialogOpen);
        RemoveTenderCommand = new AsyncRelayCommand<PaymentTender?>(RemoveTenderAsync, CanRemoveTender);
        ConfirmPaymentCommand = new AsyncRelayCommand(ConfirmPaymentAsync, CanConfirmPayment);
        CancelCommand = new RelayCommand(CancelPayment, CanCancelPayment);
        BackToPosCommand = new RelayCommand(BackToPos, CanBackToPos);
        ShowInstallmentCenterCommand = new AsyncRelayCommand(ShowInstallmentCenterAsync, CanShowInstallmentCenter);
        CloseCardPaymentErrorOverlayCommand = new RelayCommand(CloseCardPaymentErrorOverlay);
        CardPaymentErrorPrimaryActionCommand = new AsyncRelayCommand(
            ExecuteCardPaymentErrorPrimaryActionAsync,
            CanExecuteCardPaymentErrorPrimaryAction);

        RefreshCart();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_localization is not null)
        {
            _localization.CultureChanged -= OnCultureChanged;
        }

        _cardSession.Dispose();
    }

    public ObservableCollection<CartLine> CartLines { get; } = [];

    public ObservableCollection<PaymentTender> PaymentTenders { get; } = [];

    public IReadOnlyList<QuickCashOption> QuickCashAmounts => BuildQuickCashAmounts();

    public IRelayCommand<string> NumberInputCommand { get; }

    public IAsyncRelayCommand<QuickCashOption> QuickCashCommand { get; }

    public IAsyncRelayCommand SelectCashCommand { get; }

    public IAsyncRelayCommand SelectCardCommand { get; }

    public IAsyncRelayCommand SelectVoucherCommand { get; }

    public IRelayCommand OpenVoucherEntryCommand { get; }

    public IRelayCommand<string> VoucherEntryKeyCommand { get; }

    public IAsyncRelayCommand ConfirmVoucherEntryCommand { get; }

    public IRelayCommand CancelVoucherEntryCommand { get; }

    public IRelayCommand OpenInstallmentCustomerDialogCommand { get; }

    public IRelayCommand<string> SelectInstallmentCustomerFieldCommand { get; }

    public IRelayCommand<string> InstallmentCustomerKeyCommand { get; }

    public IRelayCommand ConfirmInstallmentCustomerDialogCommand { get; }

    public IRelayCommand CancelInstallmentCustomerDialogCommand { get; }

    public IAsyncRelayCommand<PaymentTender?> RemoveTenderCommand { get; }

    public IAsyncRelayCommand ConfirmPaymentCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand BackToPosCommand { get; }

    public IRelayCommand ShowInstallmentCenterCommand { get; }

    public IRelayCommand CloseCardPaymentErrorOverlayCommand { get; }

    public IAsyncRelayCommand CardPaymentErrorPrimaryActionCommand { get; }

    public event EventHandler<PaymentCompletedEventArgs>? PaymentCompleted;

    public string ScreenTitleText => T(GetScreenTitleKey());

    public string OrderSummaryText => T("payment.orderSummary");

    public string CurrentTenderTextLabel => T("payment.currentTender");

    public string AmountTenderedTextLabel => CurrentTenderTextLabel;

    public string RemainingAmountText => T(GetRemainingAmountKey());

    public string ChangeDueText => T("payment.changeDue");

    public string QuickCashText => T("payment.quickCash");

    public string PaymentMethodText => T("payment.method");

    public string AppliedTendersText => T("payment.appliedTenders");

    public string ConfirmPaymentText => T(GetConfirmPaymentKey());

    public string NoTendersText => T(GetNoTendersKey());

    public string CashMethodText => T(GetMethodTextKey(PaymentMethodKind.Cash));

    public string CardMethodText => T(GetMethodTextKey(PaymentMethodKind.Card));

    public string VoucherMethodText => T(GetMethodTextKey(PaymentMethodKind.Voucher));

    public string InstallmentMethodText => T("payment.method.installment");

    public string CancelText => T("common.cancel");

    public string StatusMessage => _statusTextOverride ?? T(_statusKey);

    public bool IsPaymentInteractionEnabled => !IsPaymentInteractionLocked && !_cardSession.HasUnknownResult;

    // 普通支付状态隐藏取消入口，避免将取消误用为返回收银页。
    public bool IsCancelPaymentVisible => IsCardPaymentInProgress || _cardSession.IsAwaitingLateResult;

    public decimal TotalAmount => _cart.TotalAmount;

    public decimal DiscountAmount => _cart.DiscountAmount;

    public decimal ActualAmount => _cart.ActualAmount;

    public bool IsRefundMode => PaymentMode == PaymentEntryMode.Refund;

    public bool IsZeroSettlementMode => PaymentMode == PaymentEntryMode.ZeroSettlement;

    public bool IsPaymentMode => PaymentMode == PaymentEntryMode.Payment;

    public bool IsTenderEntryVisible => !IsZeroSettlementMode;

    public bool IsPaymentMethodSelectionVisible => !IsZeroSettlementMode;

    public bool IsQuickCashVisible => IsPaymentMode && IsCashSelected;

    public string AmountTenderedText
    {
        get => TenderAmountText;
        set => TenderAmountText = value;
    }

    public bool IsCashSelected => SelectedPaymentMethod == PaymentMethodKind.Cash;

    public bool IsCardSelected => SelectedPaymentMethod == PaymentMethodKind.Card;

    public bool IsVoucherSelected => SelectedPaymentMethod == PaymentMethodKind.Voucher;

    public bool IsVoucherCodeEntryVisible => IsVoucherSelected && !IsRefundMode;

    public bool IsInstallmentEntryVisible => IsPaymentMode;

    public bool IsInstallmentCustomerSectionVisible => IsInstallmentPaymentEnabled;

    public bool CanEditInstallmentCustomer => IsInstallmentPaymentEnabled && _installmentRepaymentOrder is null;

    public string InstallmentCustomerNameDisplay => string.IsNullOrWhiteSpace(InstallmentCustomerName)
        ? T("payment.installment.customerMissingName")
        : InstallmentCustomerName;

    public string InstallmentCustomerPhoneDisplay => string.IsNullOrWhiteSpace(InstallmentCustomerPhone)
        ? T("payment.installment.customerMissingPhone")
        : InstallmentCustomerPhone;

    public bool IsInstallmentCustomerNameDraftActive => InstallmentCustomerEditTarget.Equals("Name", StringComparison.OrdinalIgnoreCase);

    public bool IsInstallmentCustomerPhoneDraftActive => InstallmentCustomerEditTarget.Equals("Phone", StringComparison.OrdinalIgnoreCase);

    public string InstallmentOrderInfoText => _installmentRepaymentOrder is null
        ? string.Empty
        : $"{_installmentRepaymentOrder.OrderNumber} | {_installmentRepaymentOrder.CustomerName} | {_installmentRepaymentOrder.CustomerPhone} | {T("payment.installment.outstanding")}: {_installmentRepaymentOrder.OutstandingAmount:C2}";

    public bool IsConfirmPaymentVisible => CanConfirmPayment();

    partial void OnTenderAmountTextChanged(string value)
    {
        RecalculateTenderSummary();
        NotifyPaymentCommandStates();
    }

    partial void OnVoucherCodeTextChanged(string value)
    {
        NotifyPaymentCommandStates();
    }

    partial void OnIsInstallmentPaymentEnabledChanged(bool value)
    {
        if (IsInstallmentSwitchLocked && !value)
        {
            // 中文注释：历史分期补款必须锁定在分期模式，避免误切回普通付款。
            IsInstallmentPaymentEnabled = true;
            return;
        }

        if (!value)
        {
            _installmentRepaymentOrder = null;
            IsInstallmentSwitchLocked = false;
            InstallmentCustomerName = string.Empty;
            InstallmentCustomerPhone = string.Empty;
            InstallmentCustomerDraftName = string.Empty;
            InstallmentCustomerDraftPhone = string.Empty;
            IsInstallmentCustomerDialogOpen = false;
        }

        OnPropertyChanged(nameof(IsInstallmentCustomerSectionVisible));
        OnPropertyChanged(nameof(CanEditInstallmentCustomer));
        OnPropertyChanged(nameof(InstallmentOrderInfoText));
        OpenInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
        RecalculateTenderSummary();
        NotifyPaymentCommandStates();
    }

    partial void OnIsInstallmentSwitchLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditInstallmentCustomer));
        OpenInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
    }

    partial void OnInstallmentCustomerNameChanged(string value)
    {
        OnPropertyChanged(nameof(InstallmentCustomerNameDisplay));
        NotifyPaymentCommandStates();
    }

    partial void OnInstallmentCustomerPhoneChanged(string value)
    {
        OnPropertyChanged(nameof(InstallmentCustomerPhoneDisplay));
        NotifyPaymentCommandStates();
    }

    partial void OnIsInstallmentCustomerDialogOpenChanged(bool value)
    {
        SelectInstallmentCustomerFieldCommand.NotifyCanExecuteChanged();
        InstallmentCustomerKeyCommand.NotifyCanExecuteChanged();
        ConfirmInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
        CancelInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
    }

    partial void OnInstallmentCustomerEditTargetChanged(string value)
    {
        OnPropertyChanged(nameof(IsInstallmentCustomerNameDraftActive));
        OnPropertyChanged(nameof(IsInstallmentCustomerPhoneDraftActive));
    }

    partial void OnVoucherEntryTextChanged(string value)
    {
        VoucherEntryKeyCommand.NotifyCanExecuteChanged();
        ConfirmVoucherEntryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsVoucherEntryDialogOpenChanged(bool value)
    {
        VoucherEntryKeyCommand.NotifyCanExecuteChanged();
        ConfirmVoucherEntryCommand.NotifyCanExecuteChanged();
        CancelVoucherEntryCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPaymentMethodChanged(PaymentMethodKind value)
    {
        SelectCashCommand.NotifyCanExecuteChanged();
        SelectCardCommand.NotifyCanExecuteChanged();
        SelectVoucherCommand.NotifyCanExecuteChanged();
        OpenVoucherEntryCommand.NotifyCanExecuteChanged();
        QuickCashCommand.NotifyCanExecuteChanged();
        ShowInstallmentCenterCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsCashSelected));
        OnPropertyChanged(nameof(IsCardSelected));
        OnPropertyChanged(nameof(IsVoucherSelected));
        OnPropertyChanged(nameof(IsVoucherCodeEntryVisible));
        OnPropertyChanged(nameof(QuickCashAmounts));
        OnPropertyChanged(nameof(IsQuickCashVisible));
    }

    partial void OnIsCardPaymentInProgressChanged(bool value)
    {
        NotifyPaymentCommandStates();
    }

    partial void OnIsPaymentInteractionLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPaymentInteractionEnabled));
        NotifyPaymentCommandStates();
    }

    partial void OnSessionChanged(PosSessionState value)
    {
        if (value.CashierSession is not null)
        {
            _cashierSessionContext.SetCurrent(value.CashierSession);
        }

        PendingSyncCount = value.PendingSyncCount;
        NotifyPaymentCommandStates();
    }

    partial void OnPaymentModeChanged(PaymentEntryMode value)
    {
        RaiseLocalizedProperties();
        OnPropertyChanged(nameof(IsRefundMode));
        OnPropertyChanged(nameof(IsZeroSettlementMode));
        OnPropertyChanged(nameof(IsPaymentMode));
        OnPropertyChanged(nameof(IsTenderEntryVisible));
        OnPropertyChanged(nameof(IsPaymentMethodSelectionVisible));
        OnPropertyChanged(nameof(IsQuickCashVisible));
        OnPropertyChanged(nameof(IsVoucherCodeEntryVisible));
        OnPropertyChanged(nameof(IsInstallmentEntryVisible));
        OpenVoucherEntryCommand.NotifyCanExecuteChanged();
        OpenInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
        ShowInstallmentCenterCommand.NotifyCanExecuteChanged();
    }

    public void PrepareForEntry(PosSessionState session)
    {
        Session = session;
        _pendingVoucherUploadOrderGuid = null;
        _pendingVoucherTenderedAmount = 0m;
        _pendingVoucherChangeAmount = 0m;
        _paymentEntryVersion++;
        _cardSession.ResetManualCancellationState();
        _cardSession.SetResultUnknownRecoveryRequired(false);
        _cardSession.Cancel();
        _cardSession.DetachCanceledActiveCardPayment();
        IsCardPaymentInProgress = false;
        IsPaymentInteractionLocked = false;
        _installmentRepaymentOrder = null;
        IsInstallmentSwitchLocked = false;
        IsInstallmentPaymentEnabled = false;
        InstallmentCustomerName = string.Empty;
        InstallmentCustomerPhone = string.Empty;
        InstallmentCustomerDraftName = string.Empty;
        InstallmentCustomerDraftPhone = string.Empty;
        IsInstallmentCustomerDialogOpen = false;
        PaymentTenders.Clear();
        VoucherCodeText = string.Empty;
        VoucherEntryText = string.Empty;
        IsVoucherEntryDialogOpen = false;
        TenderAmountText = string.Empty;
        _statusKey = GetReadyStatusKey();
        _statusTextOverride = null;
        SelectedPaymentMethod = PaymentMethodKind.Cash;
        RefreshCart();
        if (!HasBlockingCartIssue())
        {
            SetStatus(GetReadyStatusKey());
        }

        OnPropertyChanged(nameof(StatusMessage));
    }

    public void PrepareForInstallmentRepayment(PosSessionState session, InstallmentOrderSummary order)
    {
        PrepareForEntry(session);

        // 中文注释：历史页续付直接进入普通支付页，但目标金额来自原分期单余额。
        _installmentRepaymentOrder = order;
        InstallmentCustomerName = order.CustomerName;
        InstallmentCustomerPhone = order.CustomerPhone;
        IsInstallmentPaymentEnabled = true;
        IsInstallmentSwitchLocked = true;
        PaymentMode = CalculatePaymentMode();
        RecalculateTenderSummary();
        SetStatus(GetReadyStatusKey());
        OnPropertyChanged(nameof(CanEditInstallmentCustomer));
        OnPropertyChanged(nameof(InstallmentOrderInfoText));
        NotifyPaymentCommandStates();
    }

    public void RefreshCart()
    {
        PaymentMode = CalculatePaymentMode();
        CartLines.ReplaceWith(_cart.Lines);
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(ActualAmount));
        RecalculateTenderSummary();
        RefreshCartValidationStatus();
        NotifyPaymentCommandStates();
    }

    internal void RestoreRecoveredPaymentTenders(IReadOnlyList<PaymentTender> tenders, string? statusMessage)
    {
        PaymentTenders.Clear();
        foreach (var tender in tenders)
        {
            PaymentTenders.Add(tender);
        }

        TenderAmountText = string.Empty;
        VoucherCodeText = string.Empty;
        VoucherEntryText = string.Empty;
        IsVoucherEntryDialogOpen = false;

        // 中文注释：恢复已批准卡 tender 只回填付款页状态，剩余金额仍由收银员补齐后走现有完成订单流程。
        RefreshCart();
        SetStatus("payment.status.cardTenderAdded", statusMessage);
        NotifyPaymentCommandStates();
    }

    private void AppendTenderAmount(string? value)
    {
        if (IsPaymentInteractionLocked || _cardSession.IsActive || _cardSession.HasUnknownResult)
        {
            return;
        }

        if (value == "Back")
        {
            TenderAmountText = TenderAmountText.Length > 0 ? TenderAmountText[..^1] : string.Empty;
            return;
        }

        if (value == "Clear")
        {
            TenderAmountText = string.Empty;
            return;
        }

        if (value == "." && TenderAmountText.Contains('.', StringComparison.Ordinal))
        {
            return;
        }

        TenderAmountText += value;
    }

    private async Task ApplyQuickCashAsync(QuickCashOption? option)
    {
        await _tenderController.ApplyQuickCashAsync(
            option,
            amountText => TenderAmountText = amountText,
            AddTenderByMethodAsync);
    }

    private void OpenVoucherEntry()
    {
        if (!CanOpenVoucherEntry())
        {
            return;
        }

        SelectedPaymentMethod = PaymentMethodKind.Voucher;
        VoucherEntryText = VoucherCodeText;
        IsVoucherEntryDialogOpen = true;
    }

    private bool CanOpenVoucherEntry()
    {
        return CanAddTender(PaymentMethodKind.Voucher, allowDefaultAmount: true);
    }

    private void ApplyVoucherEntryKey(string? key)
    {
        if (!CanUseVoucherEntryDialog() || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (key.Equals("Back", StringComparison.OrdinalIgnoreCase))
        {
            VoucherEntryText = VoucherEntryText.Length > 0 ? VoucherEntryText[..^1] : string.Empty;
            return;
        }

        if (key.Equals("Clear", StringComparison.OrdinalIgnoreCase))
        {
            VoucherEntryText = string.Empty;
            return;
        }

        if (key.Length == 1 && (char.IsLetterOrDigit(key[0]) || key[0] == '-'))
        {
            VoucherEntryText += key.ToUpperInvariant();
        }
    }

    private async Task ConfirmVoucherEntryAsync()
    {
        if (!IsVoucherEntryDialogOpen)
        {
            return;
        }

        var tenderCountBeforeConfirm = PaymentTenders.Count;
        VoucherCodeText = VoucherEntryText.Trim();

        // 中文注释：确认时复用现有代金券付款命令，空码、重复付款和后端失败都沿用原状态提示。
        await SelectVoucherCommand.ExecuteAsync(null);
        if (PaymentTenders.Count <= tenderCountBeforeConfirm)
        {
            return;
        }

        VoucherEntryText = string.Empty;
        IsVoucherEntryDialogOpen = false;
    }

    private bool CanConfirmVoucherEntry()
    {
        return CanUseVoucherEntryDialog();
    }

    private bool CanUseVoucherEntryDialog()
    {
        return IsVoucherEntryDialogOpen && IsPaymentInteractionEnabled;
    }

    private void CancelVoucherEntry()
    {
        VoucherEntryText = string.Empty;
        IsVoucherEntryDialogOpen = false;
    }

    private void OpenInstallmentCustomerDialog()
    {
        if (!CanOpenInstallmentCustomerDialog())
        {
            return;
        }

        InstallmentCustomerDraftName = InstallmentCustomerName;
        InstallmentCustomerDraftPhone = InstallmentCustomerPhone;
        InstallmentCustomerEditTarget = "Name";
        IsInstallmentCustomerDialogOpen = true;
    }

    private bool CanOpenInstallmentCustomerDialog()
    {
        return CanEditInstallmentCustomer && IsPaymentInteractionEnabled;
    }

    private void SelectInstallmentCustomerField(string? target)
    {
        if (!IsInstallmentCustomerDialogOpen || string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        if (target.Equals("Phone", StringComparison.OrdinalIgnoreCase))
        {
            InstallmentCustomerEditTarget = "Phone";
            return;
        }

        InstallmentCustomerEditTarget = "Name";
    }

    private void ApplyInstallmentCustomerKey(string? key)
    {
        if (!CanUseInstallmentCustomerDialog() || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (key.Equals("Back", StringComparison.OrdinalIgnoreCase))
        {
            UpdateActiveInstallmentCustomerDraft(value => value.Length > 0 ? value[..^1] : string.Empty);
            return;
        }

        if (key.Equals("Clear", StringComparison.OrdinalIgnoreCase))
        {
            UpdateActiveInstallmentCustomerDraft(_ => string.Empty);
            return;
        }

        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase) && IsInstallmentCustomerNameDraftActive)
        {
            InstallmentCustomerDraftName += " ";
            return;
        }

        if (key.Length != 1)
        {
            return;
        }

        var character = key[0];
        if (IsInstallmentCustomerNameDraftActive && char.IsLetterOrDigit(character))
        {
            InstallmentCustomerDraftName += char.ToUpperInvariant(character);
            return;
        }

        if (IsInstallmentCustomerPhoneDraftActive &&
            (char.IsDigit(character) || character is '+' or '-'))
        {
            InstallmentCustomerDraftPhone += character;
        }
    }

    private bool CanUseInstallmentCustomerDialog()
    {
        return IsInstallmentCustomerDialogOpen && CanEditInstallmentCustomer && IsPaymentInteractionEnabled;
    }

    private void ConfirmInstallmentCustomerDialog()
    {
        if (!IsInstallmentCustomerDialogOpen)
        {
            return;
        }

        InstallmentCustomerName = InstallmentCustomerDraftName.Trim();
        InstallmentCustomerPhone = InstallmentCustomerDraftPhone.Trim();
        IsInstallmentCustomerDialogOpen = false;
    }

    private void CancelInstallmentCustomerDialog()
    {
        InstallmentCustomerDraftName = string.Empty;
        InstallmentCustomerDraftPhone = string.Empty;
        IsInstallmentCustomerDialogOpen = false;
    }

    private void UpdateActiveInstallmentCustomerDraft(Func<string, string> update)
    {
        if (IsInstallmentCustomerPhoneDraftActive)
        {
            InstallmentCustomerDraftPhone = update(InstallmentCustomerDraftPhone);
            return;
        }

        InstallmentCustomerDraftName = update(InstallmentCustomerDraftName);
    }

    private async Task AddTenderByMethodAsync(PaymentMethodKind method)
    {
        if (IsInstallmentPaymentEnabled && PaymentTenders.Count > 0)
        {
            SetStatus("payment.installment.status.singleTenderOnly");
            NotifyPaymentCommandStates();
            return;
        }

        if (!TryApplyAddTenderPlan(
                _tenderController.CreateAddTenderPlan(BuildAddTenderRequest(method)),
                out var amountText,
                out var referenceText))
        {
            return;
        }

        if (IsInstallmentPaymentEnabled &&
            _workflowService.TryParseTenderedAmount(amountText, out var plannedAmount) &&
            !IsInstallmentTenderAmountAllowed(method, plannedAmount, GetPaymentTargetAmount()))
        {
            SetStatus("payment.installment.status.invalidAmount");
            NotifyPaymentCommandStates();
            return;
        }

        if (TrySetRegularPaymentTenderOrderStatus(method, amountText))
        {
            return;
        }

        var attemptedPaymentAmount = ResolveAttemptedPaymentAmount(amountText);
        using var tenderPermissionGrant = await AuthorizeAsync(
            GetTenderPermission(method),
            $"add-tender-{method.ToString().ToLowerInvariant()}",
            method,
            attemptedPaymentAmount);
        if (tenderPermissionGrant is null)
        {
            return;
        }

        var willAutoCompleteCardPayment = method == PaymentMethodKind.Card &&
            !IsInstallmentPaymentEnabled &&
            (IsPaymentMode || IsRefundMode) &&
            _workflowService.TryParseTenderedAmount(amountText, out var plannedCardAmount) &&
            plannedCardAmount >= RemainingAmount;
        using var confirmPermissionGrant = willAutoCompleteCardPayment
            ? await AuthorizeAsync(
                Permissions.PosTerminal.Payment.Confirm,
                "confirm-payment",
                method,
                attemptedPaymentAmount)
            : null;
        if (willAutoCompleteCardPayment && confirmPermissionGrant is null)
        {
            return;
        }

        // 中文注释：卡终端阶段只激活收卡授权；确认付款授权到真正完成订单前才单独激活。
        using var tenderAuthorizationActivation = tenderPermissionGrant.Activate();

        PaymentTenderAttemptResult result;
        CancellationTokenSource? cardPaymentCts = null;
        var cardPaymentWasManuallyCancelled = false;
        var isCard = method == PaymentMethodKind.Card;
        var paymentEntryVersion = _paymentEntryVersion;
        try
        {
            if (isCard)
            {
                cardPaymentCts = _cardSession.BeginCardPayment();
            }

            if (IsRefundMode && isCard)
            {
                ConsoleLog.Write(
                    "CardRefund",
                    $"payment view add card refund amountText={LogValue(amountText)} originalReference={LogValue(referenceText)} " +
                    $"tenders={PaymentTenders.Count} capacities={_cart.ReturnPaymentCapacities.Count}");
            }

            result = await _workflowService.AddTenderAsync(
                method,
                Session,
                GetPaymentTargetAmount(),
                PaymentTenders.ToList(),
                amountText,
                referenceText,
                isCard ? cardPaymentCts?.Token ?? CancellationToken.None : CancellationToken.None,
                isCard && !IsInstallmentRepaymentMode ? _cart.CreateSnapshot() : null);
            if (isCard && cardPaymentCts?.IsCancellationRequested == true)
            {
                cardPaymentWasManuallyCancelled = _cardSession.IsManualCancellation(cardPaymentCts);
            }
        }
        catch (OperationCanceledException ex) when (isCard)
        {
            if (!IsCurrentPaymentEntry(paymentEntryVersion))
            {
                return;
            }

            var wasManuallyCancelled = _cardSession.IsManualCancellation(cardPaymentCts);
            if (!wasManuallyCancelled)
            {
                var correlation = OperationAuditEvents.CreateCorrelation();
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.PaymentTenderAdd,
                    "Failed",
                    Session,
                    OperationAuditEvents.CaptureCart(_cart.Lines),
                    reasonCode: "CARD_TIMEOUT",
                    safeMessage: ex.GetType().Name,
                    paymentMethod: PaymentMethodKind.Card.ToString(),
                    paymentAmount: attemptedPaymentAmount,
                    correlationId: correlation.CorrelationId,
                    traceId: correlation.TraceId);
                ConsoleLog.WriteError(
                    "OperationAudit",
                    $"card tender cancelled without manual request error={ex.GetType().Name}",
                    new ApplicationLogContext(TraceId: correlation.TraceId),
                    ex);
            }

            _cardSession.HandleOperationCanceledException(cardPaymentCts, paymentEntryVersion);
            await ReleaseVoucherTendersAfterCardFailureAsync();
            return;
        }
        catch (Exception ex) when (isCard && ex is not OperationCanceledException)
        {
            if (!IsCurrentPaymentEntry(paymentEntryVersion))
            {
                return;
            }

            var correlation = OperationAuditEvents.CreateCorrelation();
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.PaymentTenderAdd,
                "Failed",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "CARD_EXCEPTION",
                safeMessage: ex.GetType().Name,
                paymentMethod: PaymentMethodKind.Card.ToString(),
                paymentAmount: attemptedPaymentAmount,
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"card tender failed error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            _cardSession.HandleUnexpectedException(ex, paymentEntryVersion);
            await ReleaseVoucherTendersAfterCardFailureAsync();
            return;
        }
        catch (OperationCanceledException ex)
        {
            var correlation = OperationAuditEvents.CreateCorrelation();
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.PaymentTenderAdd,
                "Failed",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "TENDER_CANCELLED",
                safeMessage: ex.GetType().Name,
                paymentMethod: method.ToString(),
                paymentAmount: attemptedPaymentAmount,
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"tender cancelled unexpectedly method={method} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            throw;
        }
        catch (Exception ex)
        {
            var correlation = OperationAuditEvents.CreateCorrelation();
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.PaymentTenderAdd,
                "Failed",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "TENDER_EXCEPTION",
                safeMessage: ex.GetType().Name,
                paymentMethod: method.ToString(),
                paymentAmount: attemptedPaymentAmount,
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"tender failed method={method} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            throw;
        }
        finally
        {
            if (isCard)
            {
                _cardSession.EndCardPayment(cardPaymentCts);
            }
        }

        if (!IsCurrentPaymentEntry(paymentEntryVersion))
        {
            return;
        }

        if (isCard &&
            cardPaymentCts?.IsCancellationRequested == true &&
            (!result.Succeeded || result.Tender is null))
        {
            _cardSession.TryHandleCancelledResult(result, cardPaymentCts, cardPaymentWasManuallyCancelled);
            await ReleaseVoucherTendersAfterCardFailureAsync();
            return;
        }

        if (isCard && _cardSession.ShouldDiscardLateResult)
        {
            _cardSession.SetCancellationStatus(wasManuallyCancelled: true);
            _cardSession.ResetManualCancellationState();
            NotifyPaymentCommandStates();
            await ReleaseVoucherTendersAfterCardFailureAsync();
            return;
        }

        if (!result.Succeeded || result.Tender is null)
        {
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.PaymentTenderAdd,
                "Failed",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: result.StatusKey,
                safeMessage: result.StatusMessage,
                paymentMethod: method.ToString(),
                paymentAmount: attemptedPaymentAmount);
            if (IsRefundMode && isCard)
            {
                ConsoleLog.Write(
                    "CardRefund",
                    $"payment view card refund tender failed statusKey={result.StatusKey} message={LogValue(result.StatusMessage)}");
            }

            if (isCard)
            {
                var resultUnknown = CardPaymentSession.IsCardResultUnknownStatusKey(result.StatusKey);
                _cardSession.TryHandleFailedResult(result);
                if (!resultUnknown)
                {
                    await ReleaseVoucherTendersAfterCardFailureAsync();
                }

                return;
            }

            SetStatus(result.StatusKey, result.StatusMessage);
            NotifyPaymentCommandStates();
            return;
        }

        if (isCard)
        {
            _cardSession.ResetManualCancellationState();
            _cardSession.SetResultUnknownRecoveryRequired(false);
        }

        PaymentTenders.Add(result.Tender);
        OperationAuditEvents.RecordAction(
            _operationAuditLogger,
            OperationAuditTypes.PaymentTenderAdd,
            "Succeeded",
            Session,
            OperationAuditEvents.CaptureCart(_cart.Lines),
            reasonCode: IsRefundMode ? "REFUND" : "SALE",
            paymentMethod: result.Tender.Method.ToString(),
            paymentAmount: result.Tender.Amount);
        if (method == PaymentMethodKind.Voucher)
        {
            VoucherCodeText = string.Empty;
        }

        TenderAmountText = string.Empty;
        RecalculateTenderSummary();
        SetStatus(result.StatusKey, result.StatusMessage);
        NotifyPaymentCommandStates();
        if (isCard &&
            !IsInstallmentPaymentEnabled &&
            !cardPaymentWasManuallyCancelled &&
            IsSettlementComplete() &&
            (IsPaymentMode || IsRefundMode))
        {
            tenderAuthorizationActivation.Dispose();
            IDisposable? confirmAuthorizationActivation;
            try
            {
                confirmAuthorizationActivation = confirmPermissionGrant?.Activate();
            }
            catch (ObjectDisposedException)
            {
                // 中文注释：长时间卡交易期间授权可能被全局撤销；完成订单前必须重新确认，不能绕过权限。
                using var refreshedConfirmPermissionGrant = await AuthorizeAsync(
                    Permissions.PosTerminal.Payment.Confirm,
                    "confirm-payment",
                    method,
                    attemptedPaymentAmount);
                if (refreshedConfirmPermissionGrant is null)
                {
                    return;
                }

                using var refreshedConfirmAuthorizationActivation = refreshedConfirmPermissionGrant.Activate();
                await CompletePaymentFromTendersAsync();
                return;
            }

            using (confirmAuthorizationActivation)
            {
                await CompletePaymentFromTendersAsync();
            }
        }
    }

    private bool TrySetRegularPaymentTenderOrderStatus(PaymentMethodKind method, string? amountText)
    {
        if (!IsPaymentMode)
        {
            return false;
        }

        if (method == PaymentMethodKind.Voucher &&
            PaymentTenders.Any(tender => tender.Method == PaymentMethodKind.Cash))
        {
            SetStatus("payment.status.paymentMethodOrder");
            NotifyPaymentCommandStates();
            return true;
        }

        if (method == PaymentMethodKind.Card &&
            _workflowService.TryParseTenderedAmount(amountText, out var tenderAmount) &&
            tenderAmount > 0m &&
            tenderAmount < RemainingAmount)
        {
            SetStatus("payment.status.cardMustBeFinalTender");
            NotifyPaymentCommandStates();
            return true;
        }

        // 中文注释：恢复出的 partial card tender 只允许补齐差额，是否结清仍由完成付款校验兜底。
        return false;
    }

    private async Task<bool> ReleaseVoucherTendersAfterCardFailureAsync()
    {
        if (!IsPaymentMode)
        {
            return true;
        }

        var voucherTenders = PaymentTenders
            .Where(tender => tender.Method == PaymentMethodKind.Voucher)
            .ToList();
        if (voucherTenders.Count == 0)
        {
            return true;
        }

        foreach (var tender in voucherTenders)
        {
            bool released;
            try
            {
                released = await _workflowService.ReleaseVoucherTenderAsync(tender, Session);
            }
            catch (Exception ex)
            {
                var correlation = OperationAuditEvents.CreateCorrelation();
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.PaymentTenderRemove,
                    "Failed",
                    Session,
                    OperationAuditEvents.CaptureCart(_cart.Lines),
                    reasonCode: "CARD_FAILURE_RELEASE_EXCEPTION",
                    safeMessage: ex.GetType().Name,
                    paymentMethod: tender.Method.ToString(),
                    paymentAmount: tender.Amount,
                    correlationId: correlation.CorrelationId,
                    traceId: correlation.TraceId);
                ConsoleLog.WriteError(
                    "OperationAudit",
                    $"automatic voucher release failed error={ex.GetType().Name}",
                    new ApplicationLogContext(TraceId: correlation.TraceId),
                    ex);
                throw;
            }

            if (!released)
            {
                SetStatus("payment.status.voucherReleaseFailed");
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.PaymentTenderRemove,
                    "Failed",
                    Session,
                    OperationAuditEvents.CaptureCart(_cart.Lines),
                    reasonCode: "CARD_FAILURE_RELEASE_FAILED",
                    safeMessage: StatusMessage,
                    paymentMethod: tender.Method.ToString(),
                    paymentAmount: tender.Amount);
                NotifyPaymentCommandStates();
                return false;
            }

            PaymentTenders.Remove(tender);
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.PaymentTenderRemove,
                "Succeeded",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "CARD_FAILURE_AUTO_RELEASE",
                paymentMethod: tender.Method.ToString(),
                paymentAmount: tender.Amount);
        }

        RecalculateTenderSummary();
        TenderAmountText = string.Empty;
        SetStatus("payment.status.voucherReleased");
        NotifyPaymentCommandStates();
        return true;
    }

    private async Task RemoveTenderAsync(PaymentTender? tender)
    {
        var tenderSnapshot = tender;
        using var permissionGrant = await AuthorizeAsync(
            Permissions.PosTerminal.Payment.RemoveTender,
            "remove-tender",
            tenderSnapshot?.Method,
            tenderSnapshot?.Amount);
        if (permissionGrant is null)
        {
            return;
        }

        using var authorizationActivation = permissionGrant.Activate();
        tender = tenderSnapshot;
        if (tender is null)
        {
            return;
        }

        if (_pendingVoucherUploadOrderGuid is not null)
        {
            SetStatus("payment.status.retryVoucherUpload");
            return;
        }

        if (IsRecoveredCardAttemptTender(tender))
        {
            // 中文注释：恢复出的已批准卡款是真实扣款，不能像普通本地 tender 一样删除。
            return;
        }

        if (IsPaymentMode && tender.Method == PaymentMethodKind.Voucher)
        {
            bool released;
            try
            {
                released = await _workflowService.ReleaseVoucherTenderAsync(tender, Session);
            }
            catch (Exception ex)
            {
                var correlation = OperationAuditEvents.CreateCorrelation();
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.PaymentTenderRemove,
                    "Failed",
                    Session,
                    OperationAuditEvents.CaptureCart(_cart.Lines),
                    reasonCode: "VOUCHER_RELEASE_EXCEPTION",
                    safeMessage: ex.GetType().Name,
                    paymentMethod: tender.Method.ToString(),
                    paymentAmount: tender.Amount,
                    correlationId: correlation.CorrelationId,
                    traceId: correlation.TraceId);
                ConsoleLog.WriteError(
                    "OperationAudit",
                    $"manual voucher release failed error={ex.GetType().Name}",
                    new ApplicationLogContext(TraceId: correlation.TraceId),
                    ex);
                throw;
            }

            if (!released)
            {
                SetStatus("payment.status.voucherReleaseFailed");
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.PaymentTenderRemove,
                    "Failed",
                    Session,
                    OperationAuditEvents.CaptureCart(_cart.Lines),
                    reasonCode: "VOUCHER_RELEASE_FAILED",
                    safeMessage: StatusMessage,
                    paymentMethod: tender.Method.ToString(),
                    paymentAmount: tender.Amount);
                NotifyPaymentCommandStates();
                return;
            }

            RemoveTenderFromList(tender, "payment.status.voucherReleased");
            RecordTenderRemoved(tender);
            return;
        }

        RemoveTenderFromList(tender, "payment.status.tenderRemoved");
        RecordTenderRemoved(tender);
    }

    private void RecordTenderRemoved(PaymentTender tender)
    {
        OperationAuditEvents.RecordAction(
            _operationAuditLogger,
            OperationAuditTypes.PaymentTenderRemove,
            "Succeeded",
            Session,
            OperationAuditEvents.CaptureCart(_cart.Lines),
            reasonCode: IsRefundMode ? "REFUND" : "SALE",
            paymentMethod: tender.Method.ToString(),
            paymentAmount: tender.Amount);
    }

    private void RemoveTenderFromList(PaymentTender tender, string statusKey)
    {
        PaymentTenders.Remove(tender);
        RecalculateTenderSummary();
        TenderAmountText = string.Empty;
        SetStatus(statusKey);
        NotifyPaymentCommandStates();
    }

    private bool CanRemoveTender(PaymentTender? tender)
    {
        return tender is not null &&
            IsPaymentInteractionEnabled &&
            !_cardSession.IsActive &&
            _pendingVoucherUploadOrderGuid is null &&
            !IsRecoveredCardAttemptTender(tender);
    }

    private static bool IsRecoveredCardAttemptTender(PaymentTender tender)
    {
        return tender.Method == PaymentMethodKind.Card &&
            tender.IdempotencyKey?.StartsWith("CARD_ATTEMPT:", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task ConfirmPaymentAsync()
    {
        using var permissionGrant = await AuthorizeAsync(Permissions.PosTerminal.Payment.Confirm, "confirm-payment");
        if (permissionGrant is null)
        {
            return;
        }

        using var authorizationActivation = permissionGrant.Activate();
        if (IsPaymentInteractionLocked)
        {
            return;
        }

        if (TrySetBlockingCartIssueStatus())
        {
            ConfirmPaymentCommand.NotifyCanExecuteChanged();
            return;
        }

        if (_pendingVoucherUploadOrderGuid is not null)
        {
            await RetryPendingVoucherUploadAsync();
            return;
        }

        if (IsInstallmentPaymentEnabled)
        {
            await ConfirmInstallmentPaymentAsync();
            return;
        }

        if (TrySetOfflineVoucherRefundTenderStatus())
        {
            return;
        }

        if (!IsZeroSettlementMode && PaymentTenders.Count == 0)
        {
            SetStatus(GetNoTendersStatusKey());
            return;
        }

        if (!IsSettlementComplete())
        {
            SetStatus(GetIncompleteSettlementStatusKey());
            return;
        }

        await CompletePaymentFromTendersAsync();
    }

    private async Task CompletePaymentFromTendersAsync()
    {
        if (IsPaymentInteractionLocked)
        {
            return;
        }

        // 中文注释：自动完成和人工确认共用此入口，首次等待前同步上锁，防止同一订单并发结算。
        IsPaymentInteractionLocked = true;
        try
        {
            await CompletePaymentFromTendersCoreAsync();
        }
        finally
        {
            IsPaymentInteractionLocked = false;
        }
    }

    private async Task CompletePaymentFromTendersCoreAsync()
    {
        if (TrySetOfflineVoucherRefundTenderStatus())
        {
            return;
        }

        var before = OperationAuditEvents.CaptureCart(_cart.Lines);
        var tenderSnapshot = PaymentTenders.ToList();
        var isRefundMode = IsRefundMode;
        var operationType = isRefundMode
            ? OperationAuditTypes.ReturnRefundComplete
            : OperationAuditTypes.SaleComplete;
        var cashTenderedAmount = tenderSnapshot
            .Where(tender => tender.Method == PaymentMethodKind.Cash)
            .Sum(tender => tender.Amount);
        CashPaymentWorkflowResult result;
        try
        {
            result = await _workflowService.CompletePaymentAsync(
                _cart,
                Session,
                tenderSnapshot,
                cashTenderedAmount);
        }
        catch (PaymentUploadFailedException ex)
        {
            var correlation = OperationAuditEvents.CreateCorrelation();
            // 付款未完成时购物车没有变化，但失败审计仍必须保留完整商品和有符号金额快照。
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                operationType,
                "Failed",
                Session,
                before,
                reasonCode: "UPLOAD_FAILED",
                safeMessage: ex.GetType().Name,
                paymentMethod: FormatPaymentMethods(tenderSnapshot),
                paymentAmount: ex.TenderedAmount,
                orderGuid: ex.OrderGuid.ToString("D"),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"payment completion upload failed operation={operationType} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            _pendingVoucherUploadOrderGuid = ex.OrderGuid;
            _pendingVoucherTenderedAmount = ex.TenderedAmount;
            _pendingVoucherChangeAmount = ex.ChangeAmount;
            SetStatus("payment.status.uploadFailed", ex.Message);
            NotifyPaymentCommandStates();
            return;
        }
        catch (Exception ex)
        {
            var correlation = OperationAuditEvents.CreateCorrelation();
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                operationType,
                "Failed",
                Session,
                before,
                reasonCode: "TECHNICAL_FAILURE",
                safeMessage: ex.GetType().Name,
                paymentMethod: FormatPaymentMethods(tenderSnapshot),
                paymentAmount: tenderSnapshot.Sum(tender => tender.Amount),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"payment completion failed operation={operationType} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            throw;
        }

        OperationAuditEvents.RecordCartChange(
            _operationAuditLogger,
            operationType,
            Session,
            before,
            OperationAuditEvents.CaptureCart(_cart.Lines),
            reasonCode: isRefundMode ? "REFUND" : "SALE",
            paymentMethod: FormatPaymentMethods(tenderSnapshot),
            paymentAmount: result.TenderedAmount,
            orderGuid: result.Order.OrderGuid.ToString("D"));
        CompleteSuccessfulPayment(result);
    }

    private async Task ConfirmInstallmentPaymentAsync()
    {
        if (!ValidateInstallmentConfirmation(out var tender))
        {
            NotifyPaymentCommandStates();
            return;
        }

        IsPaymentInteractionLocked = true;
        var auditCart = OperationAuditEvents.CaptureCart(_cart.Lines);
        try
        {
            var appliedTender = GetInstallmentAppliedTender(tender);
            if (_installmentRepaymentOrder is { } repaymentOrder)
            {
                InstallmentOrderActionResult repaymentResult;
                try
                {
                    repaymentResult = await _installmentOrderService.AddRepaymentAsync(
                        new InstallmentOrderRepaymentRequest(
                            repaymentOrder.OrderId,
                            Session,
                            CreateInstallmentPaymentDraft(appliedTender)));
                }
                catch (Exception ex)
                {
                    var correlation = OperationAuditEvents.CreateCorrelation();
                    OperationAuditEvents.RecordAction(
                        _operationAuditLogger,
                        OperationAuditTypes.InstallmentRepaymentComplete,
                        "Failed",
                        Session,
                        auditCart,
                        reasonCode: "REPAYMENT",
                        safeMessage: ex.GetType().Name,
                        paymentMethod: appliedTender.Method.ToString(),
                        paymentAmount: appliedTender.Amount,
                        orderGuid: repaymentOrder.OrderId.ToString("D"),
                        correlationId: correlation.CorrelationId,
                        traceId: correlation.TraceId);
                    ConsoleLog.WriteError(
                        "InstallmentAudit",
                        $"installment repayment submission failed error={ex.GetType().Name}",
                        new ApplicationLogContext(TraceId: correlation.TraceId),
                        ex);
                    throw;
                }

                if (!repaymentResult.Succeeded)
                {
                    OperationAuditEvents.RecordAction(
                        _operationAuditLogger,
                        OperationAuditTypes.InstallmentRepaymentComplete,
                        "Failed",
                        Session,
                        auditCart,
                        reasonCode: "REPAYMENT",
                        safeMessage: repaymentResult.Message,
                        paymentMethod: appliedTender.Method.ToString(),
                        paymentAmount: appliedTender.Amount,
                        orderGuid: repaymentOrder.OrderId.ToString("D"));
                    SetStatus("payment.installment.status.actionFailed", repaymentResult.Message);
                    return;
                }

                var completedOrder = repaymentResult.Order ?? repaymentOrder;
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.InstallmentRepaymentComplete,
                    "Succeeded",
                    Session,
                    OperationAuditEvents.CaptureCart(_cart.Lines),
                    reasonCode: "REPAYMENT",
                    paymentMethod: appliedTender.Method.ToString(),
                    paymentAmount: appliedTender.Amount,
                    orderGuid: completedOrder.OrderId.ToString("D"));
                PaymentTenders.Clear();
                _installmentRepaymentOrder = completedOrder;
                TenderAmountText = string.Empty;
                RecalculateTenderSummary();
                SetStatus("payment.installment.status.repaymentRecorded", repaymentResult.Message);
                OnPropertyChanged(nameof(InstallmentOrderInfoText));
                if (_navigationActions.InstallmentOrderCreatedAsync is not null)
                {
                    // 分期补录成功后复用外层自动打印和回 POS 流程，避免收银员继续停在已完成的补录页。
                    _installmentRepaymentOrder = null;
                    IsInstallmentSwitchLocked = false;
                    IsInstallmentPaymentEnabled = false;
                    InstallmentCustomerName = string.Empty;
                    InstallmentCustomerPhone = string.Empty;
                    await _navigationActions.InstallmentOrderCreatedAsync(completedOrder);
                }

                return;
            }

            var cartSnapshot = CreateInstallmentCartSnapshot();
            if (appliedTender.Amount >= GetPaymentTargetAmount() &&
                _navigationActions.ConfirmInstallmentFullFirstPaymentAsync is { } confirmAsync &&
                !await confirmAsync())
            {
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.InstallmentRepaymentCancel,
                    "Succeeded",
                    Session,
                    auditCart,
                    reasonCode: "FULL_FIRST_PAYMENT_CANCELLED",
                    paymentMethod: appliedTender.Method.ToString(),
                    paymentAmount: appliedTender.Amount);
                // 中文注释：首付已可结清整单时先二次确认；确认后仍按分期创建，取消则保留当前收款状态。
                SetStatus("payment.installment.status.fullFirstPaymentCancelled");
                return;
            }

            InstallmentOrderCreateResult createResult;
            try
            {
                createResult = await _installmentOrderService.CreateOrderAsync(
                    new InstallmentOrderCreateRequest(
                        Session,
                        cartSnapshot,
                        InstallmentCustomerName.Trim(),
                        InstallmentCustomerPhone.Trim(),
                        appliedTender.Amount,
                        CreateInstallmentPaymentDraft(appliedTender),
                        string.Empty));
            }
            catch (Exception ex)
            {
                var correlation = OperationAuditEvents.CreateCorrelation();
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.SaleComplete,
                    "Failed",
                    Session,
                    auditCart,
                    reasonCode: "INSTALLMENT_CREATE",
                    safeMessage: ex.GetType().Name,
                    paymentMethod: $"Installment+{appliedTender.Method}",
                    paymentAmount: appliedTender.Amount,
                    correlationId: correlation.CorrelationId,
                    traceId: correlation.TraceId);
                ConsoleLog.WriteError(
                    "InstallmentAudit",
                    $"installment order submission failed error={ex.GetType().Name}",
                    new ApplicationLogContext(TraceId: correlation.TraceId),
                    ex);
                throw;
            }

            if (!createResult.Succeeded)
            {
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.SaleComplete,
                    "Failed",
                    Session,
                    auditCart,
                    reasonCode: "INSTALLMENT_CREATE",
                    safeMessage: createResult.Message,
                    paymentMethod: $"Installment+{appliedTender.Method}",
                    paymentAmount: appliedTender.Amount);
                SetStatus("payment.installment.status.actionFailed", createResult.Message);
                return;
            }

            OperationAuditEvents.RecordCartChange(
                _operationAuditLogger,
                OperationAuditTypes.SaleComplete,
                Session,
                auditCart,
                new OperationAuditCartSnapshot(0m, 0m, 0m, []),
                reasonCode: "INSTALLMENT_CREATE",
                paymentMethod: $"Installment+{appliedTender.Method}",
                paymentAmount: appliedTender.Amount,
                orderGuid: createResult.Order?.OrderId.ToString("D"));
            PaymentTenders.Clear();
            _cart.Clear();
            TenderAmountText = string.Empty;
            if (_navigationActions.InstallmentOrderCreatedAsync is not null && createResult.Order is { } createdOrder)
            {
                // 分期首单由外层统一打印小票并回到 POS，付款页不再停留在补款模式。
                _installmentRepaymentOrder = null;
                IsInstallmentSwitchLocked = false;
                IsInstallmentPaymentEnabled = false;
                InstallmentCustomerName = string.Empty;
                InstallmentCustomerPhone = string.Empty;
                RefreshCart();
                SetStatus("payment.installment.status.created", createResult.Message);
                OnPropertyChanged(nameof(InstallmentOrderInfoText));
                await _navigationActions.InstallmentOrderCreatedAsync(createdOrder);
                return;
            }

            if (createResult.Order is { CanAddRepayment: true } nextOrder)
            {
                // 中文注释：首付成功后转为原分期单补款模式，后续付款都写回同一张分期单。
                _installmentRepaymentOrder = nextOrder;
                InstallmentCustomerName = nextOrder.CustomerName;
                InstallmentCustomerPhone = nextOrder.CustomerPhone;
                IsInstallmentPaymentEnabled = true;
                IsInstallmentSwitchLocked = true;
            }
            else
            {
                _installmentRepaymentOrder = null;
                IsInstallmentSwitchLocked = false;
                IsInstallmentPaymentEnabled = false;
                InstallmentCustomerName = string.Empty;
                InstallmentCustomerPhone = string.Empty;
            }

            RefreshCart();
            SetStatus("payment.installment.status.created", createResult.Message);
            OnPropertyChanged(nameof(InstallmentOrderInfoText));
        }
        finally
        {
            IsPaymentInteractionLocked = false;
            NotifyPaymentCommandStates();
        }
    }

    private bool ValidateInstallmentConfirmation(out PaymentTender tender)
    {
        tender = null!;

        if (!Session.IsOnline)
        {
            SetStatus("payment.installment.status.onlineRequired");
            return false;
        }

        if (PaymentTenders.Count != 1)
        {
            SetStatus(GetNoTendersStatusKey());
            return false;
        }

        tender = PaymentTenders[0];
        var targetAmount = GetPaymentTargetAmount();
        if (!IsInstallmentTenderAmountAllowed(tender.Method, tender.Amount, targetAmount))
        {
            SetStatus("payment.installment.status.invalidAmount");
            return false;
        }

        if (_installmentRepaymentOrder is { } repaymentOrder)
        {
            if (!repaymentOrder.CanAddRepayment || repaymentOrder.OutstandingAmount <= 0m)
            {
                SetStatus("payment.installment.status.noRepaymentAllowed");
                return false;
            }

            return true;
        }

        if (_cart.IsEmpty || _cart.HasNonIntegerQuantity || _cart.HasZeroPriceLine)
        {
            TrySetBlockingCartIssueStatus();
            return false;
        }

        if (ActualAmount < MinimumInstallmentTotalAmount)
        {
            SetStatus("payment.installment.status.totalBelowMinimum");
            return false;
        }

        if (string.IsNullOrWhiteSpace(InstallmentCustomerName) ||
            string.IsNullOrWhiteSpace(InstallmentCustomerPhone))
        {
            SetStatus("payment.installment.status.customerRequired");
            return false;
        }

        if (GetInstallmentAppliedPaymentAmount(tender) < MinimumInstallmentFirstPaymentAmount)
        {
            SetStatus("payment.installment.status.firstPaymentBelowMinimum");
            return false;
        }

        return true;
    }

    private bool CanConfirmInstallmentPayment()
    {
        var tender = PaymentTenders.Count == 1 ? PaymentTenders[0] : null;
        if (!Session.IsOnline ||
            PaymentTenders.Count != 1 ||
            tender is null ||
            !IsInstallmentTenderAmountAllowed(tender.Method, tender.Amount, GetPaymentTargetAmount()))
        {
            return false;
        }

        if (_installmentRepaymentOrder is { } repaymentOrder)
        {
            return repaymentOrder.CanAddRepayment && repaymentOrder.OutstandingAmount > 0m;
        }

        return !_cart.IsEmpty &&
            !_cart.HasNonIntegerQuantity &&
            !_cart.HasZeroPriceLine &&
            ActualAmount >= MinimumInstallmentTotalAmount &&
            GetInstallmentAppliedPaymentAmount(tender) >= MinimumInstallmentFirstPaymentAmount &&
            !string.IsNullOrWhiteSpace(InstallmentCustomerName) &&
            !string.IsNullOrWhiteSpace(InstallmentCustomerPhone);
    }

    private async Task RetryPendingVoucherUploadAsync()
    {
        if (_pendingVoucherUploadOrderGuid is null)
        {
            return;
        }

        var before = OperationAuditEvents.CaptureCart(_cart.Lines);
        var tenderSnapshot = PaymentTenders.ToList();
        var isRefundMode = IsRefundMode;
        var operationType = isRefundMode
            ? OperationAuditTypes.ReturnRefundComplete
            : OperationAuditTypes.SaleComplete;
        try
        {
            var result = await _workflowService.RetryVoucherUploadAsync(
                _pendingVoucherUploadOrderGuid.Value,
                _cart,
                Session,
                _pendingVoucherTenderedAmount,
                _pendingVoucherChangeAmount);
            OperationAuditEvents.RecordCartChange(
                _operationAuditLogger,
                operationType,
                Session,
                before,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "UPLOAD_RETRY_SUCCEEDED",
                paymentMethod: FormatPaymentMethods(tenderSnapshot),
                paymentAmount: result.TenderedAmount,
                orderGuid: result.Order.OrderGuid.ToString("D"));
            CompleteSuccessfulPayment(result);
        }
        catch (PaymentUploadFailedException ex)
        {
            var correlation = OperationAuditEvents.CreateCorrelation();
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                operationType,
                "Failed",
                Session,
                before,
                reasonCode: "UPLOAD_RETRY_FAILED",
                safeMessage: ex.GetType().Name,
                paymentMethod: FormatPaymentMethods(tenderSnapshot),
                paymentAmount: ex.TenderedAmount,
                orderGuid: ex.OrderGuid.ToString("D"),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"payment upload retry failed operation={operationType} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            _pendingVoucherUploadOrderGuid = ex.OrderGuid;
            _pendingVoucherTenderedAmount = ex.TenderedAmount;
            _pendingVoucherChangeAmount = ex.ChangeAmount;
            SetStatus("payment.status.uploadFailed", ex.Message);
        }
        catch (Exception ex)
        {
            var correlation = OperationAuditEvents.CreateCorrelation();
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                operationType,
                "Failed",
                Session,
                before,
                reasonCode: "UPLOAD_RETRY_FAILED",
                safeMessage: ex.GetType().Name,
                paymentMethod: FormatPaymentMethods(tenderSnapshot),
                paymentAmount: _pendingVoucherTenderedAmount,
                orderGuid: _pendingVoucherUploadOrderGuid.Value.ToString("D"),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"payment upload retry technical failure operation={operationType} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            throw;
        }
    }

    private void CompleteSuccessfulPayment(CashPaymentWorkflowResult result)
    {
        _pendingVoucherUploadOrderGuid = null;
        _pendingVoucherTenderedAmount = 0m;
        _pendingVoucherChangeAmount = 0m;
        PaymentTenders.Clear();
        PendingSyncCount = result.PendingSyncCount;
        Session = result.UpdatedSession;
        RefreshCart();
        SetStatus("payment.status.completed");
        // 中文注释：支付完成后的导航、同步、自动开钱箱和打印属于后台收尾，不得继承本次提权票据。
        using (OperationAuthorizationScope.Suspend())
        {
            PaymentCompleted?.Invoke(this, new PaymentCompletedEventArgs(result.Order, result.TenderedAmount, result.ChangeAmount));
        }
    }

    private bool CanAddTender(PaymentMethodKind method, bool allowDefaultAmount)
    {
        if (IsPaymentInteractionLocked ||
            _cardSession.IsActive ||
            _cardSession.HasUnknownResult ||
            _pendingVoucherUploadOrderGuid is not null ||
            IsZeroSettlementMode ||
            (!IsInstallmentRepaymentMode && (_cart.IsEmpty || _cart.HasNonIntegerQuantity || _cart.HasZeroPriceLine)))
        {
            return false;
        }

        if (IsInstallmentPaymentEnabled && PaymentTenders.Count > 0)
        {
            return false;
        }

        if (HasTenderForMethod(method) && !(IsRefundMode && method == PaymentMethodKind.Card))
        {
            return false;
        }

        var amountText = ResolveTenderAmountText(method, allowDefaultAmount);
        if (!_workflowService.TryParseTenderedAmount(amountText, out var amount) || amount <= 0m)
        {
            return false;
        }

        var remainingAmount = PaymentStrategies[method].ResolveDefaultAmount(
            IsRefundMode,
            GetPaymentTargetAmount(),
            GetCashRemainingAmount,
            GetExternalRemainingAmount,
            GetRefundRemainingAmount);
        if (remainingAmount <= 0m)
        {
            return false;
        }

        var additionalCheck = PaymentStrategies[method].CanAddTenderAdditionalCheck(
            IsRefundMode,
            allowDefaultAmount,
            VoucherCodeText,
            GetRefundReference(method),
            remainingAmount);
        if (additionalCheck is not null)
        {
            return false;
        }

        return IsInstallmentPaymentEnabled
            ? IsInstallmentTenderAmountAllowed(method, amount, remainingAmount)
            : method == PaymentMethodKind.Cash || amount <= remainingAmount;
    }

    private bool CanConfirmPayment()
    {
        if (IsPaymentInteractionLocked || _cardSession.IsActive || _cardSession.HasUnknownResult)
        {
            return false;
        }

        if (_pendingVoucherUploadOrderGuid is not null)
        {
            return true;
        }

        if (IsInstallmentPaymentEnabled)
        {
            return CanConfirmInstallmentPayment();
        }

        return !_cart.IsEmpty &&
            !_cart.HasNonIntegerQuantity &&
            !_cart.HasZeroPriceLine &&
            (IsZeroSettlementMode || PaymentTenders.Count > 0) &&
            IsSettlementComplete();
    }

    private void RefreshCartValidationStatus()
    {
        if (TrySetBlockingCartIssueStatus())
        {
            return;
        }

        if (_statusTextOverride is null &&
            (_statusKey is "cart.status.quantityMustBeInteger" or "cart.status.zeroPriceItem" || IsModeStatusKey(_statusKey)))
        {
            SetStatus(GetReadyStatusKey());
        }
    }

    private bool TrySetBlockingCartIssueStatus()
    {
        var statusKey = GetBlockingCartIssueStatusKey();
        if (statusKey is null)
        {
            return false;
        }

        SetStatus(statusKey);
        return true;
    }

    private void RecalculateTenderSummary()
    {
        var targetAmount = GetPaymentTargetAmount();
        TotalTendered = _workflowService.CalculateTenderedAmount(PaymentTenders.ToList());
        _workflowRemainingAmount = _workflowService.CalculateRemainingAmount(targetAmount, PaymentTenders.ToList());
        RemainingAmount = Math.Abs(_workflowRemainingAmount);
        ChangeDue = IsPaymentMode &&
            PaymentTenders.Count == 0 &&
            _workflowService.TryParseTenderedAmount(TenderAmountText, out var tenderedAmount)
                ? Math.Max(0m, CashRoundingPolicy.CalculateCashChange(targetAmount, [], tenderedAmount))
                : IsPaymentMode
                    ? _workflowService.CalculateChange(PaymentTenders.ToList(), targetAmount)
                    : 0m;
        OnPropertyChanged(nameof(QuickCashAmounts));
    }

    private IReadOnlyList<QuickCashOption> BuildQuickCashAmounts()
    {
        return
        [
            new QuickCashOption(100m, "$100", "#FF2F9E6D", "White"),
            new QuickCashOption(50m, "$50", "#FFF2C94C", "#FF2E1500"),
            new QuickCashOption(20m, "$20", "#FFE45858", "White"),
            new QuickCashOption(10m, "$10", "#FF2E6BB8", "White"),
            new QuickCashOption(5m, "$5", "#FFC15AA1", "White")
        ];
    }

    private void SyncTenderAmountToRemaining(bool force = false)
    {
        if (!force && !string.IsNullOrWhiteSpace(TenderAmountText))
        {
            return;
        }

        var amount = PaymentStrategies[SelectedPaymentMethod].ResolveDefaultAmount(
            IsRefundMode,
            GetPaymentTargetAmount(),
            GetCashRemainingAmount,
            GetExternalRemainingAmount,
            GetRefundRemainingAmount);
        TenderAmountText = amount > 0m ? amount.ToString("0.00") : string.Empty;
    }

    private bool CanApplyQuickCash(QuickCashOption? option)
    {
        return option is not null && CanAddTender(PaymentMethodKind.Cash, allowDefaultAmount: true);
    }

    private string ResolveTenderAmountText(PaymentMethodKind method)
    {
        return ResolveTenderAmountText(method, allowDefaultAmount: true);
    }

    private string ResolveTenderAmountText(PaymentMethodKind method, bool allowDefaultAmount)
    {
        if (!string.IsNullOrWhiteSpace(TenderAmountText) || !allowDefaultAmount)
        {
            return TenderAmountText;
        }

        return ResolveDefaultTenderAmountText(method);
    }

    private string ResolveDefaultTenderAmountText(PaymentMethodKind method)
    {
        var amount = PaymentStrategies[method].ResolveDefaultAmount(
            IsRefundMode,
            GetPaymentTargetAmount(),
            GetCashRemainingAmount,
            GetExternalRemainingAmount,
            GetRefundRemainingAmount);
        return amount > 0m ? amount.ToString("0.00") : string.Empty;
    }

    private decimal? ResolveAttemptedPaymentAmount(string? amountText)
    {
        if (!_workflowService.TryParseTenderedAmount(amountText, out var amount) || amount <= 0m)
        {
            return null;
        }

        // UI 输入始终为正数；退款审计必须恢复 POS 现有的有符号金额语义。
        return IsRefundMode ? -Math.Abs(amount) : Math.Abs(amount);
    }

    private decimal GetExternalRemainingAmount()
    {
        var tenderedAmount = PaymentTenders.Sum(tender => tender.Amount);
        return Math.Abs(decimal.Round(GetPaymentTargetAmount() - tenderedAmount, 2, MidpointRounding.AwayFromZero));
    }

    private bool HasTenderForMethod(PaymentMethodKind method)
    {
        return PaymentTenders.Any(tender => tender.Method == method);
    }

    private bool IsOfflineVoucherRefundUnavailable(PaymentMethodKind method)
    {
        // 退款代金券需要在线发券，离线时提前拦截，避免进入待上传状态后卡住。
        return IsRefundMode &&
            method == PaymentMethodKind.Voucher &&
            !Session.IsOnline;
    }

    private bool HasOfflineVoucherRefundTender()
    {
        return IsRefundMode &&
            !Session.IsOnline &&
            PaymentTenders.Any(IsVoucherRefundTender);
    }

    private static bool IsVoucherRefundTender(PaymentTender tender)
    {
        return tender.Method == PaymentMethodKind.Voucher && tender.Amount < 0m;
    }

    private bool TrySetOfflineVoucherRefundTenderStatus()
    {
        if (!HasOfflineVoucherRefundTender())
        {
            return false;
        }

        SetOfflineVoucherRefundUnavailableStatus();
        return true;
    }

    private void SetOfflineVoucherRefundUnavailableStatus()
    {
        SetStatus("payment.refund.status.voucherOfflineUnavailable");
        NotifyPaymentCommandStates();
    }

    private void BackToPos()
    {
        if (TrySetOfflineVoucherRefundTenderStatus())
        {
            return;
        }

        if (PaymentTenders.Count > 0)
        {
            SetStatus("payment.status.removeTendersBeforeBack");
            NotifyPaymentCommandStates();
            return;
        }

        _navigationActions.BackToPos?.Invoke();
    }

    private bool CanBackToPos()
    {
        return !IsPaymentInteractionLocked &&
            !IsCardPaymentInProgress &&
            !_cardSession.HasUnknownResult &&
            !_cardSession.IsAwaitingLateResult;
    }

    private async Task ShowInstallmentCenterAsync()
    {
        using var permissionGrant = await AuthorizeAsync(Permissions.PosTerminal.Installments.View, "open-installment-center");
        if (permissionGrant is null)
        {
            return;
        }

        // 分期流程暂时独立于现有收款流程，只负责跳转到新骨架页面。
        using var authorizationActivation = permissionGrant.Activate();
        _navigationActions.ShowInstallmentCenter?.Invoke();
    }

    private Task<ViewModelAuthorizationGrant?> AuthorizeAsync(
        string permissionCode,
        string action,
        PaymentMethodKind? requestedPaymentMethod = null,
        decimal? requestedPaymentAmount = null) =>
        ViewModelOperationAuthorization.AuthorizeAsync(
            _operationAuthorizationService,
            code => TryRequirePermission(code, requestedPaymentMethod, requestedPaymentAmount),
            permissionCode,
            "payment",
            action,
            Session);

    private bool CanShowInstallmentCenter()
    {
        return IsPaymentMode &&
            !IsPaymentInteractionLocked &&
            !_cardSession.IsActive &&
            !_cardSession.HasUnknownResult &&
            !_cart.IsEmpty;
    }

    private void CancelPayment()
    {
        if (IsCardPaymentInProgress)
        {
            _cardSession.Cancel();
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.PaymentCancel,
                "Succeeded",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "CARD_MANUAL_CANCEL",
                paymentMethod: PaymentMethodKind.Card.ToString());
            return;
        }

        if (_cardSession.IsAwaitingLateResult)
        {
            _cardSession.ShouldDiscardLateResult = true;
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.PaymentCancel,
                "Succeeded",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "CARD_LATE_RESULT_DISCARD",
                paymentMethod: PaymentMethodKind.Card.ToString());
            NotifyPaymentCommandStates();
        }
    }

    private static string? FormatPaymentMethods(IReadOnlyList<PaymentTender> tenders)
    {
        var methods = tenders
            .Select(tender => tender.Method.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return methods.Length == 0 ? null : string.Join('+', methods);
    }

    private bool CanCancelPayment()
    {
        return IsCardPaymentInProgress ||
            _cardSession.IsAwaitingLateResult;
    }

    internal bool IsCurrentPaymentEntry(int paymentEntryVersion)
    {
        return paymentEntryVersion == _paymentEntryVersion;
    }

    private PaymentEntryMode CalculatePaymentMode()
    {
        if (IsInstallmentRepaymentMode)
        {
            return PaymentEntryMode.Payment;
        }

        if (ActualAmount < 0m)
        {
            return PaymentEntryMode.Refund;
        }

        if (ActualAmount == 0m)
        {
            return PaymentEntryMode.ZeroSettlement;
        }

        return PaymentEntryMode.Payment;
    }

    private decimal GetCashRemainingAmount()
    {
        return IsRefundMode
            ? new CashRoundingPolicy().NormalizeCashTender(GetRefundRemainingAmount(PaymentMethodKind.Cash))
            : CashRoundingPolicy.GetCashPayableAmount(GetPaymentTargetAmount(), PaymentTenders.ToList());
    }

    private bool IsInstallmentRepaymentMode => _installmentRepaymentOrder is not null;

    private decimal GetPaymentTargetAmount()
    {
        return _installmentRepaymentOrder?.OutstandingAmount ?? ActualAmount;
    }

    private static bool IsInstallmentTenderAmountAllowed(PaymentMethodKind method, decimal amount, decimal targetAmount)
    {
        return targetAmount > 0m &&
            amount > 0m &&
            (method == PaymentMethodKind.Cash || amount <= targetAmount);
    }

    private PaymentTender GetInstallmentAppliedTender(PaymentTender tender)
    {
        var appliedAmount = GetInstallmentAppliedPaymentAmount(tender);
        return appliedAmount == tender.Amount
            ? tender
            : tender with { Amount = appliedAmount };
    }

    private decimal GetInstallmentAppliedPaymentAmount(PaymentTender tender)
    {
        // 中文注释：现金可以超付用于找零，但分期服务只记录真正抵扣未付金额的部分。
        return tender.Method == PaymentMethodKind.Cash
            ? Math.Min(tender.Amount, GetPaymentTargetAmount())
            : tender.Amount;
    }

    private PosCartServiceSnapshot CreateInstallmentCartSnapshot()
    {
        // 中文注释：分期创建要保存商品明细，使用和分期中心相同的轻量购物车快照结构。
        return new PosCartServiceSnapshot(
            _cart.TotalAmount,
            _cart.DiscountAmount,
            _cart.ActualAmount,
            _cart.Lines.Select(line => new PosCartLineServiceSnapshot(
                line.ProductCode,
                line.ReferenceCode,
                line.DisplayName,
                line.LookupCode,
                line.ItemNumber,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.ActualAmount)).ToList());
    }

    private static InstallmentPaymentDraft CreateInstallmentPaymentDraft(PaymentTender tender)
    {
        var (reference, reservationToken) = tender.Method == PaymentMethodKind.Voucher
            ? OrderUploadService.ParseVoucherReference(tender.Reference)
            : (tender.Reference, null);

        return new InstallmentPaymentDraft(
            Guid.NewGuid(),
            tender.Method,
            tender.Amount,
            reference,
            reservationToken,
            tender.CardTransactions,
            tender.IdempotencyKey ?? (tender.Method == PaymentMethodKind.Card ? $"preauthorized-card:{Guid.NewGuid():D}" : null));
    }

    private decimal GetRefundRemainingAmount(PaymentMethodKind method)
    {
        return PaymentStrategies[method].GetRefundRemainingAmount(
            _workflowRemainingAmount,
            _ => GetNextCardRefundCapacity()?.RemainingAmount) ?? 0m;
    }

    private string? GetRefundReference(PaymentMethodKind method)
    {
        if (!IsRefundMode)
        {
            return null;
        }

        return PaymentStrategies[method].GetRefundReference(() => GetNextCardRefundCapacity()?.Reference);
    }

    private (string Reference, decimal RemainingAmount)? GetNextCardRefundCapacity()
    {
        foreach (var capacity in _cart.ReturnPaymentCapacities.Where(capacity => capacity.Method == PaymentMethodKind.Card))
        {
            var reference = ResolveOriginalCardRefundReference(capacity);
            ConsoleLog.Write(
                "CardRefund",
                $"capacity candidate originalOrder={capacity.OriginalOrderGuid?.ToString() ?? "<null>"} " +
                $"remaining={capacity.RemainingAmount:0.00} rawReference={LogValue(capacity.Reference)} " +
                $"resolvedReference={LogValue(reference)} cardTxCount={capacity.CardTransactions?.Count ?? 0} " +
                $"refundReferences={LogValue(FormatRefundReferences(capacity.CardTransactions))}");
            if (reference is null)
            {
                continue;
            }

            var existingTendered = Math.Abs(PaymentTenders
                .Where(tender => tender.Method == PaymentMethodKind.Card)
                .Where(tender => string.Equals(GetOriginalCardReference(tender.Reference), reference, StringComparison.OrdinalIgnoreCase))
                .Sum(tender => tender.Amount));
            var remainingCapacity = Math.Max(0m, capacity.RemainingAmount - existingTendered);
            var remainingReturnAmount = GetRemainingReturnAmountForCardCapacity(capacity);
            var remainingAmount = remainingReturnAmount is decimal orderLimitedAmount
                ? Math.Min(remainingCapacity, orderLimitedAmount)
                : remainingCapacity;
            ConsoleLog.Write(
                "CardRefund",
                $"capacity evaluated originalReference={LogValue(reference)} existingTendered={existingTendered:0.00} " +
                $"remainingCapacity={remainingCapacity:0.00} orderLimited={remainingReturnAmount?.ToString("0.00") ?? "<null>"} " +
                $"selectedRemaining={remainingAmount:0.00}");
            if (remainingAmount > 0m)
            {
                ConsoleLog.Write(
                    "CardRefund",
                    $"capacity selected originalReference={LogValue(reference)} remaining={remainingAmount:0.00}");
                return (reference, remainingAmount);
            }
        }

        ConsoleLog.Write("CardRefund", "no eligible card refund capacity selected");
        return null;
    }

    private decimal? GetRemainingReturnAmountForCardCapacity(OrderReturnPaymentCapacityDto capacity)
    {
        if (capacity.OriginalOrderGuid is not Guid originalOrderGuid)
        {
            return null;
        }

        var returnAmount = Math.Abs(decimal.Round(
            _cart.Lines
                .Where(line => line.IsReturnLine && line.OriginalOrderGuid == originalOrderGuid)
                .Sum(line => line.ActualAmount),
            2,
            MidpointRounding.AwayFromZero));
        if (returnAmount <= 0m)
        {
            return 0m;
        }

        var existingCardRefundsForOrder = Math.Abs(decimal.Round(
            PaymentTenders
                .Where(tender => tender.Method == PaymentMethodKind.Card)
                .Where(tender => IsCardRefundForOriginalOrder(tender.Reference, originalOrderGuid))
                .Sum(tender => tender.Amount),
            2,
            MidpointRounding.AwayFromZero));
        return Math.Max(0m, returnAmount - existingCardRefundsForOrder);
    }

    private bool IsCardRefundForOriginalOrder(string? reference, Guid originalOrderGuid)
    {
        var originalReference = GetOriginalCardReference(reference);
        if (originalReference is null)
        {
            return false;
        }

        return _cart.ReturnPaymentCapacities.Any(capacity =>
            capacity.Method == PaymentMethodKind.Card &&
            capacity.OriginalOrderGuid == originalOrderGuid &&
            string.Equals(NormalizeReference(capacity.Reference), originalReference, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetOriginalCardReference(string? reference)
    {
        return CardRefundReference.TryGetOriginalReference(reference, out var originalReference)
            ? NormalizeReference(originalReference)
            : NormalizeReference(reference);
    }

    private static string? ResolveOriginalCardRefundReference(OrderReturnPaymentCapacityDto capacity)
    {
        var reference = NormalizeReference(capacity.Reference);
        if (reference is null || RequiresLinklyRefundReference(reference))
        {
            return BuildLinklyRefundReference(capacity.CardTransactions, reference) ?? reference;
        }

        return reference;
    }

    private static bool RequiresLinklyRefundReference(string reference)
    {
        return reference.StartsWith("ANZCLOUD:", StringComparison.OrdinalIgnoreCase) ||
            reference.StartsWith($"{LinklyBackendPaymentReference.Prefix}:", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildLinklyRefundReference(
        IReadOnlyList<CardTransactionDto>? cardTransactions,
        string? fallbackReference)
    {
        foreach (var transaction in cardTransactions ?? [])
        {
            var refundReference = NormalizeReference(transaction.RefundReference);
            if (refundReference is null)
            {
                continue;
            }

            var txnRef = NormalizeReference(transaction.TxnRef) ??
                TryGetLinklyTxnRef(fallbackReference) ??
                "RFN";
            return $"ANZCLOUD:{txnRef}:{refundReference}";
        }

        return null;
    }

    private static string FormatRefundReferences(IReadOnlyList<CardTransactionDto>? cardTransactions)
    {
        var values = (cardTransactions ?? [])
            .Select(transaction => transaction.RefundReference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? string.Empty : string.Join(',', values);
    }

    private static string? TryGetLinklyTxnRef(string? reference)
    {
        var parts = reference?.Trim().Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        return parts.Length >= 2 &&
            (string.Equals(parts[0], "ANZCLOUD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parts[0], LinklyBackendPaymentReference.Prefix, StringComparison.OrdinalIgnoreCase))
                ? parts[1]
                : null;
    }

    private static string? NormalizeReference(string? reference)
    {
        return string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
    }

    private bool IsSettlementComplete()
    {
        return PaymentMode switch
        {
            PaymentEntryMode.Refund => _workflowRemainingAmount >= 0m,
            PaymentEntryMode.ZeroSettlement => true,
            _ => _workflowRemainingAmount <= 0m
        };
    }

    private bool HasBlockingCartIssue()
    {
        return _cart.HasNonIntegerQuantity || _cart.HasZeroPriceLine;
    }

    private string? GetBlockingCartIssueStatusKey()
    {
        if (_cart.HasNonIntegerQuantity)
        {
            return "cart.status.quantityMustBeInteger";
        }

        if (_cart.HasZeroPriceLine)
        {
            return "cart.status.zeroPriceItem";
        }

        return null;
    }

    private string GetScreenTitleKey()
    {
        return PaymentMode switch
        {
            PaymentEntryMode.Refund => "payment.refund.title",
            PaymentEntryMode.ZeroSettlement => "payment.zeroSettlement.title",
            _ => "payment.title"
        };
    }

    private string GetMethodTextKey(PaymentMethodKind method)
    {
        return method switch
        {
            PaymentMethodKind.Cash => IsRefundMode ? "payment.method.refundCash" : "payment.method.cash",
            PaymentMethodKind.Card => IsRefundMode ? "payment.method.refundCard" : "payment.method.card",
            PaymentMethodKind.Voucher => IsRefundMode ? "payment.method.refundVoucher" : "payment.method.voucher",
            _ => "payment.method.installment"
        };
    }

    private string GetRemainingAmountKey()
    {
        return IsRefundMode ? "payment.refund.remaining" : "payment.remaining";
    }

    private string GetConfirmPaymentKey()
    {
        return PaymentMode switch
        {
            PaymentEntryMode.Refund => "payment.refund.confirm",
            PaymentEntryMode.ZeroSettlement => "payment.zeroSettlement.confirm",
            _ => "payment.confirm"
        };
    }

    private string GetNoTendersKey()
    {
        return PaymentMode switch
        {
            PaymentEntryMode.Refund => "payment.refund.noTenders",
            PaymentEntryMode.ZeroSettlement => "payment.zeroSettlement.noTenders",
            _ => "payment.noTenders"
        };
    }

    private string GetReadyStatusKey()
    {
        return PaymentMode switch
        {
            PaymentEntryMode.Refund => "payment.refund.status.ready",
            PaymentEntryMode.ZeroSettlement => "payment.zeroSettlement.status.ready",
            _ => "payment.status.ready"
        };
    }

    private string GetNoTendersStatusKey()
    {
        return IsRefundMode ? "payment.refund.status.noTendersAdded" : "payment.status.noTendersAdded";
    }

    private string GetIncompleteSettlementStatusKey()
    {
        return IsRefundMode ? "payment.refund.status.remainingBalance" : "payment.status.remainingBalance";
    }

    private bool IsModeStatusKey(string statusKey)
    {
        return statusKey == "payment.status.ready" ||
            statusKey == "payment.refund.status.ready" ||
            statusKey == "payment.zeroSettlement.status.ready";
    }

    internal void NotifyPaymentCommandStates()
    {
        NumberInputCommand.NotifyCanExecuteChanged();
        SelectCashCommand.NotifyCanExecuteChanged();
        SelectCardCommand.NotifyCanExecuteChanged();
        SelectVoucherCommand.NotifyCanExecuteChanged();
        OpenVoucherEntryCommand.NotifyCanExecuteChanged();
        VoucherEntryKeyCommand.NotifyCanExecuteChanged();
        ConfirmVoucherEntryCommand.NotifyCanExecuteChanged();
        CancelVoucherEntryCommand.NotifyCanExecuteChanged();
        OpenInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
        SelectInstallmentCustomerFieldCommand.NotifyCanExecuteChanged();
        InstallmentCustomerKeyCommand.NotifyCanExecuteChanged();
        ConfirmInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
        CancelInstallmentCustomerDialogCommand.NotifyCanExecuteChanged();
        QuickCashCommand.NotifyCanExecuteChanged();
        RemoveTenderCommand.NotifyCanExecuteChanged();
        ConfirmPaymentCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        BackToPosCommand.NotifyCanExecuteChanged();
        ShowInstallmentCenterCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsCancelPaymentVisible));
        OnPropertyChanged(nameof(IsConfirmPaymentVisible));
    }

    internal void SetStatus(string key, string? statusText = null)
    {
        _statusKey = key;
        _statusTextOverride = statusText;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private bool TryRequirePermission(
        string permissionCode,
        PaymentMethodKind? requestedPaymentMethod = null,
        decimal? requestedPaymentAmount = null)
    {
        if ((!_enforcePermissions && _cashierSessionContext.CurrentSession is null && Session.CashierSession is null) ||
            _cashierSessionContext.RequirePermission(permissionCode, out var message))
        {
            return true;
        }

        // 中文注释：付款命令必须在执行前复核当前收银员权限。
        var (operationType, paymentMethod, paymentAmount, orderGuid) = GetDeniedAuditOperation(
            permissionCode,
            requestedPaymentMethod,
            requestedPaymentAmount);
        if (operationType is not null)
        {
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                operationType,
                "Denied",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "PERMISSION_DENIED",
                safeMessage: message,
                paymentMethod: paymentMethod,
                paymentAmount: paymentAmount,
                orderGuid: orderGuid);
        }

        SetStatus("payment.status.permissionDenied", message);
        NotifyPaymentCommandStates();
        return false;
    }

    private (string? OperationType, string? PaymentMethod, decimal? PaymentAmount, string? OrderGuid) GetDeniedAuditOperation(
        string permissionCode,
        PaymentMethodKind? requestedPaymentMethod,
        decimal? requestedPaymentAmount)
    {
        if (permissionCode == Permissions.PosTerminal.Payment.TakeCash)
        {
            return (
                OperationAuditTypes.PaymentTenderAdd,
                (requestedPaymentMethod ?? PaymentMethodKind.Cash).ToString(),
                requestedPaymentAmount,
                null);
        }

        if (permissionCode == Permissions.PosTerminal.Payment.TakeCard)
        {
            return (
                OperationAuditTypes.PaymentTenderAdd,
                (requestedPaymentMethod ?? PaymentMethodKind.Card).ToString(),
                requestedPaymentAmount,
                null);
        }

        if (permissionCode == Permissions.PosTerminal.Payment.TakeVoucher)
        {
            return (
                OperationAuditTypes.PaymentTenderAdd,
                (requestedPaymentMethod ?? PaymentMethodKind.Voucher).ToString(),
                requestedPaymentAmount,
                null);
        }

        if (permissionCode == Permissions.PosTerminal.Payment.RemoveTender)
        {
            return (
                OperationAuditTypes.PaymentTenderRemove,
                requestedPaymentMethod?.ToString(),
                requestedPaymentAmount,
                null);
        }

        if (permissionCode == Permissions.PosTerminal.Payment.Confirm)
        {
            var tenders = PaymentTenders.ToList();
            var tender = tenders.FirstOrDefault();
            if (_installmentRepaymentOrder is { } repaymentOrder)
            {
                return (
                    OperationAuditTypes.InstallmentRepaymentComplete,
                    tender?.Method.ToString(),
                    tender is null ? null : GetInstallmentAppliedPaymentAmount(tender),
                    repaymentOrder.OrderId.ToString("D"));
            }

            if (IsInstallmentPaymentEnabled)
            {
                return (
                    OperationAuditTypes.SaleComplete,
                    tender is null ? "Installment" : $"Installment+{tender.Method}",
                    tender is null ? null : GetInstallmentAppliedPaymentAmount(tender),
                    null);
            }

            decimal? paymentAmount = _pendingVoucherUploadOrderGuid is not null
                ? _pendingVoucherTenderedAmount
                : tenders.Count == 0
                    ? null
                    : tenders.Sum(item => item.Amount);
            return (
                IsRefundMode ? OperationAuditTypes.ReturnRefundComplete : OperationAuditTypes.SaleComplete,
                FormatPaymentMethods(tenders),
                paymentAmount,
                _pendingVoucherUploadOrderGuid?.ToString("D"));
        }

        return (null, null, null, null);
    }

    private static string GetTenderPermission(PaymentMethodKind method)
    {
        return method switch
        {
            PaymentMethodKind.Cash => Permissions.PosTerminal.Payment.TakeCash,
            PaymentMethodKind.Card => Permissions.PosTerminal.Payment.TakeCard,
            PaymentMethodKind.Voucher => Permissions.PosTerminal.Payment.TakeVoucher,
            _ => Permissions.PosTerminal.Payment.Confirm
        };
    }

    internal string T(string key)
    {
        return _localization?.T(key) ?? key;
    }

    private static readonly string[] LocalizedPropertyNames =
    [
        nameof(ScreenTitleText),
        nameof(OrderSummaryText),
        nameof(CurrentTenderTextLabel),
        nameof(AmountTenderedTextLabel),
        nameof(RemainingAmountText),
        nameof(ChangeDueText),
        nameof(QuickCashText),
        nameof(PaymentMethodText),
        nameof(AppliedTendersText),
        nameof(ConfirmPaymentText),
        nameof(NoTendersText),
        nameof(CashMethodText),
        nameof(CardMethodText),
        nameof(VoucherMethodText),
        nameof(InstallmentMethodText),
        nameof(InstallmentCustomerNameDisplay),
        nameof(InstallmentCustomerPhoneDisplay),
        nameof(InstallmentOrderInfoText),
        nameof(CancelText)
    ];

    private void RaiseLocalizedProperties()
    {
        foreach (var name in LocalizedPropertyNames)
        {
            OnPropertyChanged(name);
        }

        OnPropertyChanged(nameof(StatusMessage));
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RaiseLocalizedProperties();
    }

    private void CloseCardPaymentErrorOverlay()
    {
        _cardSession.CloseErrorOverlay();
    }

    private bool CanExecuteCardPaymentErrorPrimaryAction()
    {
        return _cardSession.CanExecuteErrorPrimaryAction();
    }

    private async Task ExecuteCardPaymentErrorPrimaryActionAsync()
    {
        await _cardSession.ExecuteErrorPrimaryActionAsync();
    }

    private PaymentTenderAddRequest BuildAddTenderRequest(PaymentMethodKind method)
    {
        return new PaymentTenderAddRequest(
            method,
            SelectedPaymentMethod,
            IsPaymentInteractionLocked || _cardSession.IsActive || _cardSession.HasUnknownResult,
            _pendingVoucherUploadOrderGuid is not null,
            IsRefundMode,
            IsOfflineVoucherRefundUnavailable(method),
            HasTenderForMethod(method) && !(IsRefundMode && method == PaymentMethodKind.Card),
            GetBlockingCartIssueStatusKey(),
            VoucherCodeText,
            ResolveTenderAmountText,
            ResolveDefaultTenderAmountText,
            GetRefundReference);
    }

    private bool TryApplyAddTenderPlan(
        PaymentTenderAddPlan plan,
        out string amountText,
        out string? referenceText)
    {
        if (plan.SelectedPaymentMethod is PaymentMethodKind selectedPaymentMethod)
        {
            SelectedPaymentMethod = selectedPaymentMethod;
        }

        if (!plan.ShouldProceed)
        {
            if (plan.StatusKey is not null)
            {
                // 中文注释：controller 只返回入口决策，真正的状态投影和命令刷新仍由 VM 统一处理。
                SetStatus(plan.StatusKey);
            }

            if (plan.NotifyCommandStates)
            {
                NotifyPaymentCommandStates();
            }

            amountText = string.Empty;
            referenceText = null;
            return false;
        }

        amountText = plan.AmountText ?? string.Empty;
        referenceText = plan.ReferenceText;
        return true;
    }
}

public sealed record QuickCashOption(decimal Amount, string Label, string NoteColorKey, string ForegroundColorKey);

public enum PaymentEntryMode
{
    Payment,
    Refund,
    ZeroSettlement
}
