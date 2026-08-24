using System.Runtime.ExceptionServices;

namespace Hbpos.Client.Wpf.Services;

public interface IAppShutdownCoordinator
{
    bool IsPreparationStarted { get; }

    bool IsPrepared { get; }

    TimeSpan GetOrStartRemainingBudget();

    void RegisterStep(
        string name,
        int order,
        TimeSpan timeout,
        Func<CancellationToken, Task> executeAsync);

    Task PrepareAsync(CancellationToken cancellationToken = default);

    Task ObserveDetachedFailuresAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class AppShutdownCoordinator : IAppShutdownCoordinator
{
    private static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly List<ShutdownStep> _steps = [];
    private readonly List<Task> _detachedStepTasks = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _totalBudget;
    private Task? _prepareTask;
    private ExceptionDispatchInfo? _lateFatalException;
    private DateTimeOffset? _deadlineUtc;
    private int _registrationIndex;
    private int _isPrepared;

    public AppShutdownCoordinator(TimeProvider? timeProvider = null, TimeSpan? totalBudget = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _totalBudget = totalBudget ?? DefaultTotalBudget;
        if (_totalBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBudget));
        }
    }

    public bool IsPreparationStarted
    {
        get
        {
            lock (_sync)
            {
                return _prepareTask is not null;
            }
        }
    }

    public bool IsPrepared => Volatile.Read(ref _isPrepared) == 1;

    public TimeSpan GetOrStartRemainingBudget()
    {
        lock (_sync)
        {
            _deadlineUtc ??= _timeProvider.GetUtcNow() + _totalBudget;
            var remaining = _deadlineUtc.Value - _timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public void RegisterStep(
        string name,
        int order,
        TimeSpan timeout,
        Func<CancellationToken, Task> executeAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(executeAsync);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        lock (_sync)
        {
            if (_prepareTask is not null)
            {
                throw new InvalidOperationException("Shutdown preparation has already started.");
            }

            if (_steps.Any(step => string.Equals(step.Name, name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Shutdown step '{name}' is already registered.");
            }

            _steps.Add(new ShutdownStep(name, order, _registrationIndex++, timeout, executeAsync));
        }
    }

    public Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_lateFatalException is { } lateFatalException)
            {
                // 中文注释：窗口关闭步骤超时后，OnExit 会再次等待同一个协调器；迟到的致命异常必须在这里重新暴露。
                return Task.FromException(lateFatalException.SourceException);
            }

            _deadlineUtc ??= _timeProvider.GetUtcNow() + _totalBudget;
            return _prepareTask ??= PrepareCoreAsync(
                _steps
                    .OrderBy(step => step.Order)
                    .ThenBy(step => step.RegistrationIndex)
                    .ToArray(),
                cancellationToken);
        }
    }

    private async Task PrepareCoreAsync(
        IReadOnlyList<ShutdownStep> steps,
        CancellationToken preparationCancellation)
    {
        try
        {
            foreach (var step in steps)
            {
                var detachedStepTask = await ExecuteStepSafeAsync(
                        step,
                        preparationCancellation)
                    .ConfigureAwait(false);
                if (detachedStepTask is not null)
                {
                    TrackDetachedStepTask(detachedStepTask);
                }

                ThrowIfLateFatalException();
            }

            ThrowIfLateFatalException();
        }
        finally
        {
            Volatile.Write(ref _isPrepared, 1);
        }
    }

    private async Task<Task?> ExecuteStepSafeAsync(
        ShutdownStep step,
        CancellationToken preparationCancellation)
    {
        var remainingBudget = GetOrStartRemainingBudget();
        if (remainingBudget <= TimeSpan.Zero)
        {
            ConsoleLog.WriteError("Shutdown", $"shutdown total budget expired before step={step.Name}");
            return null;
        }

        var timeout = remainingBudget < step.Timeout ? remainingBudget : step.Timeout;
        var stepCancellation = new CancellationTokenSource();
        var disposeSynchronously = true;
        Task? detachedStepTask = null;
        // delegate 的同步段也必须离开 Dispatcher，否则在首次 await 前阻塞会绕过退出超时。
        var stepTask = Task.Run(
            () => step.ExecuteAsync(stepCancellation.Token),
            CancellationToken.None);

        try
        {
            await stepTask.WaitAsync(timeout, _timeProvider, preparationCancellation).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            disposeSynchronously = false;
            detachedStepTask = CancelAndDisposeAfterCompletion(
                step.Name,
                stepCancellation,
                stepTask);
            ConsoleLog.WriteError(
                "Shutdown",
                $"shutdown step timed out step={step.Name} timeoutMs={timeout.TotalMilliseconds:0}");
        }
        catch (OperationCanceledException) when (preparationCancellation.IsCancellationRequested)
        {
            disposeSynchronously = false;
            detachedStepTask = CancelAndDisposeAfterCompletion(
                step.Name,
                stepCancellation,
                stepTask);
            ConsoleLog.WriteError(
                "Shutdown",
                $"shutdown total budget expired step={step.Name}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LogStepFailure(step.Name, ex);
        }
        finally
        {
            if (disposeSynchronously)
            {
                stepCancellation.Dispose();
            }
        }

        return detachedStepTask;
    }

    private static Task CancelAndDisposeAfterCompletion(
        string stepName,
        CancellationTokenSource cancellation,
        Task stepTask)
    {
        // 取消回调属于外部代码，可能阻塞；绝不能在退出关键路径同步 Cancel/Dispose。
        var cancelTask = Task.Run(
            cancellation.Cancel,
            CancellationToken.None);
        return ObserveDetachedStepAndDisposeAsync(
            stepName,
            cancellation,
            cancelTask,
            stepTask);
    }

    public async Task ObserveDetachedFailuresAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfLateFatalException();
        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        List<Task> pendingTasks;
        lock (_sync)
        {
            pendingTasks = _detachedStepTasks
                .Where(task => !task.IsCompletedSuccessfully)
                .ToList();
        }

        var deadlineUtc = _timeProvider.GetUtcNow() + timeout;
        while (pendingTasks.Count > 0)
        {
            var remainingBudget = deadlineUtc - _timeProvider.GetUtcNow();
            if (remainingBudget <= TimeSpan.Zero)
            {
                // 中文注释：预算边界与 detached task 完成可能同时发生；返回前再检查一次已记录的致命异常。
                ThrowIfLateFatalException();
                return;
            }

            Task completedTask;
            try
            {
                completedTask = await Task
                    .WhenAny(pendingTasks)
                    .WaitAsync(remainingBudget, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ThrowIfLateFatalException();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ThrowIfLateFatalException();
                throw;
            }

            pendingTasks.Remove(completedTask);
            // detached observer 只会以 OOM/SO fault；普通异常已在内部记录。
            await completedTask.ConfigureAwait(false);
        }

        ThrowIfLateFatalException();
    }

    private void TrackDetachedStepTask(Task detachedStepTask)
    {
        lock (_sync)
        {
            _detachedStepTasks.Add(detachedStepTask);
        }

        _ = detachedStepTask.ContinueWith(
            static (completedTask, state) =>
            {
                var coordinator = (AppShutdownCoordinator)state!;
                var fatalException = FindFatalException(completedTask.Exception!);
                if (fatalException is null)
                {
                    return;
                }

                coordinator.RecordLateFatalException(fatalException);
                LogStepFailure("late-fatal", fatalException);
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task ObserveDetachedStepAndDisposeAsync(
        string stepName,
        CancellationTokenSource cancellation,
        Task cancelTask,
        Task stepTask)
    {
        var fatalExceptionSource = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelObservationTask = ObserveDetachedTaskAsync(
            $"{stepName}-cancel",
            cancelTask,
            fatalExceptionSource);
        var stepObservationTask = ObserveDetachedTaskAsync(
            stepName,
            stepTask,
            fatalExceptionSource);
        var allObservationTasks = Task.WhenAll(cancelObservationTask, stepObservationTask);

        await Task.WhenAny(allObservationTasks, fatalExceptionSource.Task).ConfigureAwait(false);
        if (fatalExceptionSource.Task.IsCompletedSuccessfully)
        {
            _ = allObservationTasks.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                cancellation,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            var fatalException = await fatalExceptionSource.Task.ConfigureAwait(false);
            ExceptionDispatchInfo.Capture(fatalException).Throw();
        }

        await allObservationTasks.ConfigureAwait(false);
        cancellation.Dispose();
    }

    private static async Task ObserveDetachedTaskAsync(
        string stage,
        Task task,
        TaskCompletionSource<Exception> fatalExceptionSource)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 超时后由协调器主动取消的步骤属于预期结束，不重复记录 step-failed。
        }
        catch (Exception ex)
        {
            var fatalException = FindFatalException(ex);
            if (fatalException is not null)
            {
                fatalExceptionSource.TrySetResult(fatalException);
                return;
            }

            LogStepFailure(stage, ex);
        }
    }

    private void RecordLateFatalException(Exception fatalException)
    {
        lock (_sync)
        {
            _lateFatalException ??= ExceptionDispatchInfo.Capture(fatalException);
        }
    }

    private void ThrowIfLateFatalException()
    {
        ExceptionDispatchInfo? lateFatalException;
        lock (_sync)
        {
            lateFatalException = _lateFatalException;
        }

        lateFatalException?.Throw();
    }

    private static Exception? FindFatalException(Exception exception)
    {
        if (exception is OutOfMemoryException or StackOverflowException)
        {
            return exception;
        }

        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions)
            {
                var fatalException = FindFatalException(innerException);
                if (fatalException is not null)
                {
                    return fatalException;
                }
            }

            return null;
        }

        return exception.InnerException is null
            ? null
            : FindFatalException(exception.InnerException);
    }

    private static void LogStepFailure(string stepName, Exception ex)
    {
        ConsoleLog.WriteError(
            "Shutdown",
            $"shutdown step failed step={stepName} error={ex.GetType().Name} message={ex.Message}",
            exception: ex);
    }

    private sealed record ShutdownStep(
        string Name,
        int Order,
        int RegistrationIndex,
        TimeSpan Timeout,
        Func<CancellationToken, Task> ExecuteAsync);
}
