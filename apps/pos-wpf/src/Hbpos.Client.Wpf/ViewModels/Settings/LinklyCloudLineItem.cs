using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Contracts.Linkly;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.ViewModels.Settings;

public sealed partial class LinklyCloudLineItem : ObservableObject
{
    private readonly Func<string, string> _translate;
    private readonly Action<LinklyCloudLineItem> _togglePairing;
    private readonly Action<LinklyCloudLineItem> _next;
    private readonly Action<LinklyCloudLineItem> _back;
    private readonly Action<LinklyCloudLineItem> _cancel;
    private readonly Func<LinklyCloudLineItem, Task> _confirmAsync;
    private readonly Func<LinklyCloudLineItem, Task> _testConnectionAsync;
    private readonly Func<LinklyCloudLineItem, Task> _selectAsync;
    private readonly Func<bool> _isParentBusy;
    private PairingTargetSnapshot? _pairingTarget;

    public LinklyCloudLineItem(
        LinklyCloudTerminalSummary terminal,
        Func<string, string> translate,
        Func<bool> isParentBusy,
        Action<LinklyCloudLineItem> togglePairing,
        Action<LinklyCloudLineItem> next,
        Action<LinklyCloudLineItem> back,
        Action<LinklyCloudLineItem> cancel,
        Func<LinklyCloudLineItem, Task> confirmAsync,
        Func<LinklyCloudLineItem, Task> testConnectionAsync,
        Func<LinklyCloudLineItem, Task> selectAsync,
        LinklyCloudTerminalManagementItem? managementItem = null,
        bool supportsManagement = false)
    {
        Terminal = terminal;
        _translate = translate;
        _isParentBusy = isParentBusy;
        _togglePairing = togglePairing;
        _next = next;
        _back = back;
        _cancel = cancel;
        _confirmAsync = confirmAsync;
        _testConnectionAsync = testConnectionAsync;
        _selectAsync = selectAsync;
        ManagementItem = managementItem;
        SupportsManagement = supportsManagement;

        TogglePairingCommand = new RelayCommand(() => _togglePairing(this), () => !IsAnyOperationBusy);
        NextCommand = new RelayCommand(() => _next(this), CanGoNext);
        BackCommand = new RelayCommand(() => _back(this), () => IsConfirming && !IsAnyOperationBusy);
        CancelCommand = new RelayCommand(() => _cancel(this), () => IsExpanded && !IsAnyOperationBusy);
        ConfirmCommand = new AsyncRelayCommand(() => _confirmAsync(this), () => IsConfirming && !IsAnyOperationBusy);
        TestConnectionCommand = new AsyncRelayCommand(() => _testConnectionAsync(this), () => !IsAnyOperationBusy && Terminal.IsReady && !string.IsNullOrWhiteSpace(Terminal.TerminalVersion));
        SelectCommand = new AsyncRelayCommand(() => _selectAsync(this), () => !IsAnyOperationBusy && Terminal.IsReady && !IsSelected);
    }

    public LinklyCloudTerminalSummary Terminal { get; private set; }

    public LinklyCloudTerminalManagementItem? ManagementItem { get; }

    public bool SupportsManagement { get; }

    public int LaneNo => Terminal.LaneNo;

    public string LaneLabel => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        _translate("settings.linkly.lines.laneLabel"),
        Terminal.LaneNo);

    public string DisplayName => Terminal.DisplayName;

    public string TogglePairingText => _translate(IsExpanded
        ? "settings.linkly.lines.collapsePairing"
        : "settings.linkly.lines.repair");

    public string PairingStatusText => _translate(Terminal.PairingState.Trim().ToLowerInvariant() switch
    {
        "ready" => "settings.linkly.lines.pairing.ready",
        "unpaired" => "settings.linkly.lines.pairing.unpaired",
        "needsrepair" or "needs-repair" => "settings.linkly.lines.pairing.needsRepair",
        _ => "settings.linkly.lines.pairing.unknown"
    });

    private string _connectionStatusKey = "settings.linkly.lines.connection.notTested";
    private string? _connectionHelpKey;

    public string ConnectionStatusText => _translate(_connectionStatusKey);

    public string ConnectionHelpText => string.IsNullOrWhiteSpace(_connectionHelpKey)
        ? string.Empty
        : _translate(_connectionHelpKey);

    public bool HasConnectionHelp => !string.IsNullOrWhiteSpace(ConnectionHelpText);

    public bool IsConnected => _connectionStatusKey == "settings.linkly.lines.connection.connected";

    public bool HasConnectionFailure =>
        _connectionStatusKey is "settings.linkly.lines.connection.failed" or "settings.linkly.lines.connection.unknown" &&
        HasConnectionHelp;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isEnteringCode;

    [ObservableProperty]
    private bool _isConfirming;

    [ObservableProperty]
    private bool _isOperationBusy;

    [ObservableProperty]
    private string _pairCode = string.Empty;

    public IRelayCommand TogglePairingCommand { get; }

    public IRelayCommand NextCommand { get; }

    public IRelayCommand BackCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    public IAsyncRelayCommand TestConnectionCommand { get; }

    public IAsyncRelayCommand SelectCommand { get; }

    internal PairingTargetSnapshot? PairingTarget => _pairingTarget;

    internal void OpenForCodeEntry()
    {
        PairCode = string.Empty;
        _pairingTarget = null;
        IsExpanded = true;
        IsEnteringCode = true;
        IsConfirming = false;
        RefreshCommandStates();
    }

    internal void BeginConfirmation(CardTerminalEnvironment environment)
    {
        if (!CanGoNext())
        {
            return;
        }

        // 确认页锁定线路身份和环境，授权等待期间也不能悄悄改成另一台刷卡机。
        _pairingTarget = new PairingTargetSnapshot(
            Terminal.TerminalId,
            environment,
            Terminal.DisplayName,
            Terminal.LaneNo,
            PairCode,
            Terminal.TerminalVersion,
            Terminal.AssignedDeviceCode,
            Terminal.AssignmentRevision);
        IsEnteringCode = false;
        IsConfirming = true;
        RefreshCommandStates();
    }

    internal void BackToCodeEntry()
    {
        _pairingTarget = null;
        IsEnteringCode = true;
        IsConfirming = false;
        RefreshCommandStates();
    }

    internal void CloseAndClear()
    {
        PairCode = string.Empty;
        _pairingTarget = null;
        IsExpanded = false;
        IsEnteringCode = false;
        IsConfirming = false;
        IsOperationBusy = false;
        RefreshCommandStates();
    }

    internal void UpdateTerminal(LinklyCloudTerminalSummary terminal, bool isSelected)
    {
        Terminal = terminal;
        IsSelected = isSelected;
        OnPropertyChanged(nameof(Terminal));
        OnPropertyChanged(nameof(LaneNo));
        OnPropertyChanged(nameof(LaneLabel));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(PairingStatusText));
        RefreshCommandStates();
    }

    internal void SetConnectionStatus(string statusKey, string? helpKey = null)
    {
        _connectionStatusKey = statusKey;
        _connectionHelpKey = helpKey;
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(ConnectionHelpText));
        OnPropertyChanged(nameof(HasConnectionHelp));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasConnectionFailure));
    }

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(LaneLabel));
        OnPropertyChanged(nameof(TogglePairingText));
        OnPropertyChanged(nameof(PairingStatusText));
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(ConnectionHelpText));
        OnPropertyChanged(nameof(HasConnectionHelp));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasConnectionFailure));
    }

    internal void RefreshCommandStates()
    {
        TogglePairingCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        SelectCommand.NotifyCanExecuteChanged();
    }

    partial void OnPairCodeChanged(string value)
    {
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(TogglePairingText));

    partial void OnIsEnteringCodeChanged(bool value) => RefreshCommandStates();

    partial void OnIsConfirmingChanged(bool value) => RefreshCommandStates();

    partial void OnIsOperationBusyChanged(bool value) => RefreshCommandStates();

    partial void OnIsSelectedChanged(bool value) => SelectCommand.NotifyCanExecuteChanged();

    private bool CanGoNext() =>
        IsEnteringCode &&
        !IsAnyOperationBusy &&
        PairCode.Length == 6 &&
        PairCode.All(char.IsAsciiDigit);

    private bool IsAnyOperationBusy => IsOperationBusy || _isParentBusy();
}

internal sealed record PairingTargetSnapshot(
    Guid TerminalId,
    CardTerminalEnvironment Environment,
    string DisplayName,
    int LaneNo,
    string PairCode,
    string? TerminalVersion,
    string? AssignedDeviceCode,
    long AssignmentRevision);
