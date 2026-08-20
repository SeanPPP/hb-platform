using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Hbpos.Client.Wpf.Services;

internal enum SingleInstanceStartupStatus
{
    Acquired,
    PreviewMode,
    AnotherStartupInProgress,
    ExistingInstanceCouldNotBeStopped
}

internal sealed class SingleInstanceStartupResult
{
    private SingleInstanceStartupResult(SingleInstanceStartupStatus status, SingleInstanceStartupLease? lease)
    {
        Status = status;
        Lease = lease;
    }

    public SingleInstanceStartupStatus Status { get; }

    public SingleInstanceStartupLease? Lease { get; }

    public bool CanStart => Status is SingleInstanceStartupStatus.Acquired or SingleInstanceStartupStatus.PreviewMode;

    public static SingleInstanceStartupResult PreviewMode()
    {
        return new SingleInstanceStartupResult(SingleInstanceStartupStatus.PreviewMode, null);
    }

    public static SingleInstanceStartupResult AnotherStartupInProgress()
    {
        return new SingleInstanceStartupResult(SingleInstanceStartupStatus.AnotherStartupInProgress, null);
    }

    public static SingleInstanceStartupResult ExistingInstanceCouldNotBeStopped()
    {
        return new SingleInstanceStartupResult(SingleInstanceStartupStatus.ExistingInstanceCouldNotBeStopped, null);
    }

    public static SingleInstanceStartupResult Acquired(SingleInstanceStartupLease lease)
    {
        return new SingleInstanceStartupResult(SingleInstanceStartupStatus.Acquired, lease);
    }
}

internal sealed class SingleInstanceStartupLease : IDisposable
{
    private Semaphore? _startupGate;
    private Mutex? _runningInstance;
    private bool _startupGateHeld;
    private bool _runningInstanceHeld;

    public SingleInstanceStartupLease(Semaphore startupGate, Mutex runningInstance)
    {
        _startupGate = startupGate;
        _runningInstance = runningInstance;
        _startupGateHeld = true;
        _runningInstanceHeld = true;
    }

    public void ReleaseStartupGate()
    {
        if (!_startupGateHeld || _startupGate is null)
        {
            return;
        }

        _startupGate.Release();
        ConsoleLog.Write("startup-guard", "startup gate released");
        _startupGateHeld = false;
        _startupGate.Dispose();
        _startupGate = null;
    }

    public void Dispose()
    {
        ReleaseStartupGate();

        if (_runningInstanceHeld && _runningInstance is not null)
        {
            _runningInstance.ReleaseMutex();
            _runningInstanceHeld = false;
        }

        _runningInstance?.Dispose();
        _runningInstance = null;
    }
}

internal sealed record SingleInstanceStartupGuardOptions(
    string StartupGateName,
    string RunningInstanceMutexName,
    TimeSpan GracefulCloseTimeout,
    TimeSpan KillWaitTimeout,
    TimeSpan RunningInstanceWaitTimeout)
{
    public static SingleInstanceStartupGuardOptions Default { get; } = new(
        @"Global\Hbpos.Client.Wpf.StartupGate",
        @"Global\Hbpos.Client.Wpf.SingleInstance",
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5));
}

internal sealed class SingleInstanceStartupGuard
{
    private readonly IRunningProcessProvider _processProvider;
    private readonly SingleInstanceStartupGuardOptions _options;

    public SingleInstanceStartupGuard()
        : this(new SystemRunningProcessProvider(), SingleInstanceStartupGuardOptions.Default)
    {
    }

    public SingleInstanceStartupGuard(
        IRunningProcessProvider processProvider,
        SingleInstanceStartupGuardOptions options)
    {
        _processProvider = processProvider;
        _options = options;
    }

    public async Task<SingleInstanceStartupResult> TryAcquireAsync(bool previewMode)
    {
        if (previewMode)
        {
            return SingleInstanceStartupResult.PreviewMode();
        }

        var startupGate = new Semaphore(1, 1, _options.StartupGateName);
        if (!startupGate.WaitOne(TimeSpan.Zero))
        {
            ConsoleLog.Write("startup-guard", "another startup is already preparing the app");
            startupGate.Dispose();
            return SingleInstanceStartupResult.AnotherStartupInProgress();
        }

        Mutex? runningInstance = null;
        var runningInstanceHeld = false;
        try
        {
            using var processes = _processProvider.GetSiblingProcesses();
            var replaceableProcesses = SingleInstanceProcessSelector.FindReplaceableProcesses(_processProvider, processes.Items);
            ConsoleLog.Write("startup-guard", $"replaceable process count={replaceableProcesses.Count}");
            // 不使用 ConfigureAwait(false)：后续 Mutex 必须回到调用上下文获得并由同一 UI 线程释放。
            await StopExistingInstancesAsync(replaceableProcesses);

            runningInstance = new Mutex(false, _options.RunningInstanceMutexName);
            runningInstanceHeld = await WaitForRunningInstanceAsync(runningInstance);

            if (!runningInstanceHeld)
            {
                ConsoleLog.Write("startup-guard", "existing instance did not release the running mutex before timeout");
                runningInstance.Dispose();
                runningInstance = null;
                startupGate.Release();
                startupGate.Dispose();
                return SingleInstanceStartupResult.ExistingInstanceCouldNotBeStopped();
            }

            return SingleInstanceStartupResult.Acquired(new SingleInstanceStartupLease(startupGate, runningInstance));
        }
        catch
        {
            // 超时之外的异常：同时释放 startup gate 与已创建的 mutex 句柄，避免句柄泄漏。
            try
            {
                if (runningInstanceHeld && runningInstance is not null)
                {
                    runningInstance.ReleaseMutex();
                }
            }
            finally
            {
                runningInstance?.Dispose();
                startupGate.Release();
                startupGate.Dispose();
            }

            throw;
        }
    }

    private async Task StopExistingInstancesAsync(IReadOnlyList<IRunningProcess> processes)
    {
        foreach (var process in processes)
        {
            if (process.HasExited)
            {
                continue;
            }

            ConsoleLog.Write("startup-guard", $"requesting close for pid={process.Id}");
            var closeRequested = process.CloseMainWindow();
            if (closeRequested && await process.WaitForExitAsync(_options.GracefulCloseTimeout))
            {
                ConsoleLog.Write("startup-guard", $"pid={process.Id} exited after close request");
                continue;
            }

            if (process.HasExited)
            {
                ConsoleLog.Write("startup-guard", $"pid={process.Id} exited before kill request");
                continue;
            }

            ConsoleLog.Write("startup-guard", $"killing pid={process.Id}");
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(_options.KillWaitTimeout);
        }
    }

    private async Task<bool> WaitForRunningInstanceAsync(Mutex runningInstance)
    {
        var timer = Stopwatch.StartNew();
        var timeout = _options.RunningInstanceWaitTimeout < TimeSpan.Zero
            ? TimeSpan.Zero
            : _options.RunningInstanceWaitTimeout;
        var maximumPollDelay = TimeSpan.FromMilliseconds(50);

        while (true)
        {
            try
            {
                if (runningInstance.WaitOne(TimeSpan.Zero))
                {
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                // 仅 running mutex 的遗弃可按成功处理：Windows 已把所有权授予当前线程。
                ConsoleLog.Write(
                    "startup-guard",
                    "warning: running instance mutex was abandoned; Windows granted ownership, continuing startup");
                return true;
            }

            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            // 每次只做零等待 Mutex 获取，延迟期间让 WPF Dispatcher 继续处理消息。
            await Task.Delay(remaining < maximumPollDelay ? remaining : maximumPollDelay);
        }
    }
}

internal static class SingleInstanceProcessSelector
{
    public static IReadOnlyList<IRunningProcess> FindReplaceableProcesses(
        IRunningProcessProvider processProvider,
        IReadOnlyList<IRunningProcess> processes)
    {
        var currentExecutablePath = NormalizeExecutablePath(processProvider.CurrentExecutablePath);
        if (currentExecutablePath is null)
        {
            return [];
        }

        return processes
            .Where(process =>
                process.Id != processProvider.CurrentProcessId &&
                !process.HasExited &&
                string.Equals(
                    NormalizeExecutablePath(process.ExecutablePath),
                    currentExecutablePath,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string? NormalizeExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal interface IRunningProcessProvider
{
    int CurrentProcessId { get; }

    string? CurrentExecutablePath { get; }

    ProcessSnapshot GetSiblingProcesses();
}

internal sealed class ProcessSnapshot : IDisposable
{
    public ProcessSnapshot(IReadOnlyList<IRunningProcess> items)
    {
        Items = items;
    }

    public IReadOnlyList<IRunningProcess> Items { get; }

    public void Dispose()
    {
        foreach (var process in Items)
        {
            process.Dispose();
        }
    }
}

internal interface IRunningProcess : IDisposable
{
    int Id { get; }

    string? ExecutablePath { get; }

    bool HasExited { get; }

    bool CloseMainWindow();

    Task<bool> WaitForExitAsync(TimeSpan timeout);

    void Kill(bool entireProcessTree);
}

internal sealed class SystemRunningProcessProvider : IRunningProcessProvider
{
    public int CurrentProcessId => Environment.ProcessId;

    public string? CurrentExecutablePath => Environment.ProcessPath ?? TryReadProcessPath(Process.GetCurrentProcess());

    public ProcessSnapshot GetSiblingProcesses()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(currentProcess.ProcessName)
            .Select(process => (IRunningProcess)new SystemRunningProcess(process))
            .ToArray();

        return new ProcessSnapshot(processes);
    }

    private static string? TryReadProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed class SystemRunningProcess : IRunningProcess
{
    private readonly Process _process;

    public SystemRunningProcess(Process process)
    {
        _process = process;
    }

    public int Id => _process.Id;

    public string? ExecutablePath
    {
        get
        {
            try
            {
                return _process.MainModule?.FileName;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public bool CloseMainWindow()
    {
        try
        {
            return _process.CloseMainWindow();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        try
        {
            if (HasExited)
            {
                return true;
            }

            var timeoutMilliseconds = Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);
            using var timeoutCancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(timeoutMilliseconds));
            await _process.WaitForExitAsync(timeoutCancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return HasExited;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public void Kill(bool entireProcessTree)
    {
        try
        {
            _process.Kill(entireProcessTree);
        }
        catch (Exception)
        {
            // The process may exit between the check and the kill request.
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
