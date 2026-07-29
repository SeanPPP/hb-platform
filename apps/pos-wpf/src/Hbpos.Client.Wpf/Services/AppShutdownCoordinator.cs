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
}

public sealed class AppShutdownCoordinator : IAppShutdownCoordinator
{
    private static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly List<ShutdownStep> _steps = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _totalBudget;
    private Task? _prepareTask;
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
                await ExecuteStepSafeAsync(step, preparationCancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _isPrepared, 1);
        }
    }

    private async Task ExecuteStepSafeAsync(
        ShutdownStep step,
        CancellationToken preparationCancellation)
    {
        var remainingBudget = GetOrStartRemainingBudget();
        if (remainingBudget <= TimeSpan.Zero)
        {
            ConsoleLog.WriteError("Shutdown", $"shutdown total budget expired before step={step.Name}");
            return;
        }

        var timeout = remainingBudget < step.Timeout ? remainingBudget : step.Timeout;
        var stepCancellation = new CancellationTokenSource();
        var disposeSynchronously = true;
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
            CancelAndDisposeAfterCompletion(step.Name, stepCancellation, stepTask);
            ConsoleLog.WriteError(
                "Shutdown",
                $"shutdown step timed out step={step.Name} timeoutMs={step.Timeout.TotalMilliseconds:0}");
        }
        catch (OperationCanceledException) when (preparationCancellation.IsCancellationRequested)
        {
            disposeSynchronously = false;
            CancelAndDisposeAfterCompletion(step.Name, stepCancellation, stepTask);
            ConsoleLog.WriteError(
                "Shutdown",
                $"shutdown total budget expired step={step.Name}");
        }
        catch (Exception ex)
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
    }

    private static void CancelAndDisposeAfterCompletion(
        string stepName,
        CancellationTokenSource cancellation,
        Task stepTask)
    {
        // 取消回调属于外部代码，可能阻塞；绝不能在退出关键路径同步 Cancel/Dispose。
        var cancelTask = Task.Run(() =>
        {
            try
            {
                cancellation.Cancel();
            }
            catch (Exception ex)
            {
                LogStepFailure($"{stepName}-cancel", ex);
            }
        });
        var observedStepTask = stepTask.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _ = Task.WhenAll(cancelTask, observedStepTask).ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
