using System.Windows;
using System.Windows.Controls;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views;

public partial class CardRecoveryCenterView : UserControl
{
    public CardRecoveryCenterView()
    {
        InitializeComponent();
    }

    private async void CardRecoveryCenterView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CardRecoveryCenterViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            // async void 是 WPF Loaded 桥接点，最后一道保护避免页面加载异常终止客户端。
            ConsoleLog.WriteError(
                "CardRecoveryCenter",
                "Failed to load card recovery center.",
                exception: ex);
        }
    }
}
