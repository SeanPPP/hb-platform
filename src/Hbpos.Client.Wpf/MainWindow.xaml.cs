using System.Windows;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppStartupOptions _startupOptions;

    public MainWindow(MainViewModel viewModel, AppStartupOptions startupOptions)
    {
        _viewModel = viewModel;
        _startupOptions = startupOptions;
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += MainWindowLoaded;
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowLoaded;
        await _viewModel.InitializeAsync(_startupOptions);
    }
}
