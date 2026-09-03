using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class PaymentView : UserControl
{
    private INotifyPropertyChanged? _viewModelNotifications;
    private bool _isViewLoaded;

    public PaymentView()
    {
        InitializeComponent();
        Loaded += PaymentViewLoaded;
        Unloaded += PaymentViewUnloaded;
        DataContextChanged += PaymentViewDataContextChanged;
    }

    private async void PaymentViewLoaded(object sender, RoutedEventArgs e)
    {
        if (_isViewLoaded)
        {
            return;
        }

        _isViewLoaded = true;
        AttachViewModel(DataContext as INotifyPropertyChanged);
        if (DataContext is PaymentViewModel viewModel)
        {
            try
            {
                await viewModel.RefreshLinklyCloudTerminalsAsync();
            }
            catch (Exception ex)
            {
                // 目录加载失败由 VM 展示；Loaded 事件不得让付款页崩溃。
                ConsoleLog.WriteError(
                    "Payment",
                    $"refresh linkly terminal directory on load failed error={ex.GetType().Name} message={ex.Message}",
                    exception: ex);
            }
        }
    }

    private async void LinklyCloudTerminal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not PaymentViewModel viewModel ||
            e.AddedItems.OfType<LinklyCloudTerminalSummary>().FirstOrDefault() is not { } terminal)
        {
            return;
        }

        await viewModel.SelectLinklyCloudTerminalAsync(terminal);
    }

    private async void RefreshLinklyCloudTerminals_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PaymentViewModel viewModel)
        {
            await viewModel.RefreshLinklyCloudTerminalsAsync();
        }
    }

    private void PaymentViewUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isViewLoaded && _viewModelNotifications is null)
        {
            return;
        }

        _isViewLoaded = false;
        // 缓存页仅 Hidden 时不会走到这里；真正 Unloaded 才解除强事件并清空引用。
        AttachViewModel(null);
    }

    private void PaymentViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_isViewLoaded)
        {
            AttachViewModel(e.NewValue as INotifyPropertyChanged);
        }
    }

    private void AttachViewModel(INotifyPropertyChanged? viewModelNotifications)
    {
        if (ReferenceEquals(_viewModelNotifications, viewModelNotifications))
        {
            return;
        }

        if (_viewModelNotifications is not null)
        {
            _viewModelNotifications.PropertyChanged -= PaymentViewModelPropertyChanged;
        }

        _viewModelNotifications = viewModelNotifications;
        if (_viewModelNotifications is not null)
        {
            _viewModelNotifications.PropertyChanged += PaymentViewModelPropertyChanged;
        }
    }

    private void PaymentViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not PaymentViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(PaymentViewModel.IsVoucherEntryDialogOpen) &&
            viewModel.IsVoucherEntryDialogOpen)
        {
            // 弹窗显示后延迟聚焦，保证扫码枪输入直接进入代金券号码框。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                VoucherEntryTextBox.Focus();
                VoucherEntryTextBox.SelectAll();
            }));
            return;
        }

        if (e.PropertyName == nameof(PaymentViewModel.IsInstallmentCustomerDialogOpen) &&
            viewModel.IsInstallmentCustomerDialogOpen)
        {
            FocusInstallmentCustomerDraftName();
            return;
        }

        if (e.PropertyName == nameof(PaymentViewModel.InstallmentCustomerEditTarget) &&
            viewModel.IsInstallmentCustomerDialogOpen)
        {
            if (viewModel.IsInstallmentCustomerPhoneDraftActive)
            {
                FocusInstallmentCustomerDraftPhone();
                return;
            }

            FocusInstallmentCustomerDraftName();
        }
    }

    private void InstallmentCustomerDraftNameTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is PaymentViewModel viewModel &&
            viewModel.SelectInstallmentCustomerFieldCommand.CanExecute("Name"))
        {
            viewModel.SelectInstallmentCustomerFieldCommand.Execute("Name");
        }
    }

    private void InstallmentCustomerDraftPhoneTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is PaymentViewModel viewModel &&
            viewModel.SelectInstallmentCustomerFieldCommand.CanExecute("Phone"))
        {
            viewModel.SelectInstallmentCustomerFieldCommand.Execute("Phone");
        }
    }

    private void FocusInstallmentCustomerDraftName()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            InstallmentCustomerDraftNameTextBox.Focus();
            InstallmentCustomerDraftNameTextBox.SelectAll();
        }));
    }

    private void FocusInstallmentCustomerDraftPhone()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            InstallmentCustomerDraftPhoneTextBox.Focus();
            InstallmentCustomerDraftPhoneTextBox.SelectAll();
        }));
    }
}
