using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

[Collection(WpfViewLifecycleTestCollection.Name)]
public sealed class CartSwipeRevealBehaviorTests
{
    [Fact]
    public void Public_surface_is_limited_to_the_is_enabled_attached_property()
    {
        var publicMembers = typeof(CartSwipeRevealBehavior)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is MemberTypes.Field or MemberTypes.Method)
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["GetIsEnabled", "IsEnabledProperty", "SetIsEnabled"],
            publicMembers);
        Assert.Equal("IsEnabled", CartSwipeRevealBehavior.IsEnabledProperty.Name);
        Assert.Equal(typeof(bool), CartSwipeRevealBehavior.IsEnabledProperty.PropertyType);
        Assert.Equal(typeof(CartSwipeRevealBehavior), CartSwipeRevealBehavior.IsEnabledProperty.OwnerType);
        Assert.False((bool)CartSwipeRevealBehavior.IsEnabledProperty.DefaultMetadata.DefaultValue!);
    }

    [Theory]
    [InlineData(0d, 0d, 0)]
    [InlineData(11.9d, 0d, 0)]
    [InlineData(-12d, 0d, 1)]
    [InlineData(30d, 10d, 1)]
    [InlineData(10d, 12d, 2)]
    [InlineData(13d, 12d, 2)]
    [InlineData(40d, 40d, 2)]
    public void Resolve_axis_waits_for_the_threshold_and_protects_vertical_scrolling(
        double horizontal,
        double vertical,
        int expected)
    {
        Assert.Equal((CartSwipeGestureAxis)expected, CartSwipeRevealBehavior.ResolveAxis(horizontal, vertical));
    }

    [Theory]
    [InlineData(0d, -30d, 88d, -30d)]
    [InlineData(-30d, -80d, 88d, -88d)]
    [InlineData(-88d, 20d, 88d, -68d)]
    [InlineData(0d, 20d, 88d, 0d)]
    public void Clamp_offset_keeps_the_row_between_closed_and_fully_revealed(
        double currentOffset,
        double delta,
        double revealWidth,
        double expected)
    {
        Assert.Equal(expected, CartSwipeRevealBehavior.ClampOffset(currentOffset, delta, revealWidth));
    }

    [Theory]
    [InlineData(0d, false)]
    [InlineData(-43.99d, false)]
    [InlineData(-44d, true)]
    [InlineData(-88d, true)]
    public void Reveal_requires_at_least_half_of_the_delete_action(double offset, bool expected)
    {
        Assert.Equal(expected, CartSwipeRevealBehavior.ShouldReveal(offset, 88d));
    }

    [Theory]
    [InlineData(true, 0d, -88d, true)]
    [InlineData(false, 0d, -88d, false)]
    [InlineData(true, -88d, -88d, false)]
    public void Transition_animation_respects_system_animation_setting(
        bool clientAreaAnimationEnabled,
        double currentOffset,
        double targetOffset,
        bool expected)
    {
        Assert.Equal(
            expected,
            CartSwipeRevealBehavior.ShouldAnimateTransition(
                clientAreaAnimationEnabled,
                currentOffset,
                targetOffset));
    }

    [Fact]
    public async Task Opening_a_row_closes_the_previous_row_and_recycling_resets_translation()
    {
        await RunOnStaDispatcherAsync(() =>
        {
            var dataGrid = CreateDataGrid();
            var window = new Window
            {
                Width = 600,
                Height = 320,
                Content = dataGrid,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };

            try
            {
                window.Show();
                dataGrid.UpdateLayout();
                PumpDispatcher();

                var firstRow = Assert.IsType<DataGridRow>(dataGrid.ItemContainerGenerator.ContainerFromIndex(0));
                var secondRow = Assert.IsType<DataGridRow>(dataGrid.ItemContainerGenerator.ContainerFromIndex(1));

                CartSwipeRevealBehavior.SetRevealState(dataGrid, firstRow, isRevealed: true, animate: false);
                Assert.Equal(-88d, ResolveSwipeTranslation(firstRow).X);

                CartSwipeRevealBehavior.SetRevealState(dataGrid, secondRow, isRevealed: true, animate: false);
                Assert.Equal(0d, ResolveSwipeTranslation(firstRow).X);
                Assert.Equal(-88d, ResolveSwipeTranslation(secondRow).X);

                CartSwipeRevealBehavior.ResetRowVisual(secondRow);
                Assert.Equal(0d, ResolveSwipeTranslation(secondRow).X);
                Assert.False(ResolveSwipeTranslation(secondRow).HasAnimatedProperties);
            }
            finally
            {
                CartSwipeRevealBehavior.SetIsEnabled(dataGrid, false);
                window.Content = null;
                window.Close();
                PumpDispatcher();
            }
        });
    }

    private static DataGrid CreateDataGrid()
    {
        const string templateXaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             TargetType="{x:Type DataGridRow}"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid ClipToBounds="True">
                    <Button x:Name="PART_SwipeDeleteAction"
                            Width="88"
                            HorizontalAlignment="Right" />
                    <Border x:Name="PART_SwipeContent"
                            Background="White">
                        <Border.RenderTransform>
                            <TranslateTransform />
                        </Border.RenderTransform>
                        <DataGridCellsPresenter />
                    </Border>
                </Grid>
            </ControlTemplate>
            """;
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse(templateXaml)));

        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            RowStyle = rowStyle,
            ItemsSource = new[] { "First", "Second" },
        };
        dataGrid.Columns.Add(new DataGridTextColumn { Binding = new System.Windows.Data.Binding() });
        CartSwipeRevealBehavior.SetIsEnabled(dataGrid, true);
        return dataGrid;
    }

    private static TranslateTransform ResolveSwipeTranslation(DataGridRow row)
    {
        row.ApplyTemplate();
        var content = Assert.IsAssignableFrom<FrameworkElement>(
            row.Template.FindName("PART_SwipeContent", row));
        return Assert.IsType<TranslateTransform>(content.RenderTransform);
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

    private static async Task RunOnStaDispatcherAsync(Action action)
    {
        var dispatcherReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                dispatcherReady.TrySetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                dispatcherReady.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Hbpos.Client.Tests.CartSwipeRevealDispatcher",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var dispatcher = await dispatcherReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
        }
        finally
        {
            if (!dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }

            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "WPF Dispatcher thread did not shut down.");
        }
    }
}
