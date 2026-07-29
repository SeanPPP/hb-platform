using Hbpos.Client.Wpf;
using Hbpos.Client.Wpf.Services;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Hbpos.Client.Tests;

[Collection(ShutdownTimingTestCollection.Name)]
public sealed class AppShutdownCoordinatorTests
{
    [Fact]
    public void GetOrStartRemainingBudget_uses_one_deadline_without_waiting()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero));
        var coordinator = new AppShutdownCoordinator(timeProvider, TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(3), coordinator.GetOrStartRemainingBudget());
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(2), coordinator.GetOrStartRemainingBudget());
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.Zero, coordinator.GetOrStartRemainingBudget());
    }

    [Fact]
    public async Task PrepareAsync_runs_registered_steps_in_order_once()
    {
        var coordinator = new AppShutdownCoordinator();
        var calls = new List<string>();
        coordinator.RegisterStep(
            "host",
            200,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                calls.Add("host");
                return Task.CompletedTask;
            });
        coordinator.RegisterStep(
            "offline",
            100,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                calls.Add("offline");
                return Task.CompletedTask;
            });

        await Task.WhenAll(coordinator.PrepareAsync(), coordinator.PrepareAsync());

        Assert.Equal(["offline", "host"], calls);
        Assert.True(coordinator.IsPreparationStarted);
        Assert.True(coordinator.IsPrepared);
    }

    [Fact]
    public async Task PrepareAsync_continues_after_step_failure()
    {
        var coordinator = new AppShutdownCoordinator();
        var secondStepCalled = false;
        coordinator.RegisterStep(
            "offline",
            100,
            TimeSpan.FromSeconds(1),
            _ => throw new InvalidOperationException("offline failed"));
        coordinator.RegisterStep(
            "host",
            200,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                secondStepCalled = true;
                return Task.CompletedTask;
            });

        await coordinator.PrepareAsync();

        Assert.True(secondStepCalled);
        Assert.True(coordinator.IsPrepared);
    }

    [Fact]
    public async Task PrepareAsync_continues_after_step_timeout()
    {
        var coordinator = new AppShutdownCoordinator();
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOutStepCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStepCalled = false;
        coordinator.RegisterStep(
            "offline",
            100,
            TimeSpan.FromMilliseconds(20),
            async _ =>
            {
                try
                {
                    await neverCompletes.Task;
                }
                finally
                {
                    timedOutStepCompleted.TrySetResult();
                }
            });
        coordinator.RegisterStep(
            "host",
            200,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                secondStepCalled = true;
                return Task.CompletedTask;
            });

        try
        {
            await coordinator.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(1));

            Assert.True(secondStepCalled);
            Assert.True(coordinator.IsPrepared);
        }
        finally
        {
            neverCompletes.TrySetResult();
            await timedOutStepCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task PrepareAsync_logs_actual_remaining_budget_timeout()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero));
        var coordinator = new AppShutdownCoordinator(
            timeProvider,
            totalBudget: TimeSpan.FromSeconds(3));
        _ = coordinator.GetOrStartRemainingBudget();
        timeProvider.Advance(TimeSpan.FromMilliseconds(2950));
        coordinator.RegisterStep(
            "remaining-budget-log",
            100,
            TimeSpan.FromSeconds(1),
            cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();

        void CaptureLine(string line)
        {
            if (line.Contains("step=remaining-budget-log", StringComparison.Ordinal))
            {
                lines.Enqueue(line);
            }
        }

        ConsoleLog.LineWritten += CaptureLine;
        try
        {
            await coordinator.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            ConsoleLog.LineWritten -= CaptureLine;
        }

        var timeoutLine = Assert.Single(lines);
        Assert.Contains("timeoutMs=50", timeoutLine, StringComparison.Ordinal);
        Assert.DoesNotContain("timeoutMs=1000", timeoutLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_bounds_synchronous_delegate_prefix_and_continues()
    {
        var coordinator = new AppShutdownCoordinator();
        var blockingStepCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStepCalled = false;
        coordinator.RegisterStep(
            "blocking",
            100,
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                try
                {
                    Thread.Sleep(100);
                    return Task.CompletedTask;
                }
                finally
                {
                    blockingStepCompleted.TrySetResult();
                }
            });
        coordinator.RegisterStep(
            "next",
            200,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                secondStepCalled = true;
                return Task.CompletedTask;
            });

        try
        {
            await coordinator.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await blockingStepCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }

        Assert.True(secondStepCalled);
        Assert.True(coordinator.IsPrepared);
    }

    [Fact]
    public async Task PrepareAsync_respects_total_budget_across_steps()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero));
        var coordinator = new AppShutdownCoordinator(timeProvider, TimeSpan.FromSeconds(3));
        var hostStepCalled = false;
        coordinator.RegisterStep(
            "offline",
            100,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                timeProvider.Advance(TimeSpan.FromSeconds(3));
                return Task.CompletedTask;
            });
        coordinator.RegisterStep(
            "host",
            200,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                hostStepCalled = true;
                return Task.CompletedTask;
            });

        await coordinator.PrepareAsync();

        Assert.True(coordinator.IsPrepared);
        Assert.False(hostStepCalled);
    }

    [Fact]
    public async Task PrepareAsync_does_not_wait_for_blocking_cancellation_callback()
    {
        var coordinator = new AppShutdownCoordinator();
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOutStepCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextStepCalled = false;
        coordinator.RegisterStep(
            "blocking-cancel",
            100,
            TimeSpan.FromMilliseconds(20),
            async token =>
            {
                using var registration = token.Register(() =>
                {
                    callbackStarted.TrySetResult();
                    try
                    {
                        Thread.Sleep(100);
                    }
                    finally
                    {
                        callbackCompleted.TrySetResult();
                    }
                });
                try
                {
                    await neverCompletes.Task;
                }
                finally
                {
                    timedOutStepCompleted.TrySetResult();
                }
            });
        coordinator.RegisterStep(
            "next",
            200,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                nextStepCalled = true;
                return Task.CompletedTask;
            });

        try
        {
            await coordinator.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(1));
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.True(nextStepCalled);
            Assert.True(coordinator.IsPrepared);
        }
        finally
        {
            neverCompletes.TrySetResult();
            await timedOutStepCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            if (callbackStarted.Task.IsCompleted)
            {
                await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }
    }

    [Fact]
    public async Task RegisterStep_rejects_late_registration()
    {
        var coordinator = new AppShutdownCoordinator();
        coordinator.RegisterStep("host", 100, TimeSpan.FromSeconds(1), _ => Task.CompletedTask);
        await coordinator.PrepareAsync();

        Assert.Throws<InvalidOperationException>(() =>
            coordinator.RegisterStep("late", 200, TimeSpan.FromSeconds(1), _ => Task.CompletedTask));
    }

    [Fact]
    public void App_OnExit_is_synchronous()
    {
        var onExit = typeof(App).GetMethod("OnExit", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(onExit);
        Assert.Null(onExit!.GetCustomAttribute<AsyncStateMachineAttribute>());
    }

    [Fact]
    public void App_OnExit_starts_shared_budget_before_startup_cleanup()
    {
        var source = ReadAppSource();
        var start = source.IndexOf("protected override void OnExit", StringComparison.Ordinal);
        var end = source.IndexOf("private static void LogShutdownCleanupFailure", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var onExitBody = source[start..end];

        var budgetStart = onExitBody.IndexOf("GetOrStartRemainingBudget()", StringComparison.Ordinal);
        var firstCleanup = onExitBody.IndexOf("FinishStartupExperience()", StringComparison.Ordinal);

        Assert.True(budgetStart >= 0);
        Assert.True(firstCleanup > budgetStart);
    }

    [Fact]
    public void App_exit_fallback_runs_shutdown_preparation_when_window_close_was_bypassed()
    {
        var coordinator = new AppShutdownCoordinator();
        var called = false;
        coordinator.RegisterStep(
            "host",
            100,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            });

        var prepared = App.WaitForShutdownPreparation(coordinator, TimeSpan.FromSeconds(1));

        Assert.True(prepared);
        Assert.True(called);
    }

    [Fact]
    public async Task App_exit_fallback_waits_for_already_started_preparation()
    {
        var coordinator = new AppShutdownCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.RegisterStep(
            "host",
            100,
            TimeSpan.FromSeconds(1),
            async _ =>
            {
                started.TrySetResult();
                await release.Task;
            });
        var firstPreparation = coordinator.PrepareAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var exitWait = Task.Run(() =>
            App.WaitForShutdownPreparation(coordinator, TimeSpan.FromSeconds(1)));
        release.TrySetResult();

        Assert.True(await exitWait);
        await firstPreparation;
    }

    [Fact]
    public async Task App_host_dispose_fallback_is_bounded()
    {
        var disposable = new BlockingDisposable();
        var startedAt = Stopwatch.GetTimestamp();

        var disposed = App.DisposeHostWithinTimeout(
            disposable,
            TimeSpan.FromMilliseconds(20),
            "test");

        Assert.False(disposed);
        Assert.True(
            Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(1),
            "host dispose fallback should not block application exit");
        await disposable.Completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void App_host_dispose_does_not_start_after_budget_is_exhausted()
    {
        var disposable = new CountingDisposable();

        var disposed = App.DisposeHostWithinTimeout(
            disposable,
            TimeSpan.Zero,
            "test-zero-budget");

        Assert.False(disposed);
        Assert.Equal(0, disposable.DisposeCallCount);
    }

    private sealed class BlockingDisposable : IDisposable
    {
        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            try
            {
                Thread.Sleep(100);
            }
            finally
            {
                Completed.TrySetResult();
            }
        }
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCallCount { get; private set; }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private static string ReadAppSource()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var path = Path.Combine(
                    current.FullName,
                    "apps",
                    "pos-wpf",
                    "src",
                    "Hbpos.Client.Wpf",
                    "App.xaml.cs");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate App.xaml.cs.");
    }
}
