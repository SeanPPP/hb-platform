using System.Threading.Channels;
using BlazorApp.Shared.DTOs;

namespace Hbpos.Api.Logging;

internal sealed class CentralLogQueue
{
    private readonly Channel<ApplicationLogIngestItemDto> channel;
    private readonly object writerSyncRoot = new();
    private bool accepting = true;
    private long count;
    private long droppedOldestCount;

    public CentralLogQueue(int capacity)
    {
        channel = Channel.CreateBounded<ApplicationLogIngestItemDto>(
            new BoundedChannelOptions(Math.Max(1, capacity))
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            _ =>
            {
                Interlocked.Decrement(ref count);
                Interlocked.Increment(ref droppedOldestCount);
            });
    }

    public int Count => (int)Math.Max(0, Interlocked.Read(ref count));

    public long DroppedOldestCount => Interlocked.Read(ref droppedOldestCount);

    public bool Enqueue(ApplicationLogIngestItemDto item)
    {
        lock (writerSyncRoot)
        {
            if (!accepting)
            {
                return false;
            }

            return TryWrite(item);
        }
    }

    public void Requeue(IReadOnlyList<ApplicationLogIngestItemDto> items)
    {
        lock (writerSyncRoot)
        {
            foreach (var item in items)
            {
                TryWrite(item);
            }
        }
    }

    public void StopAccepting()
    {
        lock (writerSyncRoot)
        {
            accepting = false;
        }
    }

    public IReadOnlyList<ApplicationLogIngestItemDto> TakeBatch(int maximumCount)
    {
        var batch = new List<ApplicationLogIngestItemDto>(Math.Max(1, maximumCount));
        while (batch.Count < maximumCount && channel.Reader.TryRead(out var item))
        {
            Interlocked.Decrement(ref count);
            batch.Add(item);
        }

        return batch;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.WaitToReadAsync(cancellationToken);
    }

    private bool TryWrite(ApplicationLogIngestItemDto item)
    {
        if (!channel.Writer.TryWrite(item))
        {
            return false;
        }

        Interlocked.Increment(ref count);
        return true;
    }
}
