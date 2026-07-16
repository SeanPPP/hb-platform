using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class PosTerminalView : UserControl
{
    private IInputElement? _focusBeforeAttendanceQr;

    public PosTerminalView()
    {
        InitializeComponent();
    }

    private void AttendanceQrLauncher_Click(object sender, RoutedEventArgs e)
    {
        _focusBeforeAttendanceQr = Keyboard.FocusedElement;
        AttendanceQrOverlay.Visibility = Visibility.Visible;
        AttendanceQrCloseButton.Focus();
    }

    private void AttendanceQrCloseButton_Click(object sender, RoutedEventArgs e) => CloseAttendanceQr();

    private void AttendanceQrOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, AttendanceQrOverlay))
        {
            CloseAttendanceQr();
        }
    }

    private void AttendanceQrDialog_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void AttendanceQrView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && AttendanceQrOverlay.Visibility == Visibility.Visible)
        {
            CloseAttendanceQr();
            e.Handled = true;
        }
    }

    private void CloseAttendanceQr()
    {
        AttendanceQrOverlay.Visibility = Visibility.Collapsed;
        _focusBeforeAttendanceQr?.Focus();
        _focusBeforeAttendanceQr = null;
    }

    private void CartDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid || dataGrid.SelectedItem is null)
        {
            return;
        }

        var selectedItem = dataGrid.SelectedItem;
        dataGrid.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                dataGrid.UpdateLayout();
                dataGrid.ScrollIntoView(selectedItem);
            }),
            DispatcherPriority.Background);
    }
}
