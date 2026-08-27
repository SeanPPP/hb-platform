using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Hbpos.Client.Wpf.Services;

internal enum CartSwipeGestureAxis
{
    Pending,
    Horizontal,
    Vertical,
}

/// <summary>
/// 为收银台购物车表格提供单行左滑操作，不接管原有纵向触屏滚动。
/// </summary>
public static class CartSwipeRevealBehavior
{
    private const double DirectionThreshold = 12d;
    private const double HorizontalDominance = 1.2d;
    private const double DefaultRevealWidth = 88d;
    private const string SwipeContentPartName = "PART_SwipeContent";
    private const string SwipeDeleteActionPartName = "PART_SwipeDeleteAction";
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(150));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(CartSwipeRevealBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty AttachmentProperty = DependencyProperty.RegisterAttached(
        "Attachment",
        typeof(Attachment),
        typeof(CartSwipeRevealBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty OpenRowProperty = DependencyProperty.RegisterAttached(
        "OpenRow",
        typeof(DataGridRow),
        typeof(CartSwipeRevealBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty RowTrackedProperty = DependencyProperty.RegisterAttached(
        "RowTracked",
        typeof(bool),
        typeof(CartSwipeRevealBehavior),
        new PropertyMetadata(false));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    internal static CartSwipeGestureAxis ResolveAxis(double horizontal, double vertical)
    {
        if (!double.IsFinite(horizontal) || !double.IsFinite(vertical))
        {
            return CartSwipeGestureAxis.Vertical;
        }

        var horizontalMagnitude = Math.Abs(horizontal);
        var verticalMagnitude = Math.Abs(vertical);
        if (horizontalMagnitude < DirectionThreshold && verticalMagnitude < DirectionThreshold)
        {
            return CartSwipeGestureAxis.Pending;
        }

        return horizontalMagnitude >= DirectionThreshold &&
               horizontalMagnitude >= verticalMagnitude * HorizontalDominance
            ? CartSwipeGestureAxis.Horizontal
            : CartSwipeGestureAxis.Vertical;
    }

    internal static double ClampOffset(double currentOffset, double delta, double revealWidth)
    {
        if (!double.IsFinite(revealWidth) || revealWidth <= 0d)
        {
            return 0d;
        }

        var safeCurrentOffset = double.IsFinite(currentOffset) ? currentOffset : 0d;
        var safeDelta = double.IsFinite(delta) ? delta : 0d;
        return Math.Clamp(safeCurrentOffset + safeDelta, -revealWidth, 0d);
    }

    internal static bool ShouldReveal(double offset, double revealWidth) =>
        double.IsFinite(offset) &&
        double.IsFinite(revealWidth) &&
        revealWidth > 0d &&
        -offset >= revealWidth / 2d;

    internal static bool ShouldAnimateTransition(
        bool clientAreaAnimationEnabled,
        double currentOffset,
        double targetOffset) =>
        clientAreaAnimationEnabled &&
        double.IsFinite(currentOffset) &&
        double.IsFinite(targetOffset) &&
        Math.Abs(currentOffset - targetOffset) >= double.Epsilon;

    internal static void SetRevealState(
        DataGrid dataGrid,
        DataGridRow row,
        bool isRevealed,
        bool animate)
    {
        var openRow = dataGrid.GetValue(OpenRowProperty) as DataGridRow;
        if (isRevealed)
        {
            if (openRow is not null && !ReferenceEquals(openRow, row))
            {
                ApplyTargetOffset(openRow, 0d, animate);
            }

            if (!TryResolveRowVisual(row, out _, out var action, out _))
            {
                dataGrid.ClearValue(OpenRowProperty);
                return;
            }

            dataGrid.SetValue(OpenRowProperty, row);
            ApplyTargetOffset(row, -ResolveRevealWidth(action), animate);
            return;
        }

        ApplyTargetOffset(row, 0d, animate);
        if (ReferenceEquals(openRow, row))
        {
            dataGrid.ClearValue(OpenRowProperty);
        }
    }

    internal static void ResetRowVisual(DataGridRow row)
    {
        var dataGrid = ItemsControl.ItemsControlFromItemContainer(row) as DataGrid ??
                       FindAncestor<DataGrid>(row);
        if (dataGrid is not null && ReferenceEquals(dataGrid.GetValue(OpenRowProperty), row))
        {
            dataGrid.ClearValue(OpenRowProperty);
        }

        if (TryResolveRowVisual(row, out _, out _, out var translation))
        {
            SetImmediateOffset(translation, 0d);
        }
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not DataGrid dataGrid)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            if (dataGrid.GetValue(AttachmentProperty) is Attachment)
            {
                return;
            }

            var attachment = new Attachment(dataGrid);
            dataGrid.SetValue(AttachmentProperty, attachment);
            attachment.Enable();
            return;
        }

        if (dataGrid.GetValue(AttachmentProperty) is Attachment existing)
        {
            existing.Disable();
            dataGrid.ClearValue(AttachmentProperty);
        }
    }

    private static void ApplyTargetOffset(DataGridRow row, double targetOffset, bool animate)
    {
        if (!TryResolveRowVisual(row, out _, out _, out var translation))
        {
            return;
        }

        var currentOffset = translation.X;
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.X = targetOffset;

        if (!animate ||
            !ShouldAnimateTransition(SystemParameters.ClientAreaAnimation, currentOffset, targetOffset))
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentOffset,
            To = targetOffset,
            Duration = TransitionDuration,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut,
            },
        };
        translation.BeginAnimation(
            TranslateTransform.XProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void SetImmediateOffset(TranslateTransform translation, double offset)
    {
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.X = offset;
    }

    private static double StopAnimationAndReadOffset(TranslateTransform translation)
    {
        var currentOffset = translation.X;
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.X = currentOffset;
        return currentOffset;
    }

    private static bool TryResolveRowVisual(
        DataGridRow row,
        out FrameworkElement content,
        out FrameworkElement action,
        out TranslateTransform translation)
    {
        row.ApplyTemplate();
        var resolvedContent = row.Template?.FindName(SwipeContentPartName, row) as FrameworkElement;
        var resolvedAction = row.Template?.FindName(SwipeDeleteActionPartName, row) as FrameworkElement;
        if (resolvedContent?.RenderTransform is not TranslateTransform resolvedTranslation ||
            resolvedAction is null)
        {
            content = null!;
            action = null!;
            translation = null!;
            return false;
        }

        if (resolvedTranslation.IsFrozen)
        {
            resolvedTranslation = resolvedTranslation.CloneCurrentValue();
            resolvedContent.RenderTransform = resolvedTranslation;
        }

        content = resolvedContent;
        action = resolvedAction;
        translation = resolvedTranslation;
        return true;
    }

    private static double ResolveRevealWidth(FrameworkElement action)
    {
        if (double.IsFinite(action.ActualWidth) && action.ActualWidth > 0d)
        {
            return action.ActualWidth;
        }

        return double.IsFinite(action.Width) && action.Width > 0d
            ? action.Width
            : DefaultRevealWidth;
    }

    private static T? FindAncestor<T>(DependencyObject? source, DependencyObject? stopAt = null)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            if (ReferenceEquals(current, stopAt))
            {
                return null;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is FrameworkContentElement contentElement && contentElement.Parent is not null)
        {
            return contentElement.Parent;
        }

        if (current is FrameworkElement frameworkElement && frameworkElement.Parent is not null)
        {
            return frameworkElement.Parent;
        }

        return current is Visual or Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
    }

    private enum PointerKind
    {
        None,
        Touch,
        Mouse,
    }

    private sealed class Attachment
    {
        private readonly DataGrid _owner;
        private readonly EventHandler<TouchEventArgs> _previewTouchDownHandler;
        private readonly EventHandler<TouchEventArgs> _previewTouchMoveHandler;
        private readonly EventHandler<TouchEventArgs> _previewTouchUpHandler;
        private readonly EventHandler<TouchEventArgs> _lostTouchCaptureHandler;
        private readonly MouseButtonEventHandler _previewMouseDownHandler;
        private readonly MouseEventHandler _previewMouseMoveHandler;
        private readonly MouseButtonEventHandler _previewMouseUpHandler;
        private readonly MouseEventHandler _lostMouseCaptureHandler;
        private readonly MouseWheelEventHandler _previewMouseWheelHandler;
        private readonly ScrollChangedEventHandler _scrollChangedHandler;
        private DataGridRow? _candidateRow;
        private TouchDevice? _touchDevice;
        private Point _startPoint;
        private double _startingOffset;
        private double _revealWidth;
        private CartSwipeGestureAxis _axis;
        private PointerKind _pointerKind;
        private bool _enabled;
        private bool _hasCapture;

        internal Attachment(DataGrid owner)
        {
            _owner = owner;
            _previewTouchDownHandler = OnPreviewTouchDown;
            _previewTouchMoveHandler = OnPreviewTouchMove;
            _previewTouchUpHandler = OnPreviewTouchUp;
            _lostTouchCaptureHandler = OnLostTouchCapture;
            _previewMouseDownHandler = OnPreviewMouseDown;
            _previewMouseMoveHandler = OnPreviewMouseMove;
            _previewMouseUpHandler = OnPreviewMouseUp;
            _lostMouseCaptureHandler = OnLostMouseCapture;
            _previewMouseWheelHandler = OnPreviewMouseWheel;
            _scrollChangedHandler = OnScrollChanged;
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
            _owner.LoadingRow += OnLoadingRow;
            _owner.UnloadingRow += OnUnloadingRow;
            _owner.AddHandler(UIElement.PreviewTouchDownEvent, _previewTouchDownHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.PreviewTouchMoveEvent, _previewTouchMoveHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.PreviewTouchUpEvent, _previewTouchUpHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.LostTouchCaptureEvent, _lostTouchCaptureHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, _previewMouseDownHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.PreviewMouseMoveEvent, _previewMouseMoveHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, _previewMouseUpHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.LostMouseCaptureEvent, _lostMouseCaptureHandler, handledEventsToo: true);
            _owner.AddHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler, handledEventsToo: true);
            _owner.AddHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler, handledEventsToo: true);

            if (_owner.IsLoaded)
            {
                TrackRealizedRows(resetVisuals: true);
            }
        }

        internal void Disable()
        {
            if (!_enabled)
            {
                return;
            }

            _enabled = false;
            CancelGesture(animate: false);
            CloseOpenRow(animate: false);
            TrackRealizedRows(resetVisuals: true, removeTracking: true);

            _owner.Loaded -= OnOwnerLoaded;
            _owner.Unloaded -= OnOwnerUnloaded;
            _owner.LoadingRow -= OnLoadingRow;
            _owner.UnloadingRow -= OnUnloadingRow;
            _owner.RemoveHandler(UIElement.PreviewTouchDownEvent, _previewTouchDownHandler);
            _owner.RemoveHandler(UIElement.PreviewTouchMoveEvent, _previewTouchMoveHandler);
            _owner.RemoveHandler(UIElement.PreviewTouchUpEvent, _previewTouchUpHandler);
            _owner.RemoveHandler(UIElement.LostTouchCaptureEvent, _lostTouchCaptureHandler);
            _owner.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, _previewMouseDownHandler);
            _owner.RemoveHandler(UIElement.PreviewMouseMoveEvent, _previewMouseMoveHandler);
            _owner.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, _previewMouseUpHandler);
            _owner.RemoveHandler(UIElement.LostMouseCaptureEvent, _lostMouseCaptureHandler);
            _owner.RemoveHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler);
            _owner.RemoveHandler(ScrollViewer.ScrollChangedEvent, _scrollChangedHandler);
            _owner.ClearValue(OpenRowProperty);
        }

        private void OnOwnerLoaded(object sender, RoutedEventArgs args) =>
            TrackRealizedRows(resetVisuals: true);

        private void OnOwnerUnloaded(object sender, RoutedEventArgs args)
        {
            CancelGesture(animate: false);
            CloseOpenRow(animate: false);
            TrackRealizedRows(resetVisuals: true);
        }

        private void OnLoadingRow(object? sender, DataGridRowEventArgs args)
        {
            ResetRowVisual(args.Row);
            TrackRow(args.Row);
        }

        private void OnUnloadingRow(object? sender, DataGridRowEventArgs args)
        {
            if (ReferenceEquals(_candidateRow, args.Row))
            {
                CancelGesture(animate: false);
            }

            ResetRowVisual(args.Row);
            UntrackRow(args.Row);
        }

        private void OnRowDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is not DataGridRow row)
            {
                return;
            }

            if (ReferenceEquals(_candidateRow, row))
            {
                CancelGesture(animate: false);
            }

            ResetRowVisual(row);
        }

        private void OnPreviewTouchDown(object? sender, TouchEventArgs args)
        {
            if (_pointerKind is not PointerKind.None)
            {
                return;
            }

            BeginCandidate(
                args.OriginalSource as DependencyObject,
                args.GetTouchPoint(_owner).Position,
                PointerKind.Touch,
                args.TouchDevice);
        }

        private void OnPreviewTouchMove(object? sender, TouchEventArgs args)
        {
            if (_pointerKind is not PointerKind.Touch || !ReferenceEquals(_touchDevice, args.TouchDevice))
            {
                return;
            }

            if (!ProcessMove(args.GetTouchPoint(_owner).Position))
            {
                return;
            }

            if (!_hasCapture)
            {
                _hasCapture = args.TouchDevice.Capture(_owner, CaptureMode.SubTree);
            }

            args.Handled = true;
        }

        private void OnPreviewTouchUp(object? sender, TouchEventArgs args)
        {
            if (_pointerKind is not PointerKind.Touch || !ReferenceEquals(_touchDevice, args.TouchDevice))
            {
                return;
            }

            ProcessMove(args.GetTouchPoint(_owner).Position);
            var handled = _axis is CartSwipeGestureAxis.Horizontal;
            CompleteGesture(animate: true, releaseCapture: true);
            args.Handled = handled;
        }

        private void OnLostTouchCapture(object? sender, TouchEventArgs args)
        {
            if (_pointerKind is PointerKind.Touch && ReferenceEquals(_touchDevice, args.TouchDevice))
            {
                CompleteGesture(animate: true, releaseCapture: false);
            }
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (_pointerKind is not PointerKind.None || args.StylusDevice is not null)
            {
                return;
            }

            BeginCandidate(
                args.OriginalSource as DependencyObject,
                args.GetPosition(_owner),
                PointerKind.Mouse,
                touchDevice: null);
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs args)
        {
            if (_pointerKind is not PointerKind.Mouse)
            {
                return;
            }

            if (args.LeftButton is not MouseButtonState.Pressed)
            {
                CompleteGesture(animate: true, releaseCapture: true);
                return;
            }

            if (!ProcessMove(args.GetPosition(_owner)))
            {
                return;
            }

            if (!_hasCapture)
            {
                _hasCapture = Mouse.Capture(_owner, CaptureMode.SubTree);
            }

            args.Handled = true;
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs args)
        {
            if (_pointerKind is not PointerKind.Mouse)
            {
                return;
            }

            ProcessMove(args.GetPosition(_owner));
            var handled = _axis is CartSwipeGestureAxis.Horizontal;
            CompleteGesture(animate: true, releaseCapture: true);
            args.Handled = handled;
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs args)
        {
            if (_pointerKind is PointerKind.Mouse)
            {
                CompleteGesture(animate: true, releaseCapture: false);
            }
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args)
        {
            CancelGesture(animate: true);
            CloseOpenRow(animate: true);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (Math.Abs(args.VerticalChange) < double.Epsilon &&
                Math.Abs(args.HorizontalChange) < double.Epsilon)
            {
                return;
            }

            CancelGesture(animate: true);
            CloseOpenRow(animate: true);
        }

        private void BeginCandidate(
            DependencyObject? originalSource,
            Point point,
            PointerKind pointerKind,
            TouchDevice? touchDevice)
        {
            var row = originalSource is null
                ? null
                : ItemsControl.ContainerFromElement(_owner, originalSource) as DataGridRow;
            if (row is null)
            {
                CloseOpenRow(animate: true);
                return;
            }

            var openRow = _owner.GetValue(OpenRowProperty) as DataGridRow;
            if (openRow is not null && !ReferenceEquals(openRow, row))
            {
                SetRevealState(_owner, openRow, isRevealed: false, animate: true);
            }

            if (FindAncestor<ButtonBase>(originalSource, row) is not null ||
                FindAncestor<Thumb>(originalSource, row) is not null ||
                !TryResolveRowVisual(row, out _, out var action, out var translation))
            {
                return;
            }

            _candidateRow = row;
            _touchDevice = touchDevice;
            _startPoint = point;
            _startingOffset = StopAnimationAndReadOffset(translation);
            _revealWidth = ResolveRevealWidth(action);
            _axis = CartSwipeGestureAxis.Pending;
            _pointerKind = pointerKind;
            _hasCapture = false;
        }

        private bool ProcessMove(Point point)
        {
            if (_candidateRow is null ||
                !TryResolveRowVisual(_candidateRow, out _, out _, out var translation))
            {
                return false;
            }

            var horizontal = point.X - _startPoint.X;
            var vertical = point.Y - _startPoint.Y;
            if (_axis is CartSwipeGestureAxis.Pending)
            {
                _axis = ResolveAxis(horizontal, vertical);
                if (_axis is CartSwipeGestureAxis.Pending)
                {
                    return false;
                }

                if (_axis is CartSwipeGestureAxis.Vertical)
                {
                    SetRevealState(_owner, _candidateRow, isRevealed: false, animate: true);
                    ClearCandidate();
                    return false;
                }
            }

            SetImmediateOffset(
                translation,
                ClampOffset(_startingOffset, horizontal, _revealWidth));
            return true;
        }

        private void CompleteGesture(bool animate, bool releaseCapture)
        {
            var row = _candidateRow;
            var axis = _axis;
            var pointerKind = _pointerKind;
            var touchDevice = _touchDevice;
            var captured = _hasCapture;
            var shouldReveal = row is not null &&
                               axis is CartSwipeGestureAxis.Horizontal &&
                               TryResolveRowVisual(row, out _, out _, out var translation) &&
                               ShouldReveal(translation.X, _revealWidth);

            ClearCandidate();
            if (row is not null && axis is CartSwipeGestureAxis.Horizontal)
            {
                SetRevealState(_owner, row, shouldReveal, animate);
            }

            if (!releaseCapture || !captured)
            {
                return;
            }

            if (pointerKind is PointerKind.Touch && ReferenceEquals(touchDevice?.Captured, _owner))
            {
                touchDevice.Capture(null);
            }
            else if (pointerKind is PointerKind.Mouse && ReferenceEquals(Mouse.Captured, _owner))
            {
                Mouse.Capture(null);
            }
        }

        private void CancelGesture(bool animate)
        {
            if (_candidateRow is null)
            {
                return;
            }

            var row = _candidateRow;
            var pointerKind = _pointerKind;
            var touchDevice = _touchDevice;
            var captured = _hasCapture;
            ClearCandidate();
            SetRevealState(_owner, row, isRevealed: false, animate);

            if (captured && pointerKind is PointerKind.Touch && ReferenceEquals(touchDevice?.Captured, _owner))
            {
                touchDevice.Capture(null);
            }
            else if (captured && pointerKind is PointerKind.Mouse && ReferenceEquals(Mouse.Captured, _owner))
            {
                Mouse.Capture(null);
            }
        }

        private void ClearCandidate()
        {
            _candidateRow = null;
            _touchDevice = null;
            _startPoint = default;
            _startingOffset = 0d;
            _revealWidth = 0d;
            _axis = CartSwipeGestureAxis.Pending;
            _pointerKind = PointerKind.None;
            _hasCapture = false;
        }

        private void CloseOpenRow(bool animate)
        {
            if (_owner.GetValue(OpenRowProperty) is DataGridRow openRow)
            {
                SetRevealState(_owner, openRow, isRevealed: false, animate);
            }
        }

        private void TrackRealizedRows(bool resetVisuals, bool removeTracking = false)
        {
            for (var index = 0; index < _owner.Items.Count; index++)
            {
                if (_owner.ItemContainerGenerator.ContainerFromIndex(index) is not DataGridRow row)
                {
                    continue;
                }

                if (resetVisuals)
                {
                    ResetRowVisual(row);
                }

                if (removeTracking)
                {
                    UntrackRow(row);
                }
                else
                {
                    TrackRow(row);
                }
            }
        }

        private void TrackRow(DataGridRow row)
        {
            if ((bool)row.GetValue(RowTrackedProperty))
            {
                return;
            }

            row.SetValue(RowTrackedProperty, true);
            row.DataContextChanged += OnRowDataContextChanged;
        }

        private void UntrackRow(DataGridRow row)
        {
            if (!(bool)row.GetValue(RowTrackedProperty))
            {
                return;
            }

            row.DataContextChanged -= OnRowDataContextChanged;
            row.ClearValue(RowTrackedProperty);
        }
    }
}
