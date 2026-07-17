using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf;

public partial class MainWindow : Window
{
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

    private void CashierLoginOverlayPasswordBoxPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            _viewModel.CashierBarcodeInput = passwordBox.Password;
        }
    }

    private void CashierLoginOverlayPasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ExecuteCashierLoginCommandFromOverlay();
    }

    private void CashierLoginOverlayKeyboardButtonClick(object sender, RoutedEventArgs e)
    {
        var key = (sender as Button)?.Tag as string;
        var nextValue = ApplyCashierBarcodeKeyboardInput(CashierLoginOverlayPasswordBox.Password, key);

        // 关键逻辑：屏幕键盘只服务登录遮罩，扫码枪仍由同一个 PasswordBox 接收输入。
        CashierLoginOverlayPasswordBox.Password = nextValue;
        _viewModel.CashierBarcodeInput = nextValue;
        CashierLoginOverlayPasswordBox.Focus();
        Keyboard.Focus(CashierLoginOverlayPasswordBox);
    }

    internal static string ApplyCashierBarcodeKeyboardInput(string current, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return current;
        }

        if (string.Equals(key, "Clear", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (string.Equals(key, "Back", StringComparison.OrdinalIgnoreCase))
        {
            return current.Length == 0
                ? string.Empty
                : current[..^1];
        }

        return key.Length == 1 && char.IsDigit(key[0])
            ? current + key
            : current;
    }

    private void CashierLoginOverlayButtonClick(object sender, RoutedEventArgs e)
    {
        ClearCashierLoginOverlayInputAfterLogin();
    }

    private void ExecuteCashierLoginCommandFromOverlay()
    {
        var command = _viewModel.LoginCashierCommand;
        if (!command.CanExecute(null))
        {
            return;
        }

        command.Execute(null);
        ClearCashierLoginOverlayInputAfterLogin();
    }

    private void ClearCashierLoginOverlayInputAfterLogin()
    {
        // 关键逻辑：PasswordBox 不能普通绑定，登录命令清空 VM 后还要同步清掉屏幕上的敏感输入。
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(CashierLoginOverlayPasswordBox.Clear));
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

    private void OperationAuthorizationOverlayIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 关键逻辑：遮罩开关都清空两路未完成输入，授权条码不得泄漏到下一页面。
        _keyboardScannerFallback.Clear();
        _rawScannerService.ClearPendingInput();
        if (e.NewValue is not true)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                OperationAuthorizationCancelButton.Focus();
                Keyboard.Focus(OperationAuthorizationCancelButton);
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
            ReportStartupAppUpdateException,
            ReportStartupAppUpdateFailure);
    }

    private void ReportStartupAppUpdateException(Exception ex)
    {
        ConsoleLog.WriteError("Startup", $"app update startup check failed error={ex.GetType().Name} message={ex.Message}", exception: ex);
    }

    private static void ReportStartupAppUpdateFailure(AppUpdateCoordinatorResult result)
    {
        ConsoleLog.WriteError(
            "Startup",
            $"app update startup check allowed status={result.Status} errorCode={result.ErrorCode ?? "<null>"} errorMessage={result.ErrorMessage ?? "<null>"}");
    }

    internal static async Task<AppUpdateCoordinatorResult> RunStartupAppUpdateCheckCoreAsync(
        Func<Task<AppUpdateCoordinatorResult>> checkAsync,
        Action<Exception> reportException,
        Action<AppUpdateCoordinatorResult>? reportFailure = null)
    {
        try
        {
            var result = await checkAsync();
            if (result.Status is AppUpdateCoordinatorStatus.CheckFailed or AppUpdateCoordinatorStatus.PolicyFailed)
            {
                // 启动自动检查不可用时只记录一次并静默放行；强制更新状态仍由启动闸门阻断。
                reportFailure?.Invoke(result);
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
                or AppUpdateCoordinatorStatus.InstallFailed
                or AppUpdateCoordinatorStatus.CheckFailed
                or AppUpdateCoordinatorStatus.PolicyFailed => true,
            _ => false
        };
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
            _viewModel.ConfirmationDialog.IsOpen,
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
        var isOperationAuthorizationOpen = _viewModel.OperationAuthorization?.IsPromptOpen == true;
        if (isOperationAuthorizationOpen)
        {
            if (e.Key == Key.Escape)
            {
                _keyboardScannerFallback.Clear();
                _rawScannerService.ClearPendingInput();
                _viewModel.OperationAuthorization?.Cancel();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab)
            {
                return;
            }
        }

        var isForceUpdateBlocking = _viewModel.AppUpdate.IsForceUpdateBlocking;
        var isConfirmationDialogOpen = _viewModel.ConfirmationDialog.IsOpen;
        if (IsKeyboardScannerFallbackBlocked(isForceUpdateBlocking, isConfirmationDialogOpen))
        {
            _keyboardScannerFallback.Clear();
            if (!isForceUpdateBlocking && isConfirmationDialogOpen && IsAltF4(e))
            {
                // 关键逻辑：普通确认不改变 Alt+F4 的窗口关闭语义，强制更新遮罩仍吞掉全部按键。
                return;
            }

            if (ShouldConsumeBlockedKeyboardInput(isForceUpdateBlocking, isConfirmationDialogOpen, e.Key))
            {
                e.Handled = true;
            }

            return;
        }

        var timestamp = DateTimeOffset.Now;
        var result = _keyboardScannerFallback.Process(e.Key, timestamp, e.ImeProcessedKey);
        if (result is null)
        {
            // 授权遮罩打开时吞掉普通按键；仅 Tab 用于焦点导航，Enter 只用于完成扫码。
            e.Handled = isOperationAuthorizationOpen;
            return;
        }

        if (_rawScannerService is IScannerInputDeduplicator deduplicator &&
            !deduplicator.TryAcceptScanDelivery(result, "keyboard-fallback", timestamp))
        {
            // 同一把 HID 扫码枪可能同时产生 Raw Input 与普通键盘事件，只允许其中一路提交。
            e.Handled = true;
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

    private bool IsKeyboardScannerFallbackBlocked(bool isForceUpdateBlocking, bool isConfirmationDialogOpen)
    {
        var focusedElement = Keyboard.FocusedElement;
        return ShouldBlockKeyboardScannerFallback(
            isForceUpdateBlocking,
            isConfirmationDialogOpen,
            IsTextInputElement(focusedElement),
            IsFocusedElementVisible(focusedElement));
    }

    internal static bool ShouldBlockKeyboardScannerFallback(
        bool isForceUpdateBlocking,
        bool isConfirmationDialogOpen,
        bool isTextInputFocused,
        bool isFocusedElementVisible)
    {
        return isForceUpdateBlocking || isConfirmationDialogOpen ||
            ShouldBlockKeyboardScannerFallback(isTextInputFocused, isFocusedElementVisible);
    }

    internal static bool ShouldBlockKeyboardScannerFallback(
        bool isTextInputFocused,
        bool isFocusedElementVisible)
    {
        return isTextInputFocused && isFocusedElementVisible;
    }

    internal static bool ShouldBlockRawScannerWindowMessage(
        bool isForceUpdateBlocking,
        bool isConfirmationDialogOpen,
        int messageId)
    {
        return (isForceUpdateBlocking || isConfirmationDialogOpen) && messageId == RawInputMessageId;
    }

    internal static IntPtr ProcessRawScannerWindowMessage(
        bool isForceUpdateBlocking,
        bool isConfirmationDialogOpen,
        int messageId,
        IntPtr hwnd,
        IntPtr wParam,
        IntPtr lParam,
        WindowMessageProcessor processWindowMessage,
        ref bool handled)
    {
        if (ShouldBlockRawScannerWindowMessage(isForceUpdateBlocking, isConfirmationDialogOpen, messageId))
        {
            // 关键逻辑：全局遮罩打开时吞掉 WM_INPUT，避免 raw scanner 绕过界面门禁修改订单。
            handled = true;
            return IntPtr.Zero;
        }

        return processWindowMessage(hwnd, messageId, wParam, lParam, ref handled);
    }

    internal static bool ShouldConsumeBlockedKeyboardInput(
        bool isForceUpdateBlocking,
        bool isConfirmationDialogOpen,
        Key key)
    {
        if (isForceUpdateBlocking)
        {
            return true;
        }

        return isConfirmationDialogOpen && key is not Key.Escape and not Key.Tab and not Key.Enter;
    }

    private static bool IsAltF4(KeyEventArgs e)
    {
        return e.SystemKey == Key.F4 ||
            e.Key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
    }

    private void ConfirmationDialogIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        // 关键逻辑：确认遮罩打开时同时清空键盘兜底与 Raw Input 残留，避免超时派发修改购物车。
        _keyboardScannerFallback.Clear();
        _rawScannerService.ClearPendingInput();

        // 关键逻辑：遮罩出现后默认聚焦取消按钮，Enter 默认走安全分支，Tab 仍可在两按钮间循环。
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                ConfirmationCancelButton.Focus();
                Keyboard.Focus(ConfirmationCancelButton);
            }));
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

    public string? Process(Key key, DateTimeOffset timestamp, Key imeProcessedKey = Key.None)
    {
        if (key == Key.ImeProcessed && imeProcessedKey != Key.None)
        {
            // 中文输入法会把扫码枪字母键标记为 ImeProcessed，必须还原物理按键后再进入扫码缓冲。
            key = imeProcessedKey;
        }

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
