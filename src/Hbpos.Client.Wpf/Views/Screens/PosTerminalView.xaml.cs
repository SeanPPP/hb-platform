using System.Windows.Threading;
using System.Windows.Controls;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class PosTerminalView : UserControl
{
    public PosTerminalView()
    {
        InitializeComponent();
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
