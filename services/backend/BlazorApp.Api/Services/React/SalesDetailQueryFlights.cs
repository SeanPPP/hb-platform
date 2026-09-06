namespace BlazorApp.Api.Services.React;

/// <summary>同条件报表共用计算，但每个调用方独立取消等待。</summary>
internal sealed class SalesDetailQueryFlights
{
    private sealed class Flight(TimeSpan timeout)
    {
        public CancellationTokenSource Cancellation { get; } = new(timeout);
        public TaskCompletionSource<object> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters { get; set; }
        public bool Completed { get; set; }
        public bool Canceling { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Flight> _flights = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> read,
        TimeSpan timeout,
        CancellationToken callerToken
    ) where T : class
    {
        callerToken.ThrowIfCancellationRequested();
        Flight flight;
        bool start;
        lock (_gate)
        {
            start = !_flights.TryGetValue(key, out flight!);
            if (start)
            {
                flight = new Flight(timeout);
                _flights.Add(key, flight);
            }
            flight.Waiters++;
        }

        if (start)
            _ = ExecuteAsync(key, flight, read);
        try
        {
            return (T)await flight.Completion.Task.WaitAsync(callerToken);
        }
        finally
        {
            Release(key, flight);
        }
    }

    private async Task ExecuteAsync<T>(string key, Flight flight, Func<CancellationToken, Task<T>> read)
        where T : class
    {
        var token = flight.Cancellation.Token;
        try
        {
            var value = await read(token);
            token.ThrowIfCancellationRequested();
            flight.Completion.TrySetResult(value);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            flight.Completion.TrySetCanceled(token);
        }
        catch (Exception error)
        {
            flight.Completion.TrySetException(error);
            // 所有等待者都已离开时，也观察后台计算异常；存活调用方仍会收到同一异常。
            _ = flight.Completion.Task.Exception;
        }
        finally
        {
            lock (_gate)
            {
                flight.Completed = true;
                RemoveCurrent(key, flight);
                if (!flight.Canceling)
                    flight.Cancellation.Dispose();
            }
        }
    }

    private void Release(string key, Flight flight)
    {
        lock (_gate)
        {
            flight.Waiters--;
            if (flight.Waiters != 0 || flight.Completed)
                return;
            // 最后一个等待者离开就摘除该代，后续查询不加入已取消的计算。
            RemoveCurrent(key, flight);
            flight.Canceling = true;
        }

        // 取消回调可能同步完成查询，不持字典锁调用，且延后 CTS 释放避免竞态。
        try
        {
            flight.Cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // Cancel 已通知全部回调；回调异常不能掩盖调用方本身的取消结果。
        }
        finally
        {
            lock (_gate)
            {
                flight.Canceling = false;
                if (flight.Completed)
                    flight.Cancellation.Dispose();
            }
        }
    }

    // 仅删除自己这一代，旧查询结束不能误删同 key 的新查询。
    private void RemoveCurrent(string key, Flight flight)
    {
        if (_flights.TryGetValue(key, out var current) && ReferenceEquals(current, flight))
            _flights.Remove(key);
    }
}
