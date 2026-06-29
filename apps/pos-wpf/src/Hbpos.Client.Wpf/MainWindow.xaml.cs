using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf;

public partial class MainWindow : Window
{
    private static readonly TimeSpan StartupUpdateRetryDelay = TimeSpan.FromSeconds(5);
    private const string UnconfiguredAppUpdateCenterErrorCode = "APP_UPDATE_CENTER_NOT_CONFIGURED";
    private const int RawInputMessageId = 0x00FF;

    internal delegate IntPtr WindowMessageProcessor(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled);

    private readonly MainViewModel _viewModel;
    private readonly AppStartupOptions _startupOptions;
    private readonly IRawScannerService _rawScannerService;
    private readonly IDisplayTopologyService _displayTopologyService;
    private readonly IUiPriorityCoordinator _uiPriorityCoordinator;
    private readonly IAppUpdateCoordinator _appUpdateCoordinator;
    private HwndSource? _hwndSource;
    private Task? _startupInitializationTask;
    private readonly KeyboardScannerFallbackBuffer _keyboardScannerFallback = new();
    private bool _postShowStartupStarted;

    public bool IsStartupBlockedByAppUpdate { get; private set; }

    public event EventHandler? StartupCompleted;

    public MainWindow(
        MainViewModel viewModel,
        AppStartupOptions startupOptions,
        IRawScannerService rawScannerService,
        IDisplayTopologyService displayTopologyService,
        IUiPriorityCoordinator uiPriorityCoordinator,
        IAppUpdateCoordinator appUpdateCoordinator)
    {
        _viewModel = viewModel;
        _startupOptions = startupOptions;
        _rawScannerService = rawScannerService;
        _displayTopologyService = displayTopologyService;
        _uiPriorityCoordinator = uiPriorityCoordinator;
        _appUpdateCoordinator = appUpdateCoordinator;
        DataContext = _viewModel;
        InitializeComponent();
        SourceInitialized += MainWindowSourceInitialized;
        Loaded += MainWindowLoaded;
        PreviewKeyDown += MainWindowPreviewKeyDown;
        PreviewMouseDown += MainWindowUserInput;
        PreviewMouseMove += MainWindowUserInput;
        PreviewMouseWheel += MainWindowUserInput;
        PreviewTouchDown += MainWindowUserInput;
        Closed += MainWindowClosed;
    }

    private void CashierBarcodePasswordBoxPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            _viewModel.CashierBarcodeInput = passwordBox.Password;
        }
    }

    private void CashierBarcodePasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ExecuteCashierLoginCommandFromPasswordBox();
    }

    private void CashierLoginButtonClick(object sender, RoutedEventArgs e)
    {
        ClearCashierBarcodePasswordBoxesAfterLogin();
    }

    private void ExecuteCashierLoginCommandFromPasswordBox()
    {
        var command = _viewModel.LoginCashierCommand;
        if (!command.CanExecute(null))
        {
            return;
        }

        command.Execute(null);
        ClearCashierBarcodePasswordBoxesAfterLogin();
    }

    private void ClearCashierBarcodePasswordBoxesAfterLogin()
    {
        // 关键逻辑：PasswordBox 不能普通绑定，登录命令清空 VM 后还要同步清掉屏幕上的敏感输入。
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                CashierBarcodePasswordBox.Clear();
                CashierLoginOverlayPasswordBox.Clear();
            }));
    }

    private void CashierLoginOverlayIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        // 关键逻辑：遮盖打开后立即把焦点交给扫码输入框，扫码枪可直接录入收银员条码。
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                CashierLoginOverlayPasswordBox.Focus();
                Keyboard.Focus(CashierLoginOverlayPasswordBox);
            }));
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowLoaded;
        try
        {
            await InitializeForStartupAsync();
        }
        catch (Exception ex)
        {
            // Loaded 事件是 async void，初始化失败时记录并关闭窗口，避免异常脱离任务链。
            ConsoleLog.WriteError("Startup", $"main window initialization failed error={ex.GetType().Name} message={ex.Message}", exception: ex);
            _viewModel.StatusMessage = ex.Message;
            Close();
        }
    }

    public Task InitializeForStartupAsync()
    {
        _startupInitializationTask ??= InitializeForStartupCoreAsync();
        return _startupInitializationTask;
    }

    private async Task InitializeForStartupCoreAsync()
    {
        var updateResult = await RunStartupAppUpdateCheckAsync();
        IsStartupBlockedByAppUpdate = !ShouldContinueStartupAfterAppUpdateCheck(updateResult);
        if (IsStartupBlockedByAppUpdate)
        {
            // 更新闸门未放行前不初始化 scanner 和主 VM，避免旧版本继续进入收银流程。
            return;
        }

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        await _rawScannerService.InitializeAsync();
        _rawScannerService.Start(hwnd);
        await _viewModel.InitializeAsync(_startupOptions);
        StartupCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void ContinueStartupAfterShown()
    {
        if (_postShowStartupStarted)
        {
            return;
        }

        _postShowStartupStarted = true;
        _ = ContinueStartupAfterShownCoreAsync();
    }

    public void ActivateForScannerInput()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        var wasTopmost = Topmost;
        Topmost = true;
        Activate();
        Focus();
        Topmost = wasTopmost;
    }

    private async Task ContinueStartupAfterShownCoreAsync()
    {
        try
        {
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
            await Task.Delay(300);
            await _viewModel.ContinueStartupAfterShownAsync(_startupOptions, this);
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = ex.Message;
        }
    }

    private async Task<AppUpdateCoordinatorResult> RunStartupAppUpdateCheckAsync()
    {
        return await RunStartupAppUpdateCheckCoreAsync(
            () => _appUpdateCoordinator.CheckForUpdatesAsync(manual: false),
            delay => Task.Delay(delay),
            ReportStartupAppUpdateException,
            ShowStartupAppUpdateFailure);
    }

    private void ReportStartupAppUpdateException(Exception ex)
    {
        ConsoleLog.WriteError("Startup", $"app update startup check failed error={ex.GetType().Name} message={ex.Message}", exception: ex);
        ShowStartupAppUpdateFailure(AppUpdateCoordinatorResult.FromStatus(AppUpdateCoordinatorStatus.CheckFailed, ex.Message));
    }

    private void ShowStartupAppUpdateFailure(AppUpdateCoordinatorResult result)
    {
        var message = ResolveStartupAppUpdateFailureMessage(result);
        _viewModel.StatusMessage = message;
        _viewModel.AppUpdate.ShowStartupUpdateError(message, RetryStartupAfterAppUpdateFailureAsync, Close);
    }

    private async Task RetryStartupAfterAppUpdateFailureAsync()
    {
        try
        {
            // 重试启动闸门时要清掉上一次失败遮罩，并绕开已经完成的启动初始化任务缓存。
            _viewModel.AppUpdate.ClearStartupUpdateError();
            IsStartupBlockedByAppUpdate = false;
            _startupInitializationTask = null;
            await InitializeForStartupAsync();
            if (IsStartupBlockedByAppUpdate)
            {
                return;
            }

            ActivateForScannerInput();
            ContinueStartupAfterShown();
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteError("Startup", $"startup retry after app update failure failed error={ex.GetType().Name} message={ex.Message}", exception: ex);
            _viewModel.StatusMessage = ex.Message;
            Close();
        }
    }

    internal static async Task<AppUpdateCoordinatorResult> RunStartupAppUpdateCheckCoreAsync(
        Func<Task<AppUpdateCoordinatorResult>> checkAsync,
        Func<TimeSpan, Task> delayAsync,
        Action<Exception> reportException,
        Action<AppUpdateCoordinatorResult>? reportFinalFailure = null)
    {
        try
        {
            var result = await checkAsync();
            if (IsUnconfiguredAppUpdateCenter(result))
            {
                // 中文注释：未配置更新中心是本机部署缺口，不能把门店 POS 阻断在启动页；其他检查失败仍走重试和阻断。
                return result;
            }

            if (result.Status is AppUpdateCoordinatorStatus.CheckFailed or AppUpdateCoordinatorStatus.PolicyFailed)
            {
                // 启动阶段的检查失败和策略失败都不能直接放行，只安排一次短延迟重试。
                await delayAsync(StartupUpdateRetryDelay);
                var retryResult = await checkAsync();
                if (retryResult.Status is AppUpdateCoordinatorStatus.CheckFailed or AppUpdateCoordinatorStatus.PolicyFailed)
                {
                    // 二次失败必须进入前台可见的阻断态，避免门店无感知地继续运行旧版本。
                    reportFinalFailure?.Invoke(retryResult);
                }

                return retryResult;
            }

            return result;
        }
        catch (Exception ex)
        {
            reportException(ex);
            return AppUpdateCoordinatorResult.CheckFailed();
        }
    }

    internal static bool ShouldContinueStartupAfterAppUpdateCheck(AppUpdateCoordinatorResult result)
    {
        return result.Status switch
        {
            AppUpdateCoordinatorStatus.NoUpdate
                or AppUpdateCoordinatorStatus.OptionalDeclined
                or AppUpdateCoordinatorStatus.OptionalReady
                or AppUpdateCoordinatorStatus.InstallFailed => true,
            _ => IsUnconfiguredAppUpdateCenter(result)
        };
    }

    private static bool IsUnconfiguredAppUpdateCenter(AppUpdateCoordinatorResult result)
    {
        return result.Status == AppUpdateCoordinatorStatus.CheckFailed &&
            string.Equals(
                result.ErrorCode,
                UnconfiguredAppUpdateCenterErrorCode,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatAppUpdateStatus(AppUpdateCoordinatorResult result)
    {
        var template = LocalizationResourceProvider.Instance[result.StatusKey];
        return result.StatusArgs.Length == 0
            ? template
            : string.Format(LocalizationResourceProvider.Instance.CurrentCulture, template, result.StatusArgs);
    }

    private static string ResolveStartupAppUpdateFailureMessage(AppUpdateCoordinatorResult result)
    {
        if (result.StatusArgs.Length > 0 &&
            result.StatusArgs[0] is string message &&
            !string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return FormatAppUpdateStatus(result);
    }

    private void MainWindowSourceInitialized(object? sender, EventArgs e)
    {
        _displayTopologyService.AttachWorkAreaConstraint(this);
        WindowsShellIdentityService.ApplyWindowIdentity(this);
        WindowsShellIdentityService.ApplyWindowIcon(this);
        _hwndSource = (HwndSource?)PresentationSource.FromVisual(this);
        _hwndSource?.AddHook(MainWindowMessageHook);
    }

    private void MainWindowClosed(object? sender, EventArgs e)
    {
        PreviewKeyDown -= MainWindowPreviewKeyDown;
        PreviewMouseDown -= MainWindowUserInput;
        PreviewMouseMove -= MainWindowUserInput;
        PreviewMouseWheel -= MainWindowUserInput;
        PreviewTouchDown -= MainWindowUserInput;
        _hwndSource?.RemoveHook(MainWindowMessageHook);
        _rawScannerService.Stop();
        _viewModel.Dispose();
    }

    private IntPtr MainWindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        return ProcessRawScannerWindowMessage(
            _viewModel.AppUpdate.IsForceUpdateBlocking,
            msg,
            hwnd,
            wParam,
            lParam,
            _rawScannerService.ProcessWindowMessage,
            ref handled);
    }

    private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _uiPriorityCoordinator.NotifyUserInput();
        if (IsKeyboardScannerFallbackBlocked())
        {
            _keyboardScannerFallback.Clear();
            if (_viewModel.AppUpdate.IsForceUpdateBlocking)
            {
                // 强制更新期间必须阻断键盘扫码兜底，避免遮罩背后的收银界面继续被条码修改。
                e.Handled = true;
            }

            return;
        }

        var result = _keyboardScannerFallback.Process(e.Key, DateTimeOffset.Now);
        if (result is null)
        {
            return;
        }

        if (_viewModel.TryProcessKeyboardScannerInput(result))
        {
            e.Handled = true;
        }
    }

    private void MainWindowUserInput(object? sender, InputEventArgs e)
    {
        _uiPriorityCoordinator.NotifyUserInput();
    }

    private bool IsKeyboardScannerFallbackBlocked()
    {
        var focusedElement = Keyboard.FocusedElement;
        return ShouldBlockKeyboardScannerFallback(
            _viewModel.AppUpdate.IsForceUpdateBlocking,
            IsTextInputElement(focusedElement),
            IsFocusedElementVisible(focusedElement));
    }

    internal static bool ShouldBlockKeyboardScannerFallback(
        bool isForceUpdateBlocking,
        bool isTextInputFocused,
        bool isFocusedElementVisible)
    {
        return isForceUpdateBlocking ||
            ShouldBlockKeyboardScannerFallback(isTextInputFocused, isFocusedElementVisible);
    }

    internal static bool ShouldBlockKeyboardScannerFallback(
        bool isTextInputFocused,
        bool isFocusedElementVisible)
    {
        return isTextInputFocused && isFocusedElementVisible;
    }

    internal static bool ShouldBlockRawScannerWindowMessage(bool isForceUpdateBlocking, int messageId)
    {
        return isForceUpdateBlocking && messageId == RawInputMessageId;
    }

    internal static IntPtr ProcessRawScannerWindowMessage(
        bool isForceUpdateBlocking,
        int messageId,
        IntPtr hwnd,
        IntPtr wParam,
        IntPtr lParam,
        WindowMessageProcessor processWindowMessage,
        ref bool handled)
    {
        if (ShouldBlockRawScannerWindowMessage(isForceUpdateBlocking, messageId))
        {
            // 中文注释：强更阻断遮罩打开后连 WM_INPUT 也要吞掉，避免 raw scanner 绕过键盘兜底限制。
            handled = true;
            return IntPtr.Zero;
        }

        return processWindowMessage(hwnd, messageId, wParam, lParam, ref handled);
    }

    private static bool IsTextInputElement(object? focusedElement)
    {
        return focusedElement is TextBoxBase or PasswordBox or ComboBox;
    }

    private static bool IsFocusedElementVisible(object? focusedElement)
    {
        return focusedElement is not UIElement uiElement ||
            uiElement.IsVisible &&
            PresentationSource.FromDependencyObject(uiElement) is not null;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}

internal sealed class KeyboardScannerFallbackBuffer
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMilliseconds(120);
    private const int MinBarcodeLength = 3;
    private readonly System.Text.StringBuilder _buffer = new();
    private DateTimeOffset _lastInputAt = DateTimeOffset.MinValue;

    public string? Process(Key key, DateTimeOffset timestamp)
    {
        if (key == Key.Enter)
        {
            return Complete();
        }

        if (!TryMapCharacter(key, out var character))
        {
            Clear();
            return null;
        }

        if (_buffer.Length > 0 && timestamp - _lastInputAt > ScanTimeout)
        {
            _buffer.Clear();
        }

        _buffer.Append(character);
        _lastInputAt = timestamp;
        return null;
    }

    public void Clear()
    {
        _buffer.Clear();
        _lastInputAt = DateTimeOffset.MinValue;
    }

    private string? Complete()
    {
        var barcode = _buffer.ToString();
        Clear();
        return barcode.Length >= MinBarcodeLength ? barcode : null;
    }

    private static bool TryMapCharacter(Key key, out char character)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            character = (char)('0' + (key - Key.D0));
            return true;
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            character = (char)('0' + (key - Key.NumPad0));
            return true;
        }

        if (key >= Key.A && key <= Key.Z)
        {
            character = (char)('A' + (key - Key.A));
            return true;
        }

        character = key switch
        {
            Key.OemMinus or Key.Subtract => '-',
            Key.OemPlus or Key.Add => '+',
            Key.OemPeriod or Key.Decimal => '.',
            Key.OemComma => ',',
            Key.Space => ' ',
            _ => '\0'
        };

        return character != '\0';
    }
}
