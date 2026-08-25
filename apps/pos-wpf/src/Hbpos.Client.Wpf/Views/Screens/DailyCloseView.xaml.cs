using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class DailyCloseView : UserControl
{
    private IInputElement? _focusBeforeCashCountDialog;

    public DailyCloseView()
    {
        InitializeComponent();
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
                    if (!CashCountDialogCancelButton.IsVisible || !CashCountDialogCancelButton.IsEnabled)
                    {
                        return;
                    }

                    // 弹窗打开后默认聚焦取消，Enter 保持安全分支，Tab 只在弹窗内循环。
                    CashCountDialogCancelButton.Focus();
                    Keyboard.Focus(CashCountDialogCancelButton);
                }));
            return;
        }

        if (_focusBeforeCashCountDialog is UIElement { IsVisible: true } previousFocus)
        {
            previousFocus.Focus();
        }

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
}
