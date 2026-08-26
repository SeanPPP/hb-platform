using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

[Collection(WpfViewLifecycleTestCollection.Name)]
public sealed class TouchScrollFeedbackTests
{
    [Fact]
    public void Public_surface_is_limited_to_the_is_enabled_attached_property()
    {
        var publicMembers = typeof(TouchScrollFeedback)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is MemberTypes.Field or MemberTypes.Method)
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["GetIsEnabled", "IsEnabledProperty", "SetIsEnabled"],
            publicMembers);
        Assert.Equal("IsEnabled", TouchScrollFeedback.IsEnabledProperty.Name);
        Assert.Equal(typeof(bool), TouchScrollFeedback.IsEnabledProperty.PropertyType);
        Assert.Equal(typeof(TouchScrollFeedback), TouchScrollFeedback.IsEnabledProperty.OwnerType);
        Assert.False((bool)TouchScrollFeedback.IsEnabledProperty.DefaultMetadata.DefaultValue!);
    }

    [Theory]
    [InlineData(-10_000d)]
    [InlineData(-180d)]
    [InlineData(-36d)]
    [InlineData(-18d)]
    [InlineData(-1d)]
    [InlineData(0d)]
    [InlineData(1d)]
    [InlineData(18d)]
    [InlineData(36d)]
    [InlineData(180d)]
    [InlineData(10_000d)]
    public void Rubber_band_offset_uses_the_bounded_ios_style_resistance_curve(double translation)
    {
        var expected = translation == 0d
            ? 0d
            : Math.Sign(translation) * 10d * (1d - Math.Exp(-Math.Abs(translation) / 18d));

        var actual = TouchScrollFeedback.CalculateRubberBandOffset(translation);

        Assert.InRange(Math.Abs(actual - expected), 0d, 1e-12d);
        Assert.InRange(Math.Abs(actual), 0d, 10d);
    }

    [Fact]
    public void Rubber_band_offset_handles_non_finite_input_without_exceeding_ten_dip()
    {
        Assert.Equal(0d, TouchScrollFeedback.CalculateRubberBandOffset(double.NaN));
        Assert.Equal(10d, TouchScrollFeedback.CalculateRubberBandOffset(double.PositiveInfinity));
        Assert.Equal(-10d, TouchScrollFeedback.CalculateRubberBandOffset(double.NegativeInfinity));
    }

    [Fact]
    public void Rubber_band_offset_is_symmetric_monotonic_and_strictly_bounded()
    {
        var translations = new[] { 0d, 1d, 6d, 18d, 36d, 180d, 10_000d };
        var offsets = translations
            .Select(TouchScrollFeedback.CalculateRubberBandOffset)
            .ToArray();

        Assert.Equal(0d, offsets[0]);
        for (var index = 1; index < offsets.Length; index++)
        {
            Assert.True(offsets[index] > offsets[index - 1]);
            Assert.InRange(offsets[index], 0d, 10d);
            Assert.Equal(
                -offsets[index],
                TouchScrollFeedback.CalculateRubberBandOffset(-translations[index]),
                precision: 12);
        }
    }

    [Theory]
    [InlineData(false, 8d, false)]
    [InlineData(false, -8d, false)]
    [InlineData(true, 0d, false)]
    [InlineData(true, 8d, true)]
    public void Spring_back_is_skipped_when_system_animation_is_disabled(
        bool clientAreaAnimationEnabled,
        double currentOffset,
        bool expected)
    {
        Assert.Equal(
            expected,
            TouchScrollFeedback.ShouldAnimateSpringBack(clientAreaAnimationEnabled, currentOffset));
    }

    [Fact]
    public async Task Attached_behavior_supports_data_grid_and_direct_scroll_viewer_without_stacking_transforms()
    {
        await RunOnStaDispatcherAsync(() =>
        {
            VerifyLifecycle(CreateDirectScrollViewer());
            VerifyLifecycle(CreateDataGrid());
        });
    }

    private static ScrollViewer CreateDirectScrollViewer()
    {
        var content = new StackPanel();
        for (var index = 0; index < 20; index++)
        {
            content.Children.Add(new TextBlock { Text = $"Row {index}" });
        }

        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static DataGrid CreateDataGrid() => new()
    {
        AutoGenerateColumns = true,
        ItemsSource = Enumerable.Range(0, 20).Select(value => new { Value = value }).ToArray()
    };

    private static void VerifyLifecycle(FrameworkElement owner)
    {
        var window = new Window
        {
            Width = 240d,
            Height = 180d,
            Left = -10_000d,
            Top = -10_000d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = owner
        };

        try
        {
            window.Show();
            PumpDispatcher();
            owner.UpdateLayout();

            var presenter = ResolveContentPresenter(owner);
            var originalTransform = new ScaleTransform(1.01d, 0.99d);
            presenter.RenderTransform = originalTransform;

            Assert.False(TouchScrollFeedback.GetIsEnabled(owner));
            TouchScrollFeedback.SetIsEnabled(owner, true);
            Assert.True(TouchScrollFeedback.GetIsEnabled(owner));

            var firstAppliedTransform = AssertAppliedTransform(presenter, originalTransform);
            var firstTranslation = Assert.IsType<TranslateTransform>(firstAppliedTransform.Children[1]);
            firstTranslation.Y = 6d;
            VerifySystemAnimationDisabledResetsImmediately(owner, firstTranslation);
            firstTranslation.Y = 6d;

            // 同一 Loaded 周期内重复收到事件时，不得重复包装 RenderTransform 或重复挂接。
            owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, owner));
            PumpDispatcher();
            Assert.Same(firstAppliedTransform, presenter.RenderTransform);
            Assert.Equal(6d, firstTranslation.Y);

            window.Content = null;
            PumpDispatcher();
            Assert.Same(originalTransform, presenter.RenderTransform);
            Assert.Equal(0d, firstTranslation.Y);

            window.Content = owner;
            PumpDispatcher();
            owner.UpdateLayout();

            var reloadedPresenter = ResolveContentPresenter(owner);
            Assert.Same(presenter, reloadedPresenter);
            var reloadedTransform = AssertAppliedTransform(reloadedPresenter, originalTransform);
            Assert.NotSame(firstAppliedTransform, reloadedTransform);

            var reloadedTranslation = Assert.IsType<TranslateTransform>(reloadedTransform.Children[1]);
            reloadedTranslation.Y = -6d;
            TouchScrollFeedback.SetIsEnabled(owner, false);

            Assert.False(TouchScrollFeedback.GetIsEnabled(owner));
            Assert.Same(originalTransform, reloadedPresenter.RenderTransform);
            Assert.Equal(0d, reloadedTranslation.Y);
        }
        finally
        {
            TouchScrollFeedback.SetIsEnabled(owner, false);
            window.Content = null;
            window.Close();
            PumpDispatcher();
        }
    }

    private static TransformGroup AssertAppliedTransform(
        FrameworkElement presenter,
        Transform originalTransform)
    {
        var transformGroup = Assert.IsType<TransformGroup>(presenter.RenderTransform);
        Assert.Equal(2, transformGroup.Children.Count);
        Assert.Same(originalTransform, transformGroup.Children[0]);
        Assert.IsType<TranslateTransform>(transformGroup.Children[1]);
        return transformGroup;
    }

    private static void VerifySystemAnimationDisabledResetsImmediately(
        FrameworkElement owner,
        TranslateTransform translation)
    {
        var attachmentPropertyField = typeof(TouchScrollFeedback).GetField(
            "AttachmentProperty",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(attachmentPropertyField);
        var attachmentProperty = Assert.IsType<DependencyProperty>(attachmentPropertyField!.GetValue(null));
        var attachment = owner.GetValue(attachmentProperty);
        Assert.NotNull(attachment);

        var beginSpringBack = attachment.GetType().GetMethod(
            "BeginSpringBack",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(beginSpringBack);
        beginSpringBack!.Invoke(attachment, [false]);

        Assert.Equal(0d, translation.Y);
        Assert.False(translation.HasAnimatedProperties);
    }

    private static FrameworkElement ResolveContentPresenter(FrameworkElement owner)
    {
        owner.ApplyTemplate();
        var scrollViewer = owner switch
        {
            ScrollViewer directScrollViewer => directScrollViewer,
            DataGrid dataGrid => Assert.IsType<ScrollViewer>(
                dataGrid.Template?.FindName("DG_ScrollViewer", dataGrid)),
            _ => throw new InvalidOperationException($"不支持的测试控件类型：{owner.GetType().FullName}")
        };

        scrollViewer.ApplyTemplate();
        return Assert.IsAssignableFrom<FrameworkElement>(
            scrollViewer.Template?.FindName("PART_ScrollContentPresenter", scrollViewer));
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
            Name = "Hbpos.Client.Tests.TouchScrollFeedbackDispatcher"
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
