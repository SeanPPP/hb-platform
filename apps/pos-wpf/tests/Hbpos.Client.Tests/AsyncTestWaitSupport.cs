using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Hbpos.Client.Tests;

/// <summary>
/// 测试项目共享的异步等待工具。通过 csproj 中的 <c>global using static</c> 引入，
/// 各测试文件直接调用 <see cref="WaitUntilAsync(Func{bool}, TimeSpan?, Func{string}?, string?)"/>，
/// 不要再在文件内复制私有副本，否则等待预算与失败信息会各自漂移
/// （历史上曾出现 200ms 到 5s 不等的十几份副本）。
/// </summary>
/// <remarks>
/// 等待对象应当是"已被触发、只差线程调度"的异步工作，而不是生产代码的真实超时。
/// 如果测试需要让生产代码"超时"，应向生产代码注入 <see cref="TimeProvider"/>
/// 或超时参数直接推进虚拟时间，而不是靠这里的墙钟预算去硬等。
/// </remarks>
internal static class AsyncTestWaitSupport
{
    /// <summary>
    /// 默认等待预算。条件满足时立即返回，所以放宽预算不会拖慢通过的测试；
    /// 它只决定 CI runner 负载抖动（线程池饥饿、Dispatcher 多跳调度、SQLite 冷启动）时的容忍上限。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>轮询间隔。</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// 轮询直到 <paramref name="condition"/> 为真；超出预算则以 <see cref="Assert.Fail(string)"/>
    /// 失败，并把条件表达式原文与可选的诊断信息一起输出，方便直接从 CI 日志定位。
    /// </summary>
    /// <param name="condition">被等待的条件。</param>
    /// <param name="timeout">等待预算，缺省为 <see cref="DefaultTimeout"/>。</param>
    /// <param name="diagnostics">失败时追加到断言消息的诊断内容，例如已捕获的日志行。</param>
    /// <param name="conditionExpression">由编译器填入的条件表达式原文，调用方不需要传。</param>
    public static Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        Func<string>? diagnostics = null,
        [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return WaitUntilAsync(() => Task.FromResult(condition()), timeout, diagnostics, conditionExpression);
    }

    /// <summary>
    /// <see cref="WaitUntilAsync(Func{bool}, TimeSpan?, Func{string}?, string?)"/> 的异步条件版本，
    /// 用于条件本身需要 I/O（例如读本地 SQLite）的场景。
    /// </summary>
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        Func<string>? diagnostics = null,
        [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var budget = timeout ?? DefaultTimeout;
        // 使用 Stopwatch 而非 DateTimeOffset.UtcNow：单调时钟不受系统时间回拨影响。
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (await condition())
            {
                return;
            }

            if (stopwatch.Elapsed >= budget)
            {
                break;
            }

            await Task.Delay(PollInterval);
        }

        // 预算耗尽后再检查一次：最后一轮 Delay 期间条件可能刚好满足，不应误报。
        if (await condition())
        {
            return;
        }

        Assert.Fail(BuildTimeoutMessage("等待条件", conditionExpression, budget, diagnostics));
    }

    /// <summary>
    /// 等待 <paramref name="task"/> 完成；超出预算则以带表达式原文和诊断信息的断言失败，
    /// 取代裸 <c>task.WaitAsync(TimeSpan)</c> 抛出的无上下文 <see cref="TimeoutException"/>。
    /// 任务本身的异常会原样传播。
    /// </summary>
    public static async Task WaitUntilCompletedAsync(
        this Task task,
        Func<string>? diagnostics = null,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(task))] string? taskExpression = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        await WaitForCompletionCoreAsync(task, timeout, diagnostics, taskExpression);
        await task;
    }

    /// <summary>
    /// <see cref="WaitUntilCompletedAsync(Task, Func{string}?, TimeSpan?, string?)"/> 的带返回值版本。
    /// </summary>
    public static async Task<T> WaitUntilCompletedAsync<T>(
        this Task<T> task,
        Func<string>? diagnostics = null,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(task))] string? taskExpression = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        await WaitForCompletionCoreAsync(task, timeout, diagnostics, taskExpression);
        return await task;
    }

    /// <summary>
    /// 把已捕获的日志行格式化为诊断文本，供 <c>diagnostics</c> 参数使用；没有日志时也给出明确提示。
    /// </summary>
    public static string DescribeCapturedLogs(IEnumerable<string> lines)
    {
        var snapshot = lines.ToArray();
        return snapshot.Length == 0
            ? "（未捕获到任何日志行）"
            : string.Join(Environment.NewLine, snapshot);
    }

    private static async Task WaitForCompletionCoreAsync(
        Task task,
        TimeSpan? timeout,
        Func<string>? diagnostics,
        string? taskExpression)
    {
        var budget = timeout ?? DefaultTimeout;
        // 不用 task.WaitAsync(budget)：它抛出的 TimeoutException 与任务自身抛出的无法区分。
        using var timeoutCts = new CancellationTokenSource();
        var timeoutTask = Task.Delay(budget, timeoutCts.Token);
        var winner = await Task.WhenAny(task, timeoutTask);
        if (!ReferenceEquals(winner, task))
        {
            Assert.Fail(BuildTimeoutMessage("等待任务完成", taskExpression, budget, diagnostics));
        }

        timeoutCts.Cancel();
    }

    private static string BuildTimeoutMessage(
        string what,
        string? expression,
        TimeSpan budget,
        Func<string>? diagnostics)
    {
        var message = $"{what}在 {budget.TotalSeconds:0.##}s 内未满足：{expression}";
        if (diagnostics is not null)
        {
            message += Environment.NewLine + "诊断信息：" + Environment.NewLine + diagnostics();
        }

        return message;
    }
}
