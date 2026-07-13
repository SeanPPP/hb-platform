using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class DeviceRegistrationViewModel : ObservableObject
{
    private const int PendingDeviceStatus = -1;
    private static readonly TimeSpan DefaultApprovalPollingInterval = TimeSpan.FromSeconds(5);

    private readonly IDeviceRegistrationWorkflowService _workflowService;
    private readonly ILocalizationService? _localization;
    private readonly ApiServerSettingsViewModel? _apiServerSettings;
    private readonly TimeSpan _approvalPollingInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private string? _excludedStoreCode;
    private PendingRegistrationState? _pendingRegistration;
    private CancellationTokenSource? _approvalPollingCancellation;
    private CancellationTokenSource? _manualVerificationCancellation;
    private CancellationTokenSource? _registrationActionCancellation;
    private Task? _approvalPollingTask;
    private long _registrationSessionVersion;
    private bool _isReregisterCancelRequested;

    [ObservableProperty]
    private StoreSelectionItem? _selectedStore;

    [ObservableProperty]
    private string _hardwareId = string.Empty;

    [ObservableProperty]
    private string _deviceCode = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPendingRegistration;

    [ObservableProperty]
    private bool _isReregisterMode;

    [ObservableProperty]
    private bool _canCancel;

    public DeviceRegistrationViewModel(
        IDeviceApiClient deviceApiClient,
        ILocalDeviceRepository deviceRepository,
        IDeviceFingerprintService fingerprintService,
        ILocalizationService? localization = null,
        TimeSpan? approvalPollingInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        ApiServerSettingsViewModel? apiServerSettings = null)
        : this(
            new DeviceRegistrationWorkflowService(deviceApiClient, deviceRepository, fingerprintService, localization),
            localization,
            approvalPollingInterval,
            delayAsync,
            apiServerSettings)
    {
    }

    public DeviceRegistrationViewModel(
        IDeviceRegistrationWorkflowService workflowService,
        ILocalizationService? localization = null,
        TimeSpan? approvalPollingInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        ApiServerSettingsViewModel? apiServerSettings = null)
    {
        _workflowService = workflowService;
        _localization = localization;
        _apiServerSettings = apiServerSettings;
        _approvalPollingInterval = approvalPollingInterval ?? DefaultApprovalPollingInterval;
        _delayAsync = delayAsync ?? Task.Delay;
        _apiServerSettings?.Load();
        if (_localization is not null)
        {
            _localization.CultureChanged += (_, _) => RaiseLocalizedProperties();
        }

        RegisterCommand = new AsyncRelayCommand(RegisterAsync, CanRegister);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, CanVerify);
        CancelCommand = new RelayCommand(Cancel, CanExecuteCancel);
        if (_apiServerSettings is not null)
        {
            PropertyChangedEventManager.AddHandler(
                _apiServerSettings,
                OnApiServerSettingsPropertyChanged,
                nameof(ApiServerSettingsViewModel.RestartRequired));
        }
    }

    public ObservableCollection<StoreSelectionItem> Stores { get; } = [];

    public IAsyncRelayCommand RegisterCommand { get; }

    public IAsyncRelayCommand VerifyCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public ApiServerSettingsViewModel ApiServerSettings =>
        _apiServerSettings ?? throw new InvalidOperationException("API server settings are not configured.");

    internal Task? ApprovalPollingTask => _approvalPollingTask;

    public string TitleText => IsReregisterMode
        ? T("deviceRegistration.title.reregister", "Reregister Device to Another Store")
        : T("deviceRegistration.title", "Device Registration");

    public string RegisterButtonText => IsReregisterMode
        ? T("deviceRegistration.submit.reregister", "Submit Store Switch Reregistration")
        : IsPendingRegistrationStoreSwitch
            ? T("deviceRegistration.submit.switch", "Submit Store Switch Registration")
            : T("deviceRegistration.submit", "Submit Registration");

    private bool IsPendingRegistrationStoreSwitch =>
        !IsReregisterMode &&
        _pendingRegistration is not null &&
        SelectedStore is not null &&
        !string.Equals(SelectedStore.StoreCode, _pendingRegistration.StoreCode, StringComparison.OrdinalIgnoreCase);

    public event EventHandler<DeviceActivatedEventArgs>? DeviceActivated;

    public event Func<object?, DeviceActivatedEventArgs, Task>? DeviceActivatedAsync;

    public event EventHandler<DeviceReregisteredEventArgs>? DeviceReregistered;

    public event EventHandler? CancelRequested;

    public async Task InitializeAsync(LocalDeviceCache? cachedDevice, CancellationToken cancellationToken = default)
    {
        Prepare(cachedDevice);
        await LoadStoresAsync(cachedDevice, cancellationToken);
    }

    public void Prepare(LocalDeviceCache? cachedDevice)
    {
        StopApprovalPolling();
        IsReregisterMode = false;
        CanCancel = false;
        _isReregisterCancelRequested = false;
        _excludedStoreCode = null;
        HardwareId = _workflowService.GetHardwareId();
        Stores.Clear();
        SelectedStore = null;
        _pendingRegistration = null;

        if (cachedDevice is not null)
        {
            DeviceCode = cachedDevice.DeviceCode;
            HasPendingRegistration = cachedDevice.DeviceStatus == PendingDeviceStatus;
            if (cachedDevice.DeviceStatus == PendingDeviceStatus)
            {
                _pendingRegistration = new PendingRegistrationState(
                    cachedDevice.StoreCode,
                    cachedDevice.DeviceCode,
                    cachedDevice.Message ?? string.Empty);
            }
        }
        else
        {
            DeviceCode = string.Empty;
            HasPendingRegistration = false;
        }

        StatusMessage = T("deviceRegistration.status.loadingStores", DeviceRegistrationWorkflowService.LoadingStoresMessage);
        NotifyCommandState();
    }

    public void PrepareReregister(string currentStoreCode)
    {
        StopApprovalPolling();
        IsReregisterMode = true;
        CanCancel = true;
        _isReregisterCancelRequested = false;
        _excludedStoreCode = currentStoreCode;
        _pendingRegistration = null;
        HardwareId = _workflowService.GetHardwareId();
        Stores.Clear();
        SelectedStore = null;
        DeviceCode = string.Empty;
        HasPendingRegistration = false;
        StatusMessage = T("deviceRegistration.status.loadingStores", DeviceRegistrationWorkflowService.LoadingStoresMessage);
        NotifyCommandState();
    }

    public async Task LoadStoresAsync(LocalDeviceCache? cachedDevice, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = T("deviceRegistration.status.loadingStores", DeviceRegistrationWorkflowService.LoadingStoresMessage);

        try
        {
            var result = await _workflowService.LoadStoresAsync(cachedDevice, IsReregisterMode, _excludedStoreCode, cancellationToken);
            ApplyLoadResult(result);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        NotifyCommandState();
    }

    partial void OnIsReregisterModeChanged(bool value)
    {
        RaiseLocalizedProperties();
    }

    partial void OnSelectedStoreChanged(StoreSelectionItem? value)
    {
        StopApprovalPolling();
        ApplyPendingRegistrationSelection(value);
        OnPropertyChanged(nameof(RegisterButtonText));
        NotifyCommandState();
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandState();
    }

    partial void OnCanCancelChanged(bool value)
    {
        NotifyCommandState();
    }

    private async Task RegisterAsync()
    {
        if (!CanRegister())
        {
            return;
        }

        if (IsReregisterMode)
        {
            await ReregisterAsync();
            return;
        }

        await RegisterDeviceAsync();
    }

    private async Task RegisterDeviceAsync()
    {
        if (SelectedStore is null)
        {
            return;
        }

        var selectedStore = SelectedStore;
        StopApprovalPolling();
        var actionCancellation = new CancellationTokenSource();
        var actionToken = actionCancellation.Token;
        _registrationActionCancellation = actionCancellation;
        var sessionVersion = _registrationSessionVersion;
        IsBusy = true;
        try
        {
            StatusMessage = T("deviceRegistration.status.submitting", "Submitting device registration...");
            var result = await _workflowService.RegisterAsync(selectedStore, HardwareId, actionToken);
            if (!IsCurrentRegistrationAction(sessionVersion, actionCancellation, selectedStore))
            {
                return;
            }

            await PersistResultAsync(result, actionToken);
            if (!IsCurrentRegistrationAction(sessionVersion, actionCancellation, selectedStore))
            {
                return;
            }

            await ApplyActionResultAsync(result, clearDeviceCodeWhenRejected: true);
        }
        catch (OperationCanceledException) when (actionToken.IsCancellationRequested)
        {
            // 已切换门店或重置流程的注册提交不应覆盖当前注册状态。
        }
        catch (Exception ex)
        {
            if (IsCurrentRegistrationAction(sessionVersion, actionCancellation, selectedStore))
            {
                StatusMessage = ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_registrationActionCancellation, actionCancellation))
            {
                _registrationActionCancellation = null;
                actionCancellation.Dispose();
            }

            IsBusy = false;
        }
    }

    private async Task ReregisterAsync()
    {
        if (SelectedStore is null)
        {
            return;
        }

        var selectedStore = SelectedStore;
        StopApprovalPolling();
        var actionCancellation = new CancellationTokenSource();
        var actionToken = actionCancellation.Token;
        _registrationActionCancellation = actionCancellation;
        var sessionVersion = _registrationSessionVersion;
        IsBusy = true;
        try
        {
            StatusMessage = T("deviceRegistration.status.submittingReregister", "Submitting device reregistration...");
            var result = await _workflowService.ReregisterAsync(selectedStore, HardwareId, actionToken);
            if (_isReregisterCancelRequested || !IsCurrentRegistrationAction(sessionVersion, actionCancellation, selectedStore))
            {
                // 用户已放弃本次更换分店流程，忽略后台返回，避免关闭后的弹窗继续改写界面状态。
                return;
            }

            await PersistResultAsync(result, actionToken);
            if (_isReregisterCancelRequested || !IsCurrentRegistrationAction(sessionVersion, actionCancellation, selectedStore))
            {
                return;
            }

            await ApplyActionResultAsync(result);
        }
        catch (OperationCanceledException) when (actionToken.IsCancellationRequested)
        {
            // 已取消的重新注册提交不应覆盖当前授权分店。
        }
        catch (Exception ex)
        {
            if (_isReregisterCancelRequested || !IsCurrentRegistrationAction(sessionVersion, actionCancellation, selectedStore))
            {
                // 取消后不再把后台错误显示到已关闭流程，当前授权分店继续保持不变。
                return;
            }

            StatusMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_registrationActionCancellation, actionCancellation))
            {
                _registrationActionCancellation = null;
                actionCancellation.Dispose();
            }

            IsBusy = false;
        }
    }

    private async Task VerifyAsync()
    {
        if (!CanVerify() || SelectedStore is null || string.IsNullOrWhiteSpace(DeviceCode))
        {
            return;
        }

        var selectedStore = SelectedStore;
        var deviceCode = DeviceCode;
        StopApprovalPolling();
        var verificationCancellation = new CancellationTokenSource();
        var verificationToken = verificationCancellation.Token;
        _manualVerificationCancellation = verificationCancellation;
        var sessionVersion = _registrationSessionVersion;
        IsBusy = true;
        try
        {
            StatusMessage = T("deviceRegistration.status.checkingApproval", "Checking device approval...");
            var result = await _workflowService.VerifyAsync(selectedStore, deviceCode, HardwareId, verificationToken);
            if (!IsCurrentManualVerification(sessionVersion, verificationCancellation, selectedStore, deviceCode))
            {
                return;
            }

            await PersistResultAsync(result, verificationToken);
            if (!IsCurrentManualVerification(sessionVersion, verificationCancellation, selectedStore, deviceCode))
            {
                return;
            }

            await ApplyActionResultAsync(result);
        }
        catch (OperationCanceledException) when (verificationToken.IsCancellationRequested)
        {
            // 已切换门店或重置流程的手动验证不应覆盖当前注册状态。
        }
        catch (Exception ex)
        {
            if (IsCurrentManualVerification(sessionVersion, verificationCancellation, selectedStore, deviceCode))
            {
                StatusMessage = ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_manualVerificationCancellation, verificationCancellation))
            {
                _manualVerificationCancellation = null;
                verificationCancellation.Dispose();
            }

            IsBusy = false;
        }
    }

    private void ApplyLoadResult(DeviceRegistrationLoadResult result)
    {
        Stores.Clear();
        foreach (var store in result.Stores)
        {
            Stores.Add(store);
        }

        DeviceCode = result.DeviceCode;
        HasPendingRegistration = result.HasPendingRegistration;
        StatusMessage = result.StatusMessage;
        var pendingStoreCode = _pendingRegistration?.StoreCode ?? result.SelectedStore?.StoreCode;
        if (!IsReregisterMode && result.HasPendingRegistration && !string.IsNullOrWhiteSpace(pendingStoreCode))
        {
            _pendingRegistration = new PendingRegistrationState(
                pendingStoreCode,
                string.IsNullOrWhiteSpace(_pendingRegistration?.DeviceCode) ? result.DeviceCode : _pendingRegistration.DeviceCode,
                string.IsNullOrWhiteSpace(_pendingRegistration?.StatusMessage) ? result.StatusMessage : _pendingRegistration.StatusMessage);
        }

        // 重新注册必须由收银员手动选择目标分店，避免默认选中后误提交。
        SelectedStore = IsReregisterMode ? null : result.SelectedStore;
        NotifyCommandState();
    }

    private async Task ApplyActionResultAsync(
        DeviceRegistrationActionResult result,
        bool clearDeviceCodeWhenRejected = false,
        bool restartPollingWhenPending = true)
    {
        var shouldClearRejectedDeviceCode = clearDeviceCodeWhenRejected
            && !result.HasPendingRegistration
            && !result.ShouldRaiseActivated;

        if (shouldClearRejectedDeviceCode)
        {
            _pendingRegistration = null;
        }

        DeviceCode = shouldClearRejectedDeviceCode ? string.Empty : result.DeviceCode;
        HasPendingRegistration = result.HasPendingRegistration;
        StatusMessage = result.StatusMessage;
        if (!IsReregisterMode)
        {
            if (result.HasPendingRegistration)
            {
                _pendingRegistration = new PendingRegistrationState(
                    result.StoreCode,
                    result.DeviceCode,
                    result.StatusMessage);
            }
            else if (result.ShouldRaiseActivated)
            {
                _pendingRegistration = null;
            }
        }

        if (result.ShouldRaiseActivated || result.ShouldRaiseReregistered || !result.HasPendingRegistration || shouldClearRejectedDeviceCode)
        {
            StopApprovalPolling();
        }
        else if (restartPollingWhenPending)
        {
            RestartApprovalPollingIfNeeded();
        }

        if (result.ShouldRaiseReregistered)
        {
            IsReregisterMode = false;
            CanCancel = false;
            DeviceReregistered?.Invoke(
                this,
                new DeviceReregisteredEventArgs(result.DeviceCode, result.StoreCode, result.StoreName, result.HardwareId));
        }

        if (result.ShouldRaiseActivated)
        {
            var args = new DeviceActivatedEventArgs(result.DeviceCode, result.StoreCode, result.StoreName, result.HardwareId, result.AuthorizationCode ?? string.Empty);
            DeviceActivated?.Invoke(this, args);
            if (DeviceActivatedAsync is not null)
            {
                foreach (Func<object?, DeviceActivatedEventArgs, Task> handler in DeviceActivatedAsync.GetInvocationList())
                {
                    await handler(this, args);
                }
            }
        }

        OnPropertyChanged(nameof(RegisterButtonText));
        NotifyCommandState();
    }

    private bool CanRegister()
    {
        return !IsBusy &&
               SelectedStore is not null &&
               !HasPendingRegistration &&
               _apiServerSettings?.RestartRequired != true;
    }

    private bool CanVerify()
    {
        return !IsBusy &&
               SelectedStore is not null &&
               !string.IsNullOrWhiteSpace(DeviceCode) &&
               _apiServerSettings?.RestartRequired != true;
    }

    private void OnApiServerSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_apiServerSettings?.RestartRequired == true)
        {
            // 保存新服务器地址后立即停止旧地址上的注册工作，重启前不允许继续提交或验证。
            StopApprovalPolling();
        }
        else
        {
            // 用户撤销地址变更后，按现有 pending 状态恢复唯一的自动审批轮询。
            RestartApprovalPollingIfNeeded();
        }

        NotifyCommandState();
    }

    private bool CanExecuteCancel()
    {
        // 重新注册是可放弃流程，即使正在加载或提交，也允许用户退出弹窗。
        return CanCancel && (IsReregisterMode || !IsBusy);
    }

    private void ApplyPendingRegistrationSelection(StoreSelectionItem? selectedStore)
    {
        if (_pendingRegistration is null || IsReregisterMode || selectedStore is null)
        {
            return;
        }

        if (string.Equals(selectedStore.StoreCode, _pendingRegistration.StoreCode, StringComparison.OrdinalIgnoreCase))
        {
            DeviceCode = _pendingRegistration.DeviceCode;
            HasPendingRegistration = true;
            StatusMessage = _pendingRegistration.StatusMessage;
            RestartApprovalPollingIfNeeded();
            return;
        }

        StopApprovalPolling();
        DeviceCode = string.Empty;
        HasPendingRegistration = false;
        StatusMessage = T(
            "deviceRegistration.status.switchStore",
            "A different store is selected. Submit a new registration request to switch stores.");
    }

    private void NotifyCommandState()
    {
        RegisterCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(RegisterButtonText));
    }

    private string T(string key, string fallback)
    {
        return _localization?.T(key) ?? fallback;
    }

    private void RestartApprovalPollingIfNeeded()
    {
        StopApprovalPolling();
        // 新地址仅在重启后生效，等待重启期间不能重新创建仍访问旧地址的审批轮询。
        if (_apiServerSettings?.RestartRequired == true ||
            IsReregisterMode ||
            !HasPendingRegistration ||
            SelectedStore is null ||
            string.IsNullOrWhiteSpace(DeviceCode))
        {
            return;
        }

        var store = SelectedStore;
        var deviceCode = DeviceCode;
        var hardwareId = HardwareId;
        var pollingCancellation = new CancellationTokenSource();
        var sessionVersion = _registrationSessionVersion;
        _approvalPollingCancellation = pollingCancellation;
        _approvalPollingTask = PollApprovalAsync(store, deviceCode, hardwareId, pollingCancellation, sessionVersion);
    }

    private void StopApprovalPolling()
    {
        _registrationSessionVersion++;
        var pollingCancellation = _approvalPollingCancellation;
        _approvalPollingCancellation = null;
        CancelAndDispose(pollingCancellation);

        var manualVerificationCancellation = _manualVerificationCancellation;
        _manualVerificationCancellation = null;
        CancelAndDispose(manualVerificationCancellation);

        var registrationActionCancellation = _registrationActionCancellation;
        _registrationActionCancellation = null;
        CancelAndDispose(registrationActionCancellation);
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private bool IsCurrentApprovalPolling(CancellationTokenSource pollingCancellation, long sessionVersion)
    {
        // 取消令牌是协作式的，旧请求仍可能晚于新轮询返回，因此必须确认结果归属当前会话。
        return ReferenceEquals(_approvalPollingCancellation, pollingCancellation)
            && sessionVersion == _registrationSessionVersion
            && !pollingCancellation.IsCancellationRequested;
    }

    private bool IsCurrentManualVerification(
        long sessionVersion,
        CancellationTokenSource verificationCancellation,
        StoreSelectionItem selectedStore,
        string deviceCode)
    {
        return ReferenceEquals(_manualVerificationCancellation, verificationCancellation)
            && sessionVersion == _registrationSessionVersion
            && !verificationCancellation.IsCancellationRequested
            && string.Equals(SelectedStore?.StoreCode, selectedStore.StoreCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(DeviceCode, deviceCode, StringComparison.Ordinal);
    }

    private bool IsCurrentRegistrationAction(
        long sessionVersion,
        CancellationTokenSource actionCancellation,
        StoreSelectionItem selectedStore)
    {
        return ReferenceEquals(_registrationActionCancellation, actionCancellation)
            && sessionVersion == _registrationSessionVersion
            && !actionCancellation.IsCancellationRequested
            && string.Equals(SelectedStore?.StoreCode, selectedStore.StoreCode, StringComparison.OrdinalIgnoreCase);
    }

    private static Task PersistResultAsync(
        DeviceRegistrationActionResult result,
        CancellationToken cancellationToken)
    {
        return result.PersistAsync?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    private async Task PollApprovalAsync(
        StoreSelectionItem store,
        string deviceCode,
        string hardwareId,
        CancellationTokenSource pollingCancellation,
        long sessionVersion)
    {
        var cancellationToken = pollingCancellation.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 待审批注册没有服务端推送，按固定间隔轻量检查，直到后台启用或返回终态。
                    await _delayAsync(_approvalPollingInterval, cancellationToken);
                    if (!IsCurrentApprovalPolling(pollingCancellation, sessionVersion))
                    {
                        return;
                    }

                    StatusMessage = T("deviceRegistration.status.checkingApproval", "Checking device approval...");
                    var result = await _workflowService.VerifyAsync(store, deviceCode, hardwareId, cancellationToken);
                    if (!IsCurrentApprovalPolling(pollingCancellation, sessionVersion))
                    {
                        return;
                    }

                    await PersistResultAsync(result, cancellationToken);
                    if (!IsCurrentApprovalPolling(pollingCancellation, sessionVersion))
                    {
                        return;
                    }

                    await ApplyActionResultAsync(result, restartPollingWhenPending: false);
                    if (!result.HasPendingRegistration || result.ShouldRaiseActivated || result.ShouldRaiseReregistered)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!IsCurrentApprovalPolling(pollingCancellation, sessionVersion))
                    {
                        return;
                    }

                    // 单次轮询失败只提示错误，保留注册页并继续下一轮重试。
                    StatusMessage = ex.Message;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_approvalPollingCancellation, pollingCancellation))
            {
                _approvalPollingCancellation = null;
                pollingCancellation.Dispose();
            }
        }
    }

    private void Cancel()
    {
        if (CanExecuteCancel())
        {
            StopApprovalPolling();
            if (IsReregisterMode)
            {
                _isReregisterCancelRequested = true;
            }

            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record DeviceActivatedEventArgs(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    string HardwareId,
    string AuthorizationCode = "");

public sealed record DeviceReregisteredEventArgs(
    string DeviceCode,
    string StoreCode,
    string StoreName,
    string HardwareId);

internal sealed record PendingRegistrationState(
    string StoreCode,
    string DeviceCode,
    string StatusMessage);
