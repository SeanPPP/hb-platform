using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class ApiServerSettingsViewModel : ObservableObject
{
    private readonly ApiServerSettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly IApiServerSwitchCoordinator? _switchCoordinator;

    [ObservableProperty]
    private string _serverAddressText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _restartRequired;

    public ApiServerSettingsViewModel(
        ApiServerSettingsService settingsService,
        ILocalizationService localization,
        IApiServerSwitchCoordinator? switchCoordinator = null)
    {
        _settingsService = settingsService;
        _localization = localization;
        _switchCoordinator = switchCoordinator;
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanRun);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanRun);
        UseDevelopmentAddressCommand = new RelayCommand(
            () => ServerAddressText = ApiServerSettingsService.DevelopmentApiBaseAddress,
            () => !IsBusy);
        UseReleaseAddressCommand = new RelayCommand(
            () => ServerAddressText = ApiServerSettingsService.ReleaseApiBaseAddress,
            () => !IsBusy);
    }

    public IAsyncRelayCommand TestConnectionCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand UseDevelopmentAddressCommand { get; }

    public IRelayCommand UseReleaseAddressCommand { get; }

    public void Load()
    {
        // 用户级地址要到重启后才生效；重启前再次进入页面必须保留待生效地址和阻断标记。
        if (RestartRequired)
        {
            return;
        }

        ServerAddressText = _settingsService.GetCurrentAddress();
        RestartRequired = false;
        SetStatus("settings.serverAddress.status.ready");
    }

    partial void OnServerAddressTextChanged(string value)
    {
        RaiseCommandStates();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RaiseCommandStates();
    }

    private bool CanRun()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(ServerAddressText);
    }

    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        SetStatus("settings.serverAddress.status.testing");
        try
        {
            var succeeded = await _settingsService.TestConnectionAsync(ServerAddressText, cancellationToken);
            SetStatus(succeeded
                ? "settings.serverAddress.status.testSucceeded"
                : "settings.serverAddress.status.testFailed");
        }
        catch (ArgumentException)
        {
            SetStatus("settings.serverAddress.status.invalid");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            if (_switchCoordinator is not null)
            {
                await SwitchRuntimeAsync(cancellationToken);
                return;
            }

            string normalized;
            try
            {
                normalized = ApiServerSettingsService.NormalizeAddress(ServerAddressText);
            }
            catch (ArgumentException)
            {
                SetStatus("settings.serverAddress.status.invalid");
                return;
            }

            SetStatus("settings.serverAddress.status.testing");

            // 保存前先确认目标服务在线，避免把不可用地址写入用户环境变量。
            if (!await _settingsService.TestConnectionAsync(normalized, cancellationToken))
            {
                SetStatus("settings.serverAddress.status.testFailed");
                return;
            }

            var currentAddress = _settingsService.GetCurrentAddress();
            try
            {
                _settingsService.SaveUserAddress(normalized);
            }
            catch (Exception ex) when (ex is
                ArgumentException or
                System.IO.IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                // 持久化失败不是地址校验失败，避免异步命令故障或误导用户修改合法地址。
                SetStatus("settings.serverAddress.status.saveFailed");
                return;
            }

            ServerAddressText = normalized;
            RestartRequired = !string.Equals(
                normalized,
                currentAddress,
                StringComparison.Ordinal);
            SetStatus(RestartRequired
                ? "settings.serverAddress.status.savedRestartRequired"
                : "settings.serverAddress.status.saved");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseCommandStates()
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        UseDevelopmentAddressCommand.NotifyCanExecuteChanged();
        UseReleaseAddressCommand.NotifyCanExecuteChanged();
    }

    private async Task SwitchRuntimeAsync(CancellationToken cancellationToken)
    {
        SetStatus("settings.serverAddress.status.switching");
        var result = await _switchCoordinator!.SwitchAsync(ServerAddressText, cancellationToken);
        RestartRequired = false;
        switch (result.Status)
        {
            case ApiServerSwitchStatus.Success:
                ServerAddressText = _settingsService.GetCurrentAddress();
                SetStatus("settings.serverAddress.status.switched");
                break;
            case ApiServerSwitchStatus.SameAddress:
                ServerAddressText = _settingsService.GetCurrentAddress();
                SetStatus("settings.serverAddress.status.sameAddress");
                break;
            case ApiServerSwitchStatus.Blocked:
                SetStatus(result.BlockReason ?? "settings.serverAddress.status.blocked");
                break;
            case ApiServerSwitchStatus.PostCommitFailed:
                SetStatus("settings.serverAddress.status.postCommitFailed");
                break;
            default:
                SetStatus(result.ErrorMessage?.StartsWith("settings.", StringComparison.Ordinal) == true
                    ? result.ErrorMessage
                    : "settings.serverAddress.status.preCommitFailed");
                break;
        }
    }

    private void SetStatus(string key)
    {
        StatusMessage = _localization.T(key);
    }
}
