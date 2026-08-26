using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Hbpos.Client.Wpf.Services;

/// <summary>
/// 为 DataGrid 和 ScrollViewer 提供轻量的触屏边界回弹，不接管 WPF 原生滚动与惯性。
/// </summary>
public static class TouchScrollFeedback
{
    private const double MaximumOffset = 10d;
    private const double ResistanceDistance = 18d;
    private const double ScrollBoundaryTolerance = 0.5d;
    private static readonly Duration SpringBackDuration = new(TimeSpan.FromMilliseconds(200));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TouchScrollFeedback),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty AttachmentProperty = DependencyProperty.RegisterAttached(
        "Attachment",
        typeof(Attachment),
        typeof(TouchScrollFeedback),
        new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    internal static double CalculateRubberBandOffset(double translation)
    {
        if (double.IsNaN(translation) || Math.Abs(translation) < double.Epsilon)
        {
            return 0d;
        }

        if (double.IsPositiveInfinity(translation))
        {
            return MaximumOffset;
        }

        if (double.IsNegativeInfinity(translation))
        {
            return -MaximumOffset;
        }

        var magnitude = MaximumOffset * (1d - Math.Exp(-Math.Abs(translation) / ResistanceDistance));
        return Math.CopySign(magnitude, translation);
    }

    internal static bool ShouldAnimateSpringBack(bool clientAreaAnimationEnabled, double currentOffset) =>
        clientAreaAnimationEnabled && Math.Abs(currentOffset) >= double.Epsilon;

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement owner ||
            owner is not DataGrid && owner is not ScrollViewer)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            if (owner.GetValue(AttachmentProperty) is Attachment)
            {
                return;
            }

            var attachment = new Attachment(owner);
            owner.SetValue(AttachmentProperty, attachment);
            attachment.Enable();
            return;
        }

        if (owner.GetValue(AttachmentProperty) is Attachment existing)
        {
            existing.Disable();
            owner.ClearValue(AttachmentProperty);
        }
    }

    private sealed class Attachment
    {
        private readonly FrameworkElement _owner;
        private readonly EventHandler<ManipulationStartingEventArgs> _manipulationStartingHandler;
        private readonly EventHandler<ManipulationDeltaEventArgs> _manipulationDeltaHandler;
        private readonly EventHandler<ManipulationBoundaryFeedbackEventArgs> _boundaryFeedbackHandler;
        private readonly EventHandler<ManipulationCompletedEventArgs> _manipulationCompletedHandler;
        private ScrollViewer? _scrollViewer;
        private FrameworkElement? _contentPresenter;
        private Transform? _originalTransform;
        private TransformGroup? _appliedTransform;
        private TranslateTransform? _translation;
        private DispatcherOperation? _pendingAttach;
        private DispatcherOperation? _pendingBoundaryCheck;
        private double _rawBoundaryTranslation;
        private bool _boundaryFeedbackSeenForCurrentDelta;
        private int _boundaryCheckVersion;
        private int _animationVersion;
        private bool _enabled;

        internal Attachment(FrameworkElement owner)
        {
            _owner = owner;
            _manipulationStartingHandler = OnManipulationStarting;
            _manipulationDeltaHandler = OnManipulationDelta;
            _boundaryFeedbackHandler = OnManipulationBoundaryFeedback;
            _manipulationCompletedHandler = OnManipulationCompleted;
        }

        internal void Enable()
        {
            if (_enabled)
            {
                return;
            }

            _enabled = true;
            _owner.Loaded += OnOwnerLoaded;
            _owner.Unloaded += OnOwnerUnloaded;

            if (_owner.IsLoaded)
            {
                AttachOrScheduleRetry();
            }
        }

        internal void Disable()
        {
            if (!_enabled)
            {
                return;
            }

            _enabled = false;
            _owner.Loaded -= OnOwnerLoaded;
            _owner.Unloaded -= OnOwnerUnloaded;
            CancelPendingAttach();
            CancelPendingBoundaryCheck();
            DetachVisuals();
        }

        private void OnOwnerLoaded(object sender, RoutedEventArgs args) => AttachOrScheduleRetry();

        private void OnOwnerUnloaded(object sender, RoutedEventArgs args)
        {
            CancelPendingAttach();
            CancelPendingBoundaryCheck();
            DetachVisuals();
        }

        private void AttachOrScheduleRetry()
        {
            CancelPendingAttach();
            if (TryAttachVisuals())
            {
                return;
            }

            // 模板通常会在 Loaded 后同一轮布局中生成；只补一次异步重试，避免长期监听 LayoutUpdated。
            _pendingAttach = _owner.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    _pendingAttach = null;
                    if (_enabled && _owner.IsLoaded)
                    {
                        TryAttachVisuals();
                    }
                }));
        }

        private bool TryAttachVisuals()
        {
            var scrollViewer = ResolveScrollViewer(_owner);
            var contentPresenter = scrollViewer is null ? null : ResolveContentPresenter(scrollViewer);
            if (scrollViewer is null || contentPresenter is null)
            {
                return false;
            }

            if (ReferenceEquals(_scrollViewer, scrollViewer) &&
                ReferenceEquals(_contentPresenter, contentPresenter) &&
                ReferenceEquals(contentPresenter.RenderTransform, _appliedTransform))
            {
                return true;
            }

            DetachVisuals();

            _scrollViewer = scrollViewer;
            _contentPresenter = contentPresenter;
            _originalTransform = contentPresenter.RenderTransform;
            _translation = new TranslateTransform();
            _appliedTransform = new TransformGroup();
            _appliedTransform.Children.Add(_originalTransform);
            _appliedTransform.Children.Add(_translation);
            contentPresenter.RenderTransform = _appliedTransform;

            scrollViewer.AddHandler(
                UIElement.ManipulationStartingEvent,
                _manipulationStartingHandler,
                handledEventsToo: true);
            scrollViewer.AddHandler(
                UIElement.ManipulationDeltaEvent,
                _manipulationDeltaHandler,
                handledEventsToo: true);
            scrollViewer.AddHandler(
                UIElement.ManipulationBoundaryFeedbackEvent,
                _boundaryFeedbackHandler,
                handledEventsToo: true);
            scrollViewer.AddHandler(
                UIElement.ManipulationCompletedEvent,
                _manipulationCompletedHandler,
                handledEventsToo: true);
            return true;
        }

        private void DetachVisuals()
        {
            CancelPendingBoundaryCheck();
            if (_scrollViewer is not null)
            {
                _scrollViewer.RemoveHandler(UIElement.ManipulationStartingEvent, _manipulationStartingHandler);
                _scrollViewer.RemoveHandler(UIElement.ManipulationDeltaEvent, _manipulationDeltaHandler);
                _scrollViewer.RemoveHandler(UIElement.ManipulationBoundaryFeedbackEvent, _boundaryFeedbackHandler);
                _scrollViewer.RemoveHandler(UIElement.ManipulationCompletedEvent, _manipulationCompletedHandler);
            }

            ResetImmediately();

            if (_contentPresenter is not null &&
                ReferenceEquals(_contentPresenter.RenderTransform, _appliedTransform))
            {
                _contentPresenter.RenderTransform = _originalTransform ?? Transform.Identity;
            }

            _scrollViewer = null;
            _contentPresenter = null;
            _originalTransform = null;
            _appliedTransform = null;
            _translation = null;
            _boundaryFeedbackSeenForCurrentDelta = false;
        }

        private void OnManipulationStarting(object? sender, ManipulationStartingEventArgs args)
        {
            if (IsTargetEvent(args))
            {
                CancelPendingBoundaryCheck();
                _boundaryFeedbackSeenForCurrentDelta = false;
                ResetImmediately();
            }
        }

        private void OnManipulationDelta(object? sender, ManipulationDeltaEventArgs args)
        {
            if (!IsTargetEvent(args))
            {
                return;
            }

            // ReportBoundaryFeedback 会在本次 ManipulationDelta 路由结束后转换成边界事件，
            // 因此在输入队列末尾判断本帧是否仍有边界反馈，避免反向拖回内容时残留偏移。
            CancelPendingBoundaryCheck();
            _boundaryFeedbackSeenForCurrentDelta = false;
            var version = _boundaryCheckVersion;
            _pendingBoundaryCheck = _owner.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    _pendingBoundaryCheck = null;
                    if (!_enabled || version != _boundaryCheckVersion)
                    {
                        return;
                    }

                    if (!_boundaryFeedbackSeenForCurrentDelta &&
                        Math.Abs(_translation?.Y ?? 0d) >= double.Epsilon)
                    {
                        ResetImmediately();
                    }

                    _boundaryFeedbackSeenForCurrentDelta = false;
                }));
        }

        private void OnManipulationBoundaryFeedback(object? sender, ManipulationBoundaryFeedbackEventArgs args)
        {
            if (!IsTargetEvent(args))
            {
                return;
            }

            _boundaryFeedbackSeenForCurrentDelta = true;
            args.Handled = true;

            var feedback = args.BoundaryFeedback.Translation.Y;
            if (_scrollViewer is null ||
                _translation is null ||
                _scrollViewer.ScrollableHeight <= ScrollBoundaryTolerance ||
                Math.Abs(feedback) < double.Epsilon ||
                !IsAtMatchingBoundary(_scrollViewer, feedback))
            {
                ResetImmediately();
                return;
            }

            CancelAnimation();
            // WPF 报告的是当前累计未消费位移，不应再次累加，否则回弹会被重复放大。
            _rawBoundaryTranslation = Math.Clamp(feedback, -10_000d, 10_000d);
            _translation.Y = CalculateRubberBandOffset(_rawBoundaryTranslation);
        }

        private void OnManipulationCompleted(object? sender, ManipulationCompletedEventArgs args)
        {
            if (IsTargetEvent(args))
            {
                BeginSpringBack(SystemParameters.ClientAreaAnimation);
            }
        }

        private void BeginSpringBack(bool clientAreaAnimationEnabled)
        {
            if (_translation is null ||
                !ShouldAnimateSpringBack(clientAreaAnimationEnabled, _translation.Y))
            {
                ResetImmediately();
                return;
            }

            var startOffset = _translation.Y;
            CancelAnimation();
            _translation.Y = 0d;

            var version = _animationVersion;
            var animation = new DoubleAnimation
            {
                From = startOffset,
                To = 0d,
                Duration = SpringBackDuration,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new ElasticEase
                {
                    EasingMode = EasingMode.EaseOut,
                    Oscillations = 1,
                    Springiness = 8d
                }
            };
            animation.Completed += (_, _) =>
            {
                if (version == _animationVersion && _translation is not null)
                {
                    _translation.Y = 0d;
                    _rawBoundaryTranslation = 0d;
                }
            };
            _translation.BeginAnimation(
                TranslateTransform.YProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void ResetImmediately()
        {
            CancelAnimation();
            _rawBoundaryTranslation = 0d;
            if (_translation is not null)
            {
                _translation.Y = 0d;
            }
        }

        private void CancelAnimation()
        {
            _animationVersion++;
            _translation?.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private bool IsTargetEvent(RoutedEventArgs args) =>
            _scrollViewer is not null &&
            (ReferenceEquals(args.Source, _scrollViewer) || ReferenceEquals(args.OriginalSource, _scrollViewer));

        private static bool IsAtMatchingBoundary(ScrollViewer scrollViewer, double feedback) =>
            feedback > 0d
                ? scrollViewer.VerticalOffset <= ScrollBoundaryTolerance
                : scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - ScrollBoundaryTolerance;

        private void CancelPendingAttach()
        {
            if (_pendingAttach?.Status == DispatcherOperationStatus.Pending)
            {
                _pendingAttach.Abort();
            }

            _pendingAttach = null;
        }

        private void CancelPendingBoundaryCheck()
        {
            _boundaryCheckVersion++;
            if (_pendingBoundaryCheck?.Status == DispatcherOperationStatus.Pending)
            {
                _pendingBoundaryCheck.Abort();
            }

            _pendingBoundaryCheck = null;
        }

        private static ScrollViewer? ResolveScrollViewer(FrameworkElement owner)
        {
            if (owner is ScrollViewer scrollViewer)
            {
                scrollViewer.ApplyTemplate();
                return scrollViewer;
            }

            if (owner is not DataGrid dataGrid)
            {
                return null;
            }

            dataGrid.ApplyTemplate();
            if (dataGrid.Template?.FindName("DG_ScrollViewer", dataGrid) is ScrollViewer namedScrollViewer)
            {
                namedScrollViewer.ApplyTemplate();
                return namedScrollViewer;
            }

            var fallback = FindVisualDescendant<ScrollViewer>(dataGrid, "DG_ScrollViewer") ??
                           FindVisualDescendant<ScrollViewer>(dataGrid);
            fallback?.ApplyTemplate();
            return fallback;
        }

        private static FrameworkElement? ResolveContentPresenter(ScrollViewer scrollViewer)
        {
            scrollViewer.ApplyTemplate();
            return scrollViewer.Template?.FindName("PART_ScrollContentPresenter", scrollViewer) as FrameworkElement ??
                   FindVisualDescendant<ScrollContentPresenter>(scrollViewer, "PART_ScrollContentPresenter") ??
                   FindVisualDescendant<ScrollContentPresenter>(scrollViewer);
        }

        private static T? FindVisualDescendant<T>(DependencyObject root, string? name = null)
            where T : FrameworkElement
        {
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match && (name is null || string.Equals(match.Name, name, StringComparison.Ordinal)))
                {
                    return match;
                }

                var descendant = FindVisualDescendant<T>(child, name);
                if (descendant is not null)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
