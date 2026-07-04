using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        if (e.PropertyName != nameof(PaymentViewModel.IsVoucherEntryDialogOpen) ||
            DataContext is not PaymentViewModel { IsVoucherEntryDialogOpen: true })
        {
            return;
        }

        // 中文注释：弹窗显示后延迟聚焦，保证扫码枪输入直接进入代金券号码框。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            VoucherEntryTextBox.Focus();
            VoucherEntryTextBox.SelectAll();
        }));
    }
}
