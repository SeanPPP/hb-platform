using System.ComponentModel;
using System.Windows.Controls;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel(DataContext as SettingsViewModel);
        AttachViewModel(DataContext as SettingsViewModel);
    }

    private void LinklyCloudPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        // 密码不写入 ViewModel 的长期 string 状态，只同步是否有输入用于按钮可用性。
        _viewModel?.RaiseLinklyCloudPasswordInputChanged(!string.IsNullOrWhiteSpace(GetCurrentLinklyCloudPassword()));
    }

    private async void SaveLinklyCloudCredential_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var password = GetCurrentLinklyCloudPassword();
        try
        {
            await _viewModel.SaveLinklyCloudCredentialAsync(password);
        }
        catch (Exception ex)
        {
            // ViewModel 已经把业务错误展示到状态栏；这里记录并阻止 async void 冒泡崩溃 UI。
            ConsoleLog.WriteError(
                "Settings",
                $"save linkly cloud credential click failed error={ex.GetType().Name} message={ex.Message}",
                exception: ex);
        }
        finally
        {
            ClearLinklyCloudPasswordBoxes();
            _viewModel.RaiseLinklyCloudPasswordInputChanged(hasPassword: false);
        }
    }

    private async void PairLinklyCloud_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var password = GetCurrentLinklyCloudPassword();
        try
        {
            await _viewModel.PairLinklyCloudAsync(password);
        }
        catch (Exception ex)
        {
            // ViewModel 已经把业务错误展示到状态栏；这里记录并阻止 async void 冒泡崩溃 UI。
            ConsoleLog.WriteError(
                "Settings",
                $"pair linkly cloud click failed error={ex.GetType().Name} message={ex.Message}",
                exception: ex);
        }
        finally
        {
            ClearLinklyCloudPasswordBoxes();
            _viewModel.RaiseLinklyCloudPasswordInputChanged(hasPassword: false);
        }
    }

    private void CancelLinklyCloudPairing_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ClearLinklyCloudPasswordBoxes();
        _viewModel?.RaiseLinklyCloudPasswordInputChanged(hasPassword: false);
    }

    private void AttachViewModel(SettingsViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.LinklyCloudPasswordText) ||
            _viewModel is null ||
            !string.IsNullOrEmpty(_viewModel.LinklyCloudPasswordText))
        {
            return;
        }

        ClearLinklyCloudPasswordBoxes();
    }

    private string GetCurrentLinklyCloudPassword()
    {
        if (_viewModel?.IsLinklyCloudBackendAsyncMode == true)
        {
            return LinklyCloudBackendPasswordBox.Password;
        }

        return LinklyCloudPasswordBox.Password;
    }

    private void ClearLinklyCloudPasswordBoxes()
    {
        ClearPasswordBox(LinklyCloudPasswordBox);
        ClearPasswordBox(LinklyCloudBackendPasswordBox);
    }

    private static void ClearPasswordBox(PasswordBox passwordBox)
    {
        if (string.IsNullOrEmpty(passwordBox.Password))
        {
            return;
        }

        // 保存或取消后清空界面输入，避免敏感信息继续留在可见控件中。
        passwordBox.Clear();
    }
}
