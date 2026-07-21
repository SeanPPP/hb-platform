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
        AttachViewModel(DataContext as TransactionHistoryViewModel);
    }

    private void TransactionHistoryViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as TransactionHistoryViewModel);
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
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
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
        StandardCashierColumn.Visibility = standardVisibility;
        StandardItemsColumn.Visibility = standardVisibility;
        StandardAmountColumn.Visibility = standardVisibility;
        StandardPaymentColumn.Visibility = standardVisibility;
        InstallmentCustomerColumn.Visibility = installmentVisibility;
        InstallmentPhoneColumn.Visibility = installmentVisibility;
        InstallmentTotalColumn.Visibility = installmentVisibility;
        InstallmentOutstandingColumn.Visibility = installmentVisibility;
        InstallmentPaidColumn.Visibility = installmentVisibility;
        ReuploadSelectionColumn.Visibility = localOrdersVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
