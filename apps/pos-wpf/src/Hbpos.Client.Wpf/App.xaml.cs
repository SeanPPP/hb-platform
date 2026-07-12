using System.Windows;
using System.Windows.Threading;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hbpos.Client.Wpf;

public partial class App : Application
{
    private const int SplashShownPercent = 10;
    private const int HostBuiltPercent = 30;
    private const int HostStartedPercent = 50;
    private const int MainWindowPreparingPercent = 65;
    private const int MainWindowInitializedPercent = 85;
    private const int StartupCompletedPercent = 100;
    private const int HostShutdownTimeoutSeconds = 2;

    private IHost? _host;
    private SingleInstanceStartupLease? _startupLease;
    private StartupSplashWindow? _startupSplashWindow;
    private StartupProgressState? _startupProgressState;
    private bool _startupGateReleaseScheduled;
    private bool _globalExceptionObserversRegistered;

    protected override async void OnStartup(StartupEventArgs e)
    {
        WindowsShellIdentityService.ApplyProcessIdentity();

        var startupOptions = AppStartupOptions.FromArgs(e.Args);
        var startupGuard = new SingleInstanceStartupGuard();
        var startupResult = startupGuard.TryAcquire(startupOptions.PreviewMode);
        if (!startupResult.CanStart)
        {
            Shutdown();
            return;
        }

        _startupLease = startupResult.Lease;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!startupOptions.PreviewMode)
        {
            _startupProgressState = new StartupProgressState();
            _startupProgressState.SetStage(SplashShownPercent, StartupText("startup.stage.starting"));
            _startupSplashWindow = new StartupSplashWindow(_startupProgressState);
            _startupSplashWindow.Show();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
        }

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices(services =>
                {
                    services.AddHbposClientServices(startupOptions);
                })
                .Build();
            var applicationLogOptions = _host.Services.GetRequiredService<ApplicationLogOptions>();
            ConsoleLog.ConfigureCenterDefaults(applicationLogOptions.ToDefaults());
            ConsoleLog.ConfigureCenterSink(_host.Services.GetRequiredService<IApplicationLogSink>());
            RegisterGlobalExceptionObservers();
            _startupProgressState?.SetStage(HostBuiltPercent, StartupText("startup.stage.initializingServices"));

            await _host.StartAsync();
            var localization = _host.Services.GetRequiredService<ILocalizationService>();
            LocalizationResourceProvider.Instance.Configure(localization);
            ButtonFeedbackRouter.Register(_host.Services.GetRequiredService<IUserFeedbackService>());
            _startupProgressState?.SetStage(HostStartedPercent, localization.T("startup.stage.startingLocalComponents"));

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _startupProgressState?.SetStage(MainWindowPreparingPercent, localization.T("startup.stage.loadingProducts"));
            await mainWindow.InitializeForStartupAsync();
            mainWindow.StartupCompleted += (_, _) => ScheduleStartupGateReleaseAfterClickGuardDelay();
            _startupProgressState?.SetStage(MainWindowInitializedPercent, localization.T("startup.stage.preparingMainWindow"));
            MainWindow = mainWindow;
            FinishStartupExperience();
            mainWindow.Show();
            // 主窗口句柄会在 Show 前为扫码初始化提前创建；Show 后再刷新一次，确保任务栏按钮拿到正确图标。
            WindowsShellIdentityService.ApplyWindowIdentity(mainWindow);
            WindowsShellIdentityService.ApplyWindowIcon(mainWindow);
            if (mainWindow.IsStartupBlockedByAppUpdate)
            {
                // 已显示阻断窗口后释放启动闸门；运行中互斥仍会保护单实例。
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                ScheduleStartupGateReleaseAfterClickGuardDelay();
                base.OnStartup(e);
                return;
            }

            mainWindow.ActivateForScannerInput();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            mainWindow.ContinueStartupAfterShown();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            ScheduleStartupGateReleaseAfterClickGuardDelay();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            // 启动入口是 async void，异常不能继续抛回调度器，避免未观察异常导致进程崩溃。
            ConsoleLog.WriteError("Startup", $"startup failed error={ex.GetType().Name} message={ex.Message}", exception: ex);
            FinishStartupExperience();
            if (_host is not null)
            {
                try
                {
                    _host.Dispose();
                }
                catch (Exception disposeEx)
                {
                    ConsoleLog.WriteError("Startup", $"host dispose after startup failure failed error={disposeEx.GetType().Name} message={disposeEx.Message}", exception: disposeEx);
                }

                _host = null;
            }

            ResetGlobalLogging();
            _startupLease?.Dispose();
            _startupLease = null;
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            FinishStartupExperience();

            if (_host is not null)
            {
                try
                {
                    var mainViewModel = _host.Services.GetService<MainViewModel>();
                    if (mainViewModel is not null)
                    {
                        await mainViewModel.ReportOfflineForShutdownAsync();
                    }

                    // 退出入口同样是 async void，StopAsync 失败时记录日志后继续释放资源。
                    // 两条 uploader 与本地 writer 共享宿主的 2 秒退出总预算。
                    await _host.StopAsync(TimeSpan.FromSeconds(HostShutdownTimeoutSeconds));
                }
                catch (Exception ex)
                {
                    ConsoleLog.WriteError("Shutdown", $"host stop failed error={ex.GetType().Name} message={ex.Message}", exception: ex);
                }
                finally
                {
                    _host.Dispose();
                    _host = null;
                }
            }

            _startupLease?.Dispose();
            _startupLease = null;
        }
        catch (Exception ex)
        {
            ConsoleLog.WriteError("Shutdown", $"shutdown cleanup failed error={ex.GetType().Name} message={ex.Message}", exception: ex);
        }
        finally
        {
            ResetGlobalLogging();
            base.OnExit(e);
        }
    }

    private void RegisterGlobalExceptionObservers()
    {
        if (_globalExceptionObserversRegistered)
        {
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _globalExceptionObserversRegistered = true;
    }

    private void ResetGlobalLogging()
    {
        if (_globalExceptionObserversRegistered)
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            _globalExceptionObserversRegistered = false;
        }

        ConsoleLog.ConfigureCenterSink(null);
        ConsoleLog.ConfigureCenterDefaults(ApplicationLogDefaults.Default);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _ = sender;
        // 这里只观察并记录，不设置 Handled，保留 WPF 原有崩溃语义。
        ConsoleLog.WriteError(
            "UnhandledException",
            $"dispatcher unhandled exception type={e.Exception.GetType().Name}",
            exception: e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _ = sender;
        if (e.ExceptionObject is Exception exception)
        {
            ConsoleLog.WriteError(
                "UnhandledException",
                $"app domain unhandled exception type={exception.GetType().Name} terminating={e.IsTerminating}",
                exception: exception);
            return;
        }

        ConsoleLog.WriteError(
            "UnhandledException",
            $"app domain unhandled non-exception object terminating={e.IsTerminating}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _ = sender;
        // 不调用 SetObserved，日志观察器不能改变 TaskScheduler 的既有异常策略。
        ConsoleLog.WriteError(
            "UnhandledException",
            "unobserved task exception",
            exception: e.Exception);
    }

    private void ScheduleStartupGateReleaseAfterClickGuardDelay()
    {
        if (_startupGateReleaseScheduled)
        {
            return;
        }

        _startupGateReleaseScheduled = true;
        _ = ReleaseStartupGateAfterClickGuardDelayAsync();
    }

    private async Task ReleaseStartupGateAfterClickGuardDelayAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        _startupLease?.ReleaseStartupGate();
    }

    private void FinishStartupExperience()
    {
        if (_startupSplashWindow is null)
        {
            return;
        }

        _startupProgressState?.SetStage(StartupCompletedPercent, StartupText("startup.stage.completed"));
        _startupSplashWindow.Close();
        _startupSplashWindow = null;
        _startupProgressState = null;
    }

    private static string StartupText(string key) => LocalizationResourceProvider.Instance[key];
}
