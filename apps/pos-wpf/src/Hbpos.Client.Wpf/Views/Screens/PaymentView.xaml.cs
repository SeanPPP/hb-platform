using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class PaymentView : UserControl
{
    private INotifyPropertyChanged? _viewModelNotifications;

    public PaymentView()
    {
        InitializeComponent();
        DataContextChanged += PaymentViewDataContextChanged;
    }

    private void PaymentViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModelNotifications is not null)
        {
            _viewModelNotifications.PropertyChanged -= PaymentViewModelPropertyChanged;
        }

        _viewModelNotifications = e.NewValue as INotifyPropertyChanged;
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
