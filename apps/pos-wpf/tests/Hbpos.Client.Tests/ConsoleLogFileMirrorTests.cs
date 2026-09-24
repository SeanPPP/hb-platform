using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

[Collection(ConsoleLogGlobalStateTestCollection.Name)]
public sealed class ConsoleLogFileMirrorTests
{
    [Fact]
    public async Task File_mirror_continues_after_one_write_failure_and_flush_marker_completes()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var workerType = typeof(ConsoleLog).GetNestedType(
            "FileLogWorker",
            System.Reflection.BindingFlags.NonPublic)!;
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-file-log-recovery-{Guid.NewGuid():N}");
        var blockedDirectory = Path.Combine(tempRoot, "blocked");
        var logPath = Path.Combine(blockedDirectory, "client.log");
        var droppedCount = 0;
        object? worker = null;

        try
        {
            Directory.CreateDirectory(tempRoot);
            await File.WriteAllTextAsync(blockedDirectory, "blocks directory creation");
            worker = Activator.CreateInstance(
                workerType,
                flags,
                binder: null,
                args: [logPath, (Action)(() => Interlocked.Increment(ref droppedCount))],
                culture: null)!;
            var tryWrite = workerType.GetMethod("TryWrite", flags)!;
            var flushAsync = workerType.GetMethod("FlushAsync", flags)!;

            Assert.True((bool)tryWrite.Invoke(worker, ["first"])!);
            using (var flushTimeout = new CancellationTokenSource(AsyncTestWaitSupport.DefaultTimeout))
            {
                await (Task)flushAsync.Invoke(worker, [flushTimeout.Token])!;
            }
            Assert.Equal(1, Volatile.Read(ref droppedCount));

            File.Delete(blockedDirectory);
            Directory.CreateDirectory(blockedDirectory);
            Assert.True((bool)tryWrite.Invoke(worker, ["second"])!);
            using (var flushTimeout = new CancellationTokenSource(AsyncTestWaitSupport.DefaultTimeout))
            {
                await (Task)flushAsync.Invoke(worker, [flushTimeout.Token])!;
            }

            Assert.Equal(["second"], await File.ReadAllLinesAsync(logPath));
        }
        finally
        {
            if (worker is not null)
            {
                var stopAsync = workerType.GetMethod("StopAsync", flags)!;
                await (Task)stopAsync.Invoke(worker, [CancellationToken.None])!;
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

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

                Assert.True(writersStarted.Wait(AsyncTestWaitSupport.DefaultTimeout));
                Assert.True(SpinWait.SpinUntil(
                    () => writers.All(writer => (writer.ThreadState & ThreadState.WaitSleepJoin) != 0),
                    AsyncTestWaitSupport.DefaultTimeout));

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
                Assert.True(writer.Join(AsyncTestWaitSupport.DefaultTimeout));
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
            // 连续写入 128 行，验证有界通道容量内一行都不会被丢弃（drop 计数不变）。
            // 注意：这里刻意不再断言 ConsoleLog.Write 的 p99 墙钟延迟——Write 除文件入队外还会同步走
            // Console/Debug/Trace/OutputDebugString，在共享 CI runner 上这些外部路径的抖动不可控；
            // "写入不会等待文件消费者"由 FileLogWorker.TryWrite 走 Channel.Writer.TryWrite 结构性保证。
            const int burstLineCount = 128;
            for (var index = 0; index < burstLineCount; index++)
            {
                ConsoleLog.Write("FileMirror", $"file-log-burst-{index}");
            }

            Assert.Equal(droppedBefore, ConsoleLog.DroppedFileLogLineCount);
            // flush 要等约 130 次逐行 open/append/close 落盘，Windows runner 上 AV 扫描会显著放大耗时，使用共享预算。
            using var flushTimeout = new CancellationTokenSource(AsyncTestWaitSupport.DefaultTimeout);
            await ConsoleLog.FlushFileLogAsync(flushTimeout.Token);

            var lines = await File.ReadAllLinesAsync(logPath);
            var firstIndex = Array.FindIndex(lines, line => line.Contains(firstToken, StringComparison.Ordinal));
            var secondIndex = Array.FindIndex(lines, line => line.Contains(secondToken, StringComparison.Ordinal));
            Assert.True(firstIndex >= 0, $"未在文件中找到首行 token；文件共 {lines.Length} 行。");
            Assert.True(secondIndex > firstIndex, $"第二行 token 顺序错误：firstIndex={firstIndex} secondIndex={secondIndex}。");

            using var stopTimeout = new CancellationTokenSource(AsyncTestWaitSupport.DefaultTimeout);
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
