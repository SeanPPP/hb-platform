using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

[Collection(ConsoleLogGlobalStateTestCollection.Name)]
public sealed class ConsoleLogFileMirrorTests
{
    [Fact]
    public async Task File_mirror_does_not_recreate_worker_after_stop_wins_the_gate()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var consoleLogType = typeof(ConsoleLog);
        var gate = consoleLogType.GetField("FileLogGate", flags)!.GetValue(null)!;
        var stoppedField = consoleLogType.GetField("_fileLogStopped", flags)!;
        var stopTaskField = consoleLogType.GetField("_fileLogStopTask", flags)!;
        var workerField = consoleLogType.GetField("_fileLogWorker", flags)!;
        var writeFileLog = consoleLogType.GetMethod("WriteFileLog", flags)!;
        var previousPath = Environment.GetEnvironmentVariable("HBPOS_CLIENT_LOG_FILE");
        var logPath = Path.Combine(Path.GetTempPath(), $"hbpos-file-log-race-{Guid.NewGuid():N}.log");
        object? leakedWorker = null;

        try
        {
            Environment.SetEnvironmentVariable("HBPOS_CLIENT_LOG_FILE", logPath);
            lock (gate)
            {
                workerField.SetValue(null, null);
                stopTaskField.SetValue(null, null);
                stoppedField.SetValue(null, 0);
            }

            using var writersStarted = new CountdownEvent(4);
            var writerErrors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var writers = Enumerable.Range(0, 4)
                .Select(index => new Thread(() =>
                {
                    writersStarted.Signal();
                    try
                    {
                        writeFileLog.Invoke(null, [$"race-{index}"]);
                    }
                    catch (Exception exception)
                    {
                        writerErrors.Enqueue(exception);
                    }
                })
                {
                    IsBackground = true
                })
                .ToArray();

            Monitor.Enter(gate);
            try
            {
                foreach (var writer in writers)
                {
                    writer.Start();
                }

                Assert.True(writersStarted.Wait(TimeSpan.FromSeconds(2)));
                Assert.True(SpinWait.SpinUntil(
                    () => writers.All(writer => (writer.ThreadState & ThreadState.WaitSleepJoin) != 0),
                    TimeSpan.FromSeconds(2)));

                // 精确模拟 StopFileLogAsync 已持锁完成状态切换、但旧写线程仍在门外等待的交错。
                stoppedField.SetValue(null, 1);
                stopTaskField.SetValue(null, Task.CompletedTask);
                workerField.SetValue(null, null);
            }
            finally
            {
                Monitor.Exit(gate);
            }

            foreach (var writer in writers)
            {
                Assert.True(writer.Join(TimeSpan.FromSeconds(2)));
            }

            Assert.Empty(writerErrors);
            leakedWorker = workerField.GetValue(null);
        }
        finally
        {
            if (leakedWorker is not null)
            {
                var stopAsync = leakedWorker.GetType().GetMethod("StopAsync")!;
                await (Task)stopAsync.Invoke(leakedWorker, [CancellationToken.None])!;
            }

            lock (gate)
            {
                workerField.SetValue(null, null);
                stopTaskField.SetValue(null, null);
                stoppedField.SetValue(null, 0);
            }

            Environment.SetEnvironmentVariable("HBPOS_CLIENT_LOG_FILE", previousPath);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }

        Assert.Null(leakedWorker);
    }

    [Fact]
    public async Task File_mirror_flushes_accepted_lines_in_fifo_order_and_stops_idempotently()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"hbpos-file-log-{Guid.NewGuid():N}.log");
        var previousPath = Environment.GetEnvironmentVariable("HBPOS_CLIENT_LOG_FILE");
        var firstToken = $"file-log-first-{Guid.NewGuid():N}";
        var secondToken = $"file-log-second-{Guid.NewGuid():N}";

        try
        {
            Environment.SetEnvironmentVariable("HBPOS_CLIENT_LOG_FILE", logPath);

            ConsoleLog.Write("FileMirror", firstToken);
            ConsoleLog.Write("FileMirror", secondToken);
            var droppedBefore = ConsoleLog.DroppedFileLogLineCount;
            var samples = new long[128];
            for (var index = 0; index < samples.Length; index++)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                ConsoleLog.Write("FileMirror", $"file-log-p99-{index}");
                stopwatch.Stop();
                samples[index] = stopwatch.ElapsedTicks;
            }

            Array.Sort(samples);
            var p99 = TimeSpan.FromSeconds(samples[(int)Math.Ceiling(samples.Length * 0.99d) - 1] / (double)System.Diagnostics.Stopwatch.Frequency);
            Assert.True(p99 < TimeSpan.FromMilliseconds(2), $"ConsoleLog.Write p99 was {p99.TotalMilliseconds:F3} ms.");
            Assert.Equal(droppedBefore, ConsoleLog.DroppedFileLogLineCount);
            using var flushTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ConsoleLog.FlushFileLogAsync(flushTimeout.Token);

            var lines = await File.ReadAllLinesAsync(logPath);
            var firstIndex = Array.FindIndex(lines, line => line.Contains(firstToken, StringComparison.Ordinal));
            var secondIndex = Array.FindIndex(lines, line => line.Contains(secondToken, StringComparison.Ordinal));
            Assert.True(firstIndex >= 0);
            Assert.True(secondIndex > firstIndex);

            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ConsoleLog.StopFileLogAsync(stopTimeout.Token);
            await ConsoleLog.StopFileLogAsync(stopTimeout.Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HBPOS_CLIENT_LOG_FILE", previousPath);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }
}
