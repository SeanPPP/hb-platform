using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Screens;

public partial class CustomerDisplayView : UserControl
{
    private const double CompactPromotionWidthThreshold = 1280;
    private CustomerDisplayViewModel? _viewModel;

    public CustomerDisplayView()
    {
        InitializeComponent();
        Loaded += CustomerDisplayViewLoaded;
        DataContextChanged += CustomerDisplayViewDataContextChanged;
        Unloaded += CustomerDisplayViewUnloaded;
    }

    private void CustomerDisplayViewLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as CustomerDisplayViewModel);
        RefreshPromotionLayout();
    }

    private void CustomerDisplayViewDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeFromLines();
        SubscribeToViewModel(e.NewValue as CustomerDisplayViewModel);
    }

    private void SubscribeToViewModel(CustomerDisplayViewModel? viewModel)
    {
        if (_viewModel is not null || viewModel is null)
        {
            return;
        }

        _viewModel = viewModel;
        _viewModel.Lines.CollectionChanged += LinesCollectionChanged;
        ScrollLatestLineIntoView();
    }

    private void CustomerDisplayViewUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        UnsubscribeFromLines();
    }

    private void LinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollLatestLineIntoView();
    }

    private void ContentGrid_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        ApplyPromotionLayout(e.NewSize.Width);
    }

    public void RefreshPromotionLayout()
    {
        ApplyPromotionLayout(ContentGrid.ActualWidth);
    }

    private void ApplyPromotionLayout(double width)
    {
        if (UsesCompactPromotionLayout(width))
        {
            PromotionBannerRow.Height = new System.Windows.GridLength(154);
            Grid.SetRow(PromotionPanel, 0);
            Grid.SetColumn(PromotionPanel, 0);
            Grid.SetColumnSpan(PromotionPanel, 2);
            PromotionPanel.Margin = new System.Windows.Thickness(0, 0, 0, 16);
            Grid.SetColumnSpan(CartPanel, 2);
            PromotionTextPanel.Margin = new System.Windows.Thickness(28, 20, 160, 18);
            PromotionSubtitleText.FontSize = 26;
            PromotionBodyText.FontSize = 14;
            return;
        }

        PromotionBannerRow.Height = new System.Windows.GridLength(0);
        Grid.SetRow(PromotionPanel, 1);
        Grid.SetColumn(PromotionPanel, 1);
        Grid.SetColumnSpan(PromotionPanel, 1);
        PromotionPanel.Margin = new System.Windows.Thickness(18, 0, 0, 0);
        Grid.SetColumnSpan(CartPanel, 1);
        PromotionTextPanel.Margin = new System.Windows.Thickness(48, 44, 48, 44);
        PromotionSubtitleText.FontSize = 34;
        PromotionBodyText.FontSize = 16;
    }

    public static bool UsesCompactPromotionLayout(double width)
    {
        return width > 0 && width < CompactPromotionWidthThreshold;
    }

    private void ScrollLatestLineIntoView()
    {
        var latestLine = _viewModel?.Lines.LastOrDefault();
        if (latestLine is null)
        {
            return;
        }

        LineDataGrid.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                LineDataGrid.UpdateLayout();
                LineDataGrid.ScrollIntoView(latestLine);
            }),
            DispatcherPriority.Background);
    }

    private void UnsubscribeFromLines()
    {
        if (_viewModel is not null)
        {
            _viewModel.Lines.CollectionChanged -= LinesCollectionChanged;
            _viewModel = null;
        }
    }
}
