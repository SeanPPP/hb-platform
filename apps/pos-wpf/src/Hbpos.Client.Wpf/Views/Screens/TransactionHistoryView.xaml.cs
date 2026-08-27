using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class TransactionHistoryView : UserControl
{
    private TransactionHistoryViewModel? _viewModel;
    private bool _isViewLoaded;
    private bool _isScreenShown;
    private bool _orderDetailsAnnouncementPending;

    public TransactionHistoryView()
    {
        InitializeComponent();
        Loaded += TransactionHistoryViewLoaded;
        Unloaded += TransactionHistoryViewUnloaded;
        DataContextChanged += TransactionHistoryViewDataContextChanged;
        IsVisibleChanged += TransactionHistoryViewIsVisibleChanged;
    }

    private void TransactionHistoryViewLoaded(object sender, RoutedEventArgs e)
    {
        if (_isViewLoaded)
        {
            return;
        }

        _isViewLoaded = true;
        AttachViewModel(DataContext as TransactionHistoryViewModel);
    }

    private void TransactionHistoryViewUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isViewLoaded && _viewModel is null)
        {
            return;
        }

        _isViewLoaded = false;
        AttachViewModel(null);
    }

    private void TransactionHistoryViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_isViewLoaded)
        {
            AttachViewModel(e.NewValue as TransactionHistoryViewModel);
        }
    }

    private void TransactionHistoryViewIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_isViewLoaded || _viewModel is null)
        {
            return;
        }

        // 中文注释：进入界面立即刷新；离开界面停止挂单自动刷新计时。
        if (IsVisible)
        {
            ShowAttachedViewModel();
        }
        else
        {
            HideAttachedViewModel();
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
            HideAttachedViewModel();
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
            if (IsVisible)
            {
                ShowAttachedViewModel();
            }
        }

        UpdateHistoryColumnVisibility();
    }

    private void ShowAttachedViewModel()
    {
        if (_viewModel is null || _isScreenShown)
        {
            return;
        }

        // 先更新门状态，避免 VM 回调引发可见性重入时重复进入。
        _isScreenShown = true;
        _viewModel.OnScreenShown();
    }

    private void HideAttachedViewModel()
    {
        if (_viewModel is null || !_isScreenShown)
        {
            return;
        }

        // 离开时仅通知一次；Unloaded 会继续解除强事件并清空 VM 引用。
        _isScreenShown = false;
        _viewModel.OnScreenHidden();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransactionHistoryViewModel.IsOrderDetailsOpen))
        {
            if (_viewModel?.IsOrderDetailsOpen == true)
            {
                // 弹窗由隐藏态切为显示态后再聚焦，确保 Esc 和焦点循环从首个触控按钮开始生效。
                Dispatcher.BeginInvoke(
                    () =>
                    {
                        if (_viewModel?.IsOrderDetailsOpen == true && OrderDetailsCloseButton.IsVisible)
                        {
                            OrderDetailsCloseButton.Focus();
                            AnnounceOrderDetailsState();
                        }
                    },
                    DispatcherPriority.Input);
            }

            return;
        }

        if (e.PropertyName is nameof(TransactionHistoryViewModel.IsReceiptPreviewLoading) or
            nameof(TransactionHistoryViewModel.OrderDetailsErrorMessage) or
            nameof(TransactionHistoryViewModel.IsOrderDetailsEmpty))
        {
            ScheduleOrderDetailsStateAnnouncement();
            return;
        }

        if (!string.IsNullOrEmpty(e.PropertyName) &&
            e.PropertyName is not nameof(TransactionHistoryViewModel.SelectedSource) and
                not nameof(TransactionHistoryViewModel.IsStandardSourceSelected) and
                not nameof(TransactionHistoryViewModel.IsInstallmentSourceSelected))
        {
            return;
        }

        UpdateHistoryColumnVisibility();
    }

    private void ScheduleOrderDetailsStateAnnouncement()
    {
        if (_orderDetailsAnnouncementPending || _viewModel?.IsOrderDetailsOpen != true)
        {
            return;
        }

        _orderDetailsAnnouncementPending = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                _orderDetailsAnnouncementPending = false;
                if (_viewModel?.IsOrderDetailsOpen == true)
                {
                    AnnounceOrderDetailsState();
                }
            },
            DispatcherPriority.ContextIdle);
    }

    private void AnnounceOrderDetailsState()
    {
        if (_viewModel is not { IsOrderDetailsOpen: true } viewModel)
        {
            return;
        }

        var statusElement = viewModel.IsReceiptPreviewLoading
            ? OrderDetailsLoadingStatusText
            : !string.IsNullOrWhiteSpace(viewModel.OrderDetailsErrorMessage)
                ? OrderDetailsErrorStatusText
                : viewModel.IsOrderDetailsEmpty
                    ? OrderDetailsEmptyStatusText
                    : null;
        if (statusElement?.IsVisible != true)
        {
            return;
        }

        // LiveSetting 只声明播报优先级；状态切换后显式发送事件，确保屏幕阅读器及时读出结果。
        var peer = UIElementAutomationPeer.FromElement(statusElement) ??
            UIElementAutomationPeer.CreatePeerForElement(statusElement);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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

    private void OrderDetailsOverlayPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _viewModel?.IsOrderDetailsOpen != true)
        {
            return;
        }

        // 与按钮、KeyBinding 复用同一命令，Esc 只关闭明细，不改变订单选择或业务状态。
        _viewModel.CloseOrderDetailsCommand.Execute(null);
        e.Handled = true;
    }

    private void ClearSearchButtonClick(object sender, RoutedEventArgs e)
    {
        HistorySearchTextBox.Clear();

        // 清空后把焦点还给查询框，便于继续扫描或输入关键字。
        HistorySearchTextBox.Focus();
        e.Handled = true;
    }
}
