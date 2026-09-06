using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDetailQueryFlightsTests
{
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TimeSpan TestLimit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task 同键共享计算_一个调用方取消不影响其他调用方()
    {
        var flights = new SalesDetailQueryFlights();
        var result = Signal<string>();
        using var firstCancellation = new CancellationTokenSource();
        var calls = 0;
        CancellationToken sharedToken = default;
        Task<string> Read(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            sharedToken = token;
            return result.Task;
        }
        var first = flights.RunAsync("same", Read, Budget, firstCancellation.Token);
        var second = flights.RunAsync("same", Read, Budget, CancellationToken.None);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestLimit));
        Assert.False(sharedToken.IsCancellationRequested);
        result.SetResult("fresh");
        Assert.Equal("fresh", await second.WaitAsync(TestLimit));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task 最后调用方离开_立即取消共享计算并释放作用域()
    {
        var flights = new SalesDetailQueryFlights();
        var released = Signal<bool>();
        using var cancellation = new CancellationTokenSource();
        async Task<string> Read(CancellationToken token)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return "unreachable";
            }
            finally { released.TrySetResult(true); }
        }
        var result = flights.RunAsync("abandoned", Read, Budget, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.WaitAsync(TestLimit));
        Assert.True(await released.Task.WaitAsync(TestLimit));
    }

    [Fact]
    public async Task 已取消的旧查询结束_不能移除相同键的新查询()
    {
        var flights = new SalesDetailQueryFlights();
        var oldRelease = Signal<string>();
        var oldExited = Signal<bool>();
        var newRelease = Signal<string>();
        using var cancellation = new CancellationTokenSource();
        var first = flights.RunAsync("generation", async token =>
        {
            try { return await oldRelease.Task; }
            finally { oldExited.TrySetResult(true); }
        }, Budget, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestLimit));

        var newCalls = 0;
        Task<string> ReadNew(CancellationToken token)
        {
            Interlocked.Increment(ref newCalls);
            return newRelease.Task;
        }
        var second = flights.RunAsync("generation", ReadNew, Budget, CancellationToken.None);
        Assert.Equal(1, newCalls);
        oldRelease.SetResult("old");
        await oldExited.Task.WaitAsync(TestLimit);
        var third = flights.RunAsync("generation", ReadNew, Budget, CancellationToken.None);
        newRelease.SetResult("new");
        Assert.Equal("new", await second.WaitAsync(TestLimit));
        Assert.Equal("new", await third.WaitAsync(TestLimit));
        Assert.Equal(1, newCalls);
    }

    [Fact]
    public async Task 共享预算到期_取消计算且允许后续重试()
    {
        var flights = new SalesDetailQueryFlights();
        var released = Signal<bool>();
        var result = flights.RunAsync("timeout", async token =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return "unreachable";
            }
            finally { released.TrySetResult(true); }
        }, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.WaitAsync(TestLimit));
        Assert.True(await released.Task.WaitAsync(TestLimit));
        Assert.Equal("retry", await flights.RunAsync("timeout", _ => Task.FromResult("retry"), Budget, CancellationToken.None));
    }

    [Fact]
    public async Task 已取消的调用方不启动查询()
    {
        var flights = new SalesDetailQueryFlights();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flights.RunAsync("never", _ =>
        {
            calls++;
            return Task.FromResult("unexpected");
        }, Budget, cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task 失败结果不保留_下次允许重新计算()
    {
        var flights = new SalesDetailQueryFlights();
        await Assert.ThrowsAsync<InvalidOperationException>(() => flights.RunAsync<string>(
            "failure", _ => throw new InvalidOperationException("expected"), Budget, CancellationToken.None));
        Assert.Equal("recovered", await flights.RunAsync("failure", _ => Task.FromResult("recovered"), Budget, CancellationToken.None));
    }
}
