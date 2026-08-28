namespace BlazorApp.Api.Features.LocalSupplierInvoices.Details.Infrastructure;

/// <summary>按进货单串行化本进程内的全部明细写入，保证所有入口使用相同锁顺序。</summary>
internal static class LocalSupplierInvoiceDetailsMutationLock
{
    private static readonly object ProcessLockGate = new();
    private static readonly Dictionary<string, LockEntry> ProcessLocks = new(StringComparer.Ordinal);

    internal static async ValueTask<IAsyncDisposable> AcquireProcessAsync(string invoiceGuid)
    {
        var resource = Normalize(invoiceGuid);
        LockEntry entry;
        lock (ProcessLockGate)
        {
            if (!ProcessLocks.TryGetValue(resource, out entry!))
            {
                entry = new LockEntry();
                ProcessLocks.Add(resource, entry);
            }
            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync();
            return new Releaser(resource, entry);
        }
        catch
        {
            ReleaseReference(resource, entry, releaseSemaphore: false);
            throw;
        }
    }

    private static string Normalize(string invoiceGuid)
    {
        if (string.IsNullOrWhiteSpace(invoiceGuid))
            throw new ArgumentException("进货单 GUID 不能为空", nameof(invoiceGuid));

        return invoiceGuid.Trim().ToUpperInvariant();
    }

    private static void ReleaseReference(
        string resource,
        LockEntry entry,
        bool releaseSemaphore
    )
    {
        if (releaseSemaphore)
            entry.Semaphore.Release();

        lock (ProcessLockGate)
        {
            entry.ReferenceCount--;
            if (
                entry.ReferenceCount == 0
                && ProcessLocks.TryGetValue(resource, out var current)
                && ReferenceEquals(current, entry)
            )
            {
                ProcessLocks.Remove(resource);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class LockEntry
    {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);
        internal int ReferenceCount { get; set; }
    }

    private sealed class Releaser(string resource, LockEntry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                ReleaseReference(resource, entry, releaseSemaphore: true);

            return ValueTask.CompletedTask;
        }
    }
}
