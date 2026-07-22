using System.Windows;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;

namespace Hbpos.Client.Wpf.Services;

public interface IApplicationExitService
{
    void Exit();
}

public interface IConfirmationDialogService
{
    Task<bool> ConfirmExitApplicationAsync();

    Task<bool> ConfirmResetTestSalesDataAsync();

    Task<bool> ConfirmInstallmentFullFirstPaymentAsync();

    Task<bool> ConfirmInstallmentPickupAfterPaidOffAsync();

    Task<bool> ConfirmOrderDateRangeReuploadAsync(
        int orderCount,
        int batchCount,
        DateTime dateFrom,
        DateTime dateTo);
}

public interface IConfirmationDialogPresenter
{
    bool IsOpen { get; }

    string TitleText { get; }

    string MessageText { get; }

    string ConfirmButtonText { get; }

    string CancelButtonText { get; }

    bool IsDestructive { get; }

    IRelayCommand ConfirmCommand { get; }

    IRelayCommand CancelCommand { get; }
}

public sealed class WpfApplicationExitService : IApplicationExitService
{
    public void Exit()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.MainWindow is { } mainWindow)
        {
            mainWindow.Close();
            return;
        }

        application.Shutdown();
    }
}

public sealed class WpfConfirmationDialogService :
    ObservableObject,
    IConfirmationDialogService,
    IConfirmationDialogPresenter
{
    private readonly ILocalizationService _localization;
    private TaskCompletionSource<bool>? _completionSource;
    private string _titleKey = string.Empty;
    private string _messageKey = string.Empty;
    private string _confirmButtonKey = string.Empty;
    private object[] _messageFormatArguments = [];
    private bool _isOpen;
    private string _titleText = string.Empty;
    private string _messageText = string.Empty;
    private string _confirmButtonText = string.Empty;
    private string _cancelButtonText = string.Empty;
    private bool _isDestructive;

    public WpfConfirmationDialogService(ILocalizationService localization)
    {
        _localization = localization;
        ConfirmCommand = new RelayCommand(() => Complete(true), () => IsOpen);
        CancelCommand = new RelayCommand(() => Complete(false), () => IsOpen);
        _localization.CultureChanged += OnCultureChanged;
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (!SetProperty(ref _isOpen, value))
            {
                return;
            }

            ConfirmCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public string TitleText
    {
        get => _titleText;
        private set => SetProperty(ref _titleText, value);
    }

    public string MessageText
    {
        get => _messageText;
        private set => SetProperty(ref _messageText, value);
    }

    public string ConfirmButtonText
    {
        get => _confirmButtonText;
        private set => SetProperty(ref _confirmButtonText, value);
    }

    public string CancelButtonText
    {
        get => _cancelButtonText;
        private set => SetProperty(ref _cancelButtonText, value);
    }

    public bool IsDestructive
    {
        get => _isDestructive;
        private set => SetProperty(ref _isDestructive, value);
    }

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public Task<bool> ConfirmExitApplicationAsync() =>
        ShowAsync(
            "pos.terminal.actions.exitApplication",
            "confirmation.exit.message",
            "pos.terminal.actions.exitApplication",
            isDestructive: true);

    public Task<bool> ConfirmResetTestSalesDataAsync() =>
        ShowAsync(
            "settings.testSalesData.confirm.title",
            "settings.testSalesData.confirm.message",
            "settings.testSalesData.confirm.action",
            isDestructive: true);

    public Task<bool> ConfirmInstallmentFullFirstPaymentAsync() =>
        ShowAsync(
            "payment.installment.confirmFullFirstPayment.title",
            "payment.installment.confirmFullFirstPayment.message",
            "common.confirm",
            isDestructive: false);

    public Task<bool> ConfirmInstallmentPickupAfterPaidOffAsync() =>
        ShowAsync(
            "payment.installment.confirmPickupAfterPaidOff.title",
            "payment.installment.confirmPickupAfterPaidOff.message",
            "common.confirm",
            isDestructive: false);

    public Task<bool> ConfirmOrderDateRangeReuploadAsync(
        int orderCount,
        int batchCount,
        DateTime dateFrom,
        DateTime dateTo) =>
        ShowAsync(
            "history.reuploadRangeConfirm.title",
            "history.reuploadRangeConfirm.message",
            "history.reuploadDateRange",
            isDestructive: false,
            messageFormatArguments: [orderCount, batchCount, dateFrom, dateTo]);

    private Task<bool> ShowAsync(
        string titleKey,
        string messageKey,
        string confirmButtonKey,
        bool isDestructive,
        params object[] messageFormatArguments)
    {
        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _completionSource, completionSource, null) is not null)
        {
            // 关键逻辑：确认框不排队，已有确认时直接拒绝，避免后来的操作覆盖当前文案。
            return Task.FromResult(false);
        }

        _titleKey = titleKey;
        _messageKey = messageKey;
        _confirmButtonKey = confirmButtonKey;
        _messageFormatArguments = messageFormatArguments;
        IsDestructive = isDestructive;
        RefreshLocalizedText();
        IsOpen = true;
        return completionSource.Task;
    }

    private void Complete(bool result)
    {
        var completionSource = Interlocked.Exchange(ref _completionSource, null);
        if (completionSource is null)
        {
            return;
        }

        // 关键逻辑：先关闭展示并清空待完成引用，重复点击只能命中一次结果。
        IsOpen = false;
        completionSource.TrySetResult(result);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        if (IsOpen)
        {
            RefreshLocalizedText();
        }
    }

    private void RefreshLocalizedText()
    {
        TitleText = _localization.T(_titleKey);
        MessageText = string.Format(
            CultureInfo.CurrentCulture,
            _localization.T(_messageKey),
            _messageFormatArguments);
        ConfirmButtonText = _localization.T(_confirmButtonKey);
        CancelButtonText = _localization.T("common.cancel");
    }
}
