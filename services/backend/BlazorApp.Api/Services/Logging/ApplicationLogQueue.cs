using System.Threading.Channels;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.Logging
{
    public interface IApplicationLogQueue
    {
        bool TryEnqueue(ApplicationLogIngestItemDto item);
        ValueTask<ApplicationLogIngestItemDto> ReadAsync(CancellationToken cancellationToken);
        bool TryRead(out ApplicationLogIngestItemDto? item);
        void RecordFlushFailure(int batchSize, string? reason);
        ApplicationLogQueueRuntimeSnapshot GetRuntimeSnapshot();
    }

    public class ApplicationLogQueueRuntimeSnapshot
    {
        public int DroppedOldestCount { get; init; }
        public int EnqueueFailureCount { get; init; }
        public int FailedFlushBatchCount { get; init; }
        public int FailedFlushLogCount { get; init; }
        public int LastFailedFlushBatchSize { get; init; }
        public string? LastFailedFlushReason { get; init; }
    }

    public class ApplicationLogQueue : IApplicationLogQueue
    {
        private readonly object _runtimeStateLock = new();
        private readonly Channel<ApplicationLogIngestItemDto> _channel;
        private int _droppedOldestCount;
        private int _enqueueFailureCount;
        private int _failedFlushBatchCount;
        private int _failedFlushLogCount;
        private int _lastFailedFlushBatchSize;
        private string? _lastFailedFlushReason;

        public ApplicationLogQueue(int capacity = 5000)
        {
            _channel = Channel.CreateBounded<ApplicationLogIngestItemDto>(
                new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                },
                _ =>
                {
                    // DropOldest 不会让业务线程阻塞，但要把被顶掉的数量留下来供汇总查询。
                    Interlocked.Increment(ref _droppedOldestCount);
                }
            );
        }

        public bool TryEnqueue(ApplicationLogIngestItemDto item)
        {
            try
            {
                var written = _channel.Writer.TryWrite(item);
                if (!written)
                    Interlocked.Increment(ref _enqueueFailureCount);

                return written;
            }
            catch
            {
                Interlocked.Increment(ref _enqueueFailureCount);
                return false;
            }
        }

        public ValueTask<ApplicationLogIngestItemDto> ReadAsync(
            CancellationToken cancellationToken
        )
        {
            return _channel.Reader.ReadAsync(cancellationToken);
        }

        public bool TryRead(out ApplicationLogIngestItemDto? item)
        {
            return _channel.Reader.TryRead(out item);
        }

        public void RecordFlushFailure(int batchSize, string? reason)
        {
            Interlocked.Increment(ref _failedFlushBatchCount);
            Interlocked.Add(ref _failedFlushLogCount, batchSize);

            lock (_runtimeStateLock)
            {
                _lastFailedFlushBatchSize = batchSize;
                _lastFailedFlushReason = reason;
            }
        }

        public ApplicationLogQueueRuntimeSnapshot GetRuntimeSnapshot()
        {
            lock (_runtimeStateLock)
            {
                return new ApplicationLogQueueRuntimeSnapshot
                {
                    DroppedOldestCount = Volatile.Read(ref _droppedOldestCount),
                    EnqueueFailureCount = Volatile.Read(ref _enqueueFailureCount),
                    FailedFlushBatchCount = Volatile.Read(ref _failedFlushBatchCount),
                    FailedFlushLogCount = Volatile.Read(ref _failedFlushLogCount),
                    LastFailedFlushBatchSize = _lastFailedFlushBatchSize,
                    LastFailedFlushReason = _lastFailedFlushReason,
                };
            }
        }
    }
}
