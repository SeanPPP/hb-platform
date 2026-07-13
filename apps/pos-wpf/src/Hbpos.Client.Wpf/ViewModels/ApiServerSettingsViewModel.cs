using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class ApiServerSettingsViewModel : ObservableObject
{
    private readonly ApiServerSettingsService _settingsService;
    private readonly ILocalizationService _localization;

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
        ILocalizationService localization)
    {
        _settingsService = settingsService;
        _localization = localization;
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanRun);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanRun);
    }

    public IAsyncRelayCommand TestConnectionCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public void Load()
    {
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
            var normalized = ApiServerSettingsService.NormalizeAddress(ServerAddressText);
            SetStatus("settings.serverAddress.status.testing");

            // 保存前先确认目标服务在线，避免把不可用地址写入用户环境变量。
            if (!await _settingsService.TestConnectionAsync(normalized, cancellationToken))
            {
                SetStatus("settings.serverAddress.status.testFailed");
                return;
            }

            var currentAddress = _settingsService.GetCurrentAddress();
            _settingsService.SaveUserAddress(normalized);
            ServerAddressText = normalized;
            RestartRequired = !string.Equals(
                normalized,
                currentAddress,
                StringComparison.OrdinalIgnoreCase);
            SetStatus(RestartRequired
                ? "settings.serverAddress.status.savedRestartRequired"
                : "settings.serverAddress.status.saved");
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

    private void RaiseCommandStates()
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string key)
    {
        StatusMessage = _localization.T(key);
    }
}
