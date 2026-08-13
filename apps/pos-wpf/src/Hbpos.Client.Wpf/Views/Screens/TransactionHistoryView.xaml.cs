using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class TransactionHistoryView : UserControl
{
    private TransactionHistoryViewModel? _viewModel;

    public TransactionHistoryView()
    {
        InitializeComponent();
        DataContextChanged += TransactionHistoryViewDataContextChanged;
        IsVisibleChanged += TransactionHistoryViewIsVisibleChanged;
        AttachViewModel(DataContext as TransactionHistoryViewModel);
    }

    private void TransactionHistoryViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as TransactionHistoryViewModel);
    }

    private void TransactionHistoryViewIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // 中文注释：进入界面立即刷新；离开界面停止挂单自动刷新计时。
        if (IsVisible)
        {
            _viewModel.OnScreenShown();
        }
        else
        {
            _viewModel.OnScreenHidden();
        }
    }

    private void AttachViewModel(TransactionHistoryViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
            _viewModel.OnScreenHidden();
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
            if (IsVisible)
            {
                _viewModel.OnScreenShown();
            }
        }

        UpdateHistoryColumnVisibility();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) &&
            e.PropertyName is not nameof(TransactionHistoryViewModel.SelectedSource) and
                not nameof(TransactionHistoryViewModel.IsStandardSourceSelected) and
                not nameof(TransactionHistoryViewModel.IsInstallmentSourceSelected))
        {
            return;
        }

        UpdateHistoryColumnVisibility();
    }

    private void UpdateHistoryColumnVisibility()
    {
        var installmentVisible = _viewModel?.IsInstallmentSourceSelected == true;
        var localOrdersVisible = _viewModel?.IsLocalSourceSelected == true;
        var standardVisibility = installmentVisible ? Visibility.Collapsed : Visibility.Visible;
        var installmentVisibility = installmentVisible ? Visibility.Visible : Visibility.Collapsed;

        // DataGridColumn 不在视觉树中，列级 Binding 不能安全引用父级 DataContext；这里直接同步列显示状态。
        StandardCashierSummaryColumn.Visibility = standardVisibility;
        StandardAmountSummaryColumn.Visibility = standardVisibility;
        InstallmentCustomerSummaryColumn.Visibility = installmentVisibility;
        InstallmentAmountSummaryColumn.Visibility = installmentVisibility;
        ReuploadSelectionColumn.Visibility = localOrdersVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenHistoryRowActionsMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        // 菜单通过按钮的 DataContext 与 Tag 取得当前订单和页面命令，避免为每个动作占用独立列。
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
