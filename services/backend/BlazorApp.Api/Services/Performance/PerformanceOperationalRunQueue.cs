using System.Threading.Channels;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Services.Performance;

public sealed record PerformanceOperationalRunTransition(
    string ExternalRunId,
    string Category,
    string Operation,
    string Status,
    DateTime OccurredAtUtc,
    int Attempt,
    int? Backlog
)
{
    public static PerformanceOperationalRunTransition Queued(
        string externalRunId,
        string category,
        string operation,
        DateTime occurredAtUtc,
        int? backlog = null,
        int attempt = 1
    ) => new(externalRunId, category, operation, "queued", occurredAtUtc, attempt, backlog);

    public static PerformanceOperationalRunTransition Started(
        string externalRunId,
        string category,
        string operation,
        DateTime occurredAtUtc,
        int? backlog = null,
        int attempt = 1
    ) => new(externalRunId, category, operation, "running", occurredAtUtc, attempt, backlog);

    public static PerformanceOperationalRunTransition Retry(
        string externalRunId,
        string category,
        string operation,
        DateTime occurredAtUtc,
        int? backlog = null,
        int attempt = 2
    ) => new(externalRunId, category, operation, "retry_wait", occurredAtUtc, attempt, backlog);

    public static PerformanceOperationalRunTransition Completed(
        string externalRunId,
        string category,
        string operation,
        string status,
        DateTime occurredAtUtc,
        int attempt = 1,
        int? backlog = null
    ) => new(externalRunId, category, operation, status, occurredAtUtc, attempt, backlog);
}

internal sealed record PerformanceOperationalRunQueueItem(
    Guid OutboxId,
    PerformanceOperationalRunTransitionOutbox? PendingOutbox,
    int InMemoryAttempt = 0
);

public sealed class PerformanceOperationalRunQueue
{
    internal const int DefaultFallbackCapacity = 4_096;
    private readonly Channel<PerformanceOperationalRunQueueItem> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PerformanceOperationalRunQueue> _logger;
    private long _droppedFallbackCount;

    public PerformanceOperationalRunQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<PerformanceOperationalRunQueue> logger
    )
        : this(scopeFactory, logger, DefaultFallbackCapacity) { }

    internal PerformanceOperationalRunQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<PerformanceOperationalRunQueue> logger,
        int fallbackCapacity
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<PerformanceOperationalRunQueueItem>(
            new BoundedChannelOptions(Math.Max(1, fallbackCapacity))
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
    }

    public bool TryWrite(PerformanceOperationalRunTransition transition)
    {
        var outbox = PerformanceOperationalRunWriterService.CreateOutbox(
            transition,
            DateTime.UtcNow
        );
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
            db.Insertable(outbox).ExecuteCommand();
            // 通知失败不影响可靠性，后台轮询仍会发现已持久化的 outbox。
            _channel.Writer.TryWrite(new PerformanceOperationalRunQueueItem(outbox.Id, null));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "运行状态 outbox 首次写入失败，转入有界内存回退，异常类型 {ExceptionType}",
                ex.GetType().Name
            );
            if (
                _channel.Writer.TryWrite(
                    new PerformanceOperationalRunQueueItem(outbox.Id, outbox)
                )
            )
            {
                return true;
            }
            ReportDroppedFallback("capacity");
            return false;
        }
    }

    internal bool Requeue(PerformanceOperationalRunQueueItem item)
    {
        if (_channel.Writer.TryWrite(item))
        {
            return true;
        }
        ReportDroppedFallback("capacity");
        return false;
    }

    internal void ReportDroppedFallback(string reason)
    {
        var total = Interlocked.Increment(ref _droppedFallbackCount);
        if (total == 1 || total % 100 == 0)
        {
            // 仅输出累计数量和固定原因，不记录任务、门店或外部运行标识。
            _logger.LogError(
                "运行状态内存回退已丢弃事件，累计 {DroppedCount}，原因 {Reason}",
                total,
                reason
            );
        }
    }

    internal long DroppedFallbackCount => Interlocked.Read(ref _droppedFallbackCount);

    internal IAsyncEnumerable<PerformanceOperationalRunQueueItem> ReadAllAsync(
        CancellationToken cancellationToken
    ) => _channel.Reader.ReadAllAsync(cancellationToken);
}

public static class PerformanceOperationalRunBridge
{
    private static PerformanceOperationalRunQueue? _queue;

    internal static void Configure(PerformanceOperationalRunQueue? queue) =>
        Volatile.Write(ref _queue, queue);

    public static void Publish(PerformanceOperationalRunTransition transition) =>
        Volatile.Read(ref _queue)?.TryWrite(transition);
}
