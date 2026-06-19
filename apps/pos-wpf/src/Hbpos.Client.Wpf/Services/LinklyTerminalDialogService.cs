using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;

namespace Hbpos.Client.Wpf.Services;

public enum LinklyTerminalDialogMode
{
    CloudBackendInteractive,
    CloudDirectStatus
}

public sealed record LinklyTerminalDialogButton(
    string TextResourceKey,
    string Key,
    string? Data = null,
    bool IsDestructive = false);

public sealed class LinklyTerminalDialogButtonViewModel : ObservableObject
{
    private string _text;

    public LinklyTerminalDialogButtonViewModel(
        LinklyTerminalDialogButton source,
        string text)
    {
        Source = source;
        _text = text;
    }

    public LinklyTerminalDialogButton Source { get; }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public string Key => Source.Key;

    public string? Data => Source.Data;

    public bool IsDestructive => Source.IsDestructive;

    public void RefreshText(ILocalizationService localization)
    {
        Text = localization.T(Source.TextResourceKey);
    }
}

public sealed record LinklyTerminalDialogState(
    string SessionId,
    string Status,
    string? DisplayText,
    string? ReceiptText,
    string? ResponseText,
    int RecoveryCount,
    int? LastHttpStatus,
    string? Message,
    LinklyTerminalDialogMode Mode = LinklyTerminalDialogMode.CloudBackendInteractive,
    bool IsInteractive = true,
    bool IsFinal = false,
    IReadOnlyList<LinklyTerminalDialogButton>? DisplayButtons = null,
    string? InputType = null,
    string? GraphicCode = null,
    bool SupportsCancelPayment = false,
    string? ResponseCode = null);

public sealed record LinklyTerminalDialogAction(
    string Key,
    string? Data);

public static class LinklyTerminalDialogKeys
{
    public const string OkCancel = "0";
    public const string Yes = "1";
    public const string No = "2";
    public const string Auth = "3";
    public const string LocalCancel = "__LOCAL_CANCEL__";

    public static string Normalize(string key)
    {
        // Linkly REST sendkey 只接受官方数字键值；这里兼容旧窗口遗留的文本动作。
        var normalized = key.Trim();
        return normalized.ToUpperInvariant() switch
        {
            "OK" or "CANCEL" or "OKCANCEL" or "OK/CANCEL" => OkCancel,
            "YES" => Yes,
            "NO" => No,
            "AUTH" => Auth,
            _ => normalized
        };
    }
}

public interface ILinklyTerminalDialogService
{
    CancellationToken LocalCancelToken { get; }

    Task<LinklyTerminalDialogAction?> UpdateAsync(
        LinklyTerminalDialogState state,
        CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}

public interface ILinklyTerminalDialogPresenter
{
    bool IsOpen { get; }

    string TitleText { get; }

    string StatusLabelText { get; }

    string DisplayLabelText { get; }

    string ReceiptLabelText { get; }

    string? SessionId { get; }

    string StatusText { get; }

    string? DisplayText { get; }

    string? ReceiptText { get; }

    string? ResponseText { get; }

    string? ResponseCode { get; }

    string? MessageText { get; }

    bool IsInteractive { get; }

    bool IsFinal { get; }

    bool IsCloseButtonVisible { get; }

    bool IsCancelPaymentVisible { get; }

    string CancelPaymentText { get; }

    ObservableCollection<LinklyTerminalDialogButtonViewModel> DisplayButtons { get; }

    IRelayCommand<LinklyTerminalDialogButtonViewModel> SubmitActionCommand { get; }

    IRelayCommand CancelPaymentCommand { get; }

    IAsyncRelayCommand CloseCommand { get; }
}

public sealed class WpfLinklyTerminalDialogService :
    ObservableObject,
    ILinklyTerminalDialogService,
    ILinklyTerminalDialogPresenter
{
    private readonly ILocalizationService _localization;
    // 这个 CTS 会被 UI 线程和后台轮询线程同时访问，替换必须串行化，避免读到已释放实例。
    private readonly object _localCancelGate = new();
    private LinklyTerminalDialogAction? _pendingAction;
    private CancellationTokenSource _localCancelCts = new();
    private bool _isOpen;
    private string? _sessionId;
    private string _statusText = string.Empty;
    private string? _displayText;
    private string? _receiptText;
    private string? _responseText;
    private string? _responseCode;
    private string? _messageText;
    private bool _isInteractive;
    private bool _isFinal;
    private bool _supportsCancelPayment;
    private LinklyTerminalDialogMode _mode = LinklyTerminalDialogMode.CloudBackendInteractive;

    public WpfLinklyTerminalDialogService(ILocalizationService localization)
    {
        _localization = localization;
        SubmitActionCommand = new RelayCommand<LinklyTerminalDialogButtonViewModel>(
            SubmitAction,
            button => IsOpen && button is not null);
        CancelPaymentCommand = new RelayCommand(
            SubmitCancelPayment,
            () => IsCancelPaymentVisible);
        CloseCommand = new AsyncRelayCommand(CloseOrCancelAsync);
        _localization.CultureChanged += (_, _) => RaiseLocalizedTextChanged();
        RaiseLocalizedTextChanged();
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetProperty(ref _isOpen, value))
            {
                RaiseDialogButtonStateChanged();
            }
        }
    }

    public string TitleText => _localization.T("linkly.backend.dialog.title");

    public string StatusLabelText => _localization.T("linkly.backend.dialog.status");

    public string DisplayLabelText => _localization.T("linkly.backend.dialog.display");

    public string ReceiptLabelText => _localization.T("linkly.backend.dialog.receipt");

    public string? SessionId
    {
        get => _sessionId;
        private set => SetProperty(ref _sessionId, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? DisplayText
    {
        get => _displayText;
        private set => SetProperty(ref _displayText, value);
    }

    public string? ReceiptText
    {
        get => _receiptText;
        private set => SetProperty(ref _receiptText, value);
    }

    public string? ResponseText
    {
        get => _responseText;
        private set => SetProperty(ref _responseText, value);
    }

    public string? ResponseCode
    {
        get => _responseCode;
        private set => SetProperty(ref _responseCode, value);
    }

    public string? MessageText
    {
        get => _messageText;
        private set => SetProperty(ref _messageText, value);
    }

    public bool IsInteractive
    {
        get => _isInteractive;
        private set
        {
            if (SetProperty(ref _isInteractive, value))
            {
                RaiseDialogButtonStateChanged();
            }
        }
    }

    public bool IsFinal
    {
        get => _isFinal;
        private set
        {
            if (SetProperty(ref _isFinal, value))
            {
                RaiseDialogButtonStateChanged();
            }
        }
    }

    public bool IsCloseButtonVisible => IsOpen;

    public bool IsCancelPaymentVisible =>
        IsOpen &&
        IsInteractive &&
        !IsFinal &&
        !HasApprovedTerminalOutcome &&
        (_mode == LinklyTerminalDialogMode.CloudDirectStatus || _supportsCancelPayment);

    public string CancelPaymentText => _localization.T("linkly.backend.dialog.button.cancelPayment");

    public CancellationToken LocalCancelToken
    {
        get
        {
            lock (_localCancelGate)
            {
                return _localCancelCts.Token;
            }
        }
    }

    public ObservableCollection<LinklyTerminalDialogButtonViewModel> DisplayButtons { get; } = [];

    public IRelayCommand<LinklyTerminalDialogButtonViewModel> SubmitActionCommand { get; }

    public IRelayCommand CancelPaymentCommand { get; }

    public IAsyncRelayCommand CloseCommand { get; }

    public Task<LinklyTerminalDialogAction?> UpdateAsync(
        LinklyTerminalDialogState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return Task.FromResult(UpdateOnUiThread(state));
        }

        return dispatcher.InvokeAsync(() => UpdateOnUiThread(state)).Task;
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            CloseOnUiThread();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(CloseOnUiThread).Task;
    }

    private LinklyTerminalDialogAction? UpdateOnUiThread(LinklyTerminalDialogState state)
    {
        if (!string.Equals(SessionId, state.SessionId, StringComparison.Ordinal))
        {
            ResetLocalCancelToken();
        }

        _mode = state.Mode;
        IsOpen = true;
        SessionId = state.SessionId;
        StatusText = state.Status;
        DisplayText = state.DisplayText;
        ReceiptText = state.ReceiptText;
        ResponseText = state.ResponseText;
        ResponseCode = state.ResponseCode;
        MessageText = state.Message;
        IsInteractive = state.IsInteractive;
        IsFinal = state.IsFinal;
        _supportsCancelPayment = state.SupportsCancelPayment;

        DisplayButtons.Clear();
        foreach (var button in state.DisplayButtons ?? [])
        {
            DisplayButtons.Add(new LinklyTerminalDialogButtonViewModel(
                button,
                _localization.T(button.TextResourceKey)));
        }

        RaiseDialogButtonStateChanged();

        // 页面弹窗和轮询线程之间只传递一次待发送按键，避免重复 sendkey。
        var action = _pendingAction;
        _pendingAction = null;
        SubmitActionCommand.NotifyCanExecuteChanged();
        return action;
    }

    private void SubmitAction(LinklyTerminalDialogButtonViewModel? button)
    {
        if (button is null || string.IsNullOrWhiteSpace(button.Key))
        {
            return;
        }

        _pendingAction = new LinklyTerminalDialogAction(button.Key, button.Data);
    }

    private void SubmitCancelPayment()
    {
        if (!IsCancelPaymentVisible)
        {
            return;
        }
        // backend async 取消必须发送 Linkly OK/CANCEL sendkey，让终端真正退出刷卡流程。
        _pendingAction = new LinklyTerminalDialogAction(LinklyTerminalDialogKeys.OkCancel, null);
    }

    private bool HasApprovedTerminalOutcome =>
        IsApprovedResponseCode(ResponseCode) ||
        ContainsApprovedTerminalOutcome(ResponseText) ||
        ContainsApprovedTerminalOutcome(DisplayText) ||
        ContainsApprovedTerminalOutcome(ReceiptText);

    private static bool IsApprovedResponseCode(string? value)
    {
        // Linkly 批准码 00 和签名批准码 08 都表示交易已通过，不能再允许 POS 取消。
        var normalized = value?.Trim();
        return string.Equals(normalized, "00", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "08", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsApprovedTerminalOutcome(string? value)
    {
        // 后端异步轮询可能仍显示 Pending，但终端已给出 APPROVED；此时 POS 不能再提供取消入口。
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IsApprovedTerminalText(value) ||
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(IsApprovedTerminalText);
    }

    private static bool IsApprovedTerminalText(string value)
    {
        var normalized = value.Trim();
        return string.Equals(normalized, "APPROVED", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("APPROVED ", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("APPROVED(", StringComparison.OrdinalIgnoreCase);
    }

    private Task CloseOrCancelAsync()
    {
        if (IsCancelPaymentVisible)
        {
            // 交给统一取消逻辑；backend async 会发官方 OK/CANCEL sendkey。
            SubmitCancelPayment();
            return Task.CompletedTask;
        }

        return CloseAsync(CancellationToken.None);
    }

    private void CloseOnUiThread()
    {
        _pendingAction = null;
        ResetLocalCancelToken();
        _mode = LinklyTerminalDialogMode.CloudBackendInteractive;
        _supportsCancelPayment = false;
        IsOpen = false;
        SessionId = null;
        StatusText = string.Empty;
        DisplayText = null;
        ReceiptText = null;
        ResponseText = null;
        ResponseCode = null;
        MessageText = null;
        IsInteractive = false;
        IsFinal = false;
        DisplayButtons.Clear();
        SubmitActionCommand.NotifyCanExecuteChanged();
        RaiseDialogButtonStateChanged();
    }

    private void ResetLocalCancelToken()
    {
        lock (_localCancelGate)
        {
            if (!_localCancelCts.IsCancellationRequested)
            {
                return;
            }

            _localCancelCts.Dispose();
            _localCancelCts = new CancellationTokenSource();
        }
    }

    private void RaiseLocalizedTextChanged()
    {
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(StatusLabelText));
        OnPropertyChanged(nameof(DisplayLabelText));
        OnPropertyChanged(nameof(ReceiptLabelText));
        OnPropertyChanged(nameof(CancelPaymentText));
        foreach (var button in DisplayButtons)
        {
            button.RefreshText(_localization);
        }
    }

    private void RaiseDialogButtonStateChanged()
    {
        OnPropertyChanged(nameof(IsCloseButtonVisible));
        OnPropertyChanged(nameof(IsCancelPaymentVisible));
        CancelPaymentCommand.NotifyCanExecuteChanged();
    }
}
