using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class DailyCloseView : UserControl
{
    private IInputElement? _focusBeforeCashCountWorkspace;
    private IInputElement? _focusBeforeCashCountDialog;
    private IInputElement? _focusBeforeDiscardDraftConfirmation;

    public DailyCloseView()
    {
        InitializeComponent();
    }

    private void DailyCloseCashWorkspaceOverlayIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _focusBeforeCashCountWorkspace = Keyboard.FocusedElement;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => FocusDefaultButton(DailyCloseCashWorkspaceReturnButton)));
            return;
        }

        RestoreFocus(_focusBeforeCashCountWorkspace);
        _focusBeforeCashCountWorkspace = null;
    }

    private void DailyCloseCashWorkspaceOverlayPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not DailyCloseViewModel viewModel)
        {
            return;
        }

        // 中文注释：二层弹窗各自处理 Escape，主弹窗只在没有子弹窗时关闭并保留草稿。
        if (viewModel.IsCashCountDialogOpen || viewModel.IsDiscardDailyCloseDraftConfirmationOpen)
        {
            return;
        }

        if (viewModel.CloseCashCountWorkspaceCommand.CanExecute(null))
        {
            viewModel.CloseCashCountWorkspaceCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CashCountDialogOverlayIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _focusBeforeCashCountDialog = Keyboard.FocusedElement;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    // 弹窗打开后默认聚焦取消，Enter 保持安全分支，Tab 只在弹窗内循环。
                    FocusDefaultButton(CashCountDialogCancelButton);
                }));
            return;
        }

        RestoreFocus(_focusBeforeCashCountDialog);
        _focusBeforeCashCountDialog = null;
    }

    private void CashCountDialogOverlayPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not DailyCloseViewModel viewModel)
        {
            return;
        }

        if (viewModel.CancelCashCountDialogCommand.CanExecute(null))
        {
            viewModel.CancelCashCountDialogCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void DailyCloseDiscardDraftOverlayIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _focusBeforeDiscardDraftConfirmation = Keyboard.FocusedElement;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => FocusDefaultButton(DailyCloseDiscardDraftCancelButton)));
            return;
        }

        RestoreFocus(_focusBeforeDiscardDraftConfirmation);
        _focusBeforeDiscardDraftConfirmation = null;
    }

    private void DailyCloseDiscardDraftOverlayPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not DailyCloseViewModel viewModel)
        {
            return;
        }

        if (viewModel.CancelDiscardDailyCloseDraftCommand.CanExecute(null))
        {
            viewModel.CancelDiscardDailyCloseDraftCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static void FocusDefaultButton(Button button)
    {
        if (!button.IsVisible || !button.IsEnabled)
        {
            return;
        }

        button.Focus();
        Keyboard.Focus(button);
    }

    private static void RestoreFocus(IInputElement? focusTarget)
    {
        if (focusTarget is UIElement { IsVisible: true, IsEnabled: true } previousFocus)
        {
            previousFocus.Focus();
        }
    }
}
