using System.Collections.Concurrent;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Api.Services;

public interface ICatalogIndexCache
{
    Task<CatalogIndexBuildResult?> GetOrBuildAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken);

    void InvalidateStore(string storeCode);
}

public sealed record CatalogIndexBuildResult(
    string StoreCode,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SellableItemDto> SellableItems,
    CatalogSellableIndex CatalogIndex);

public sealed class CatalogIndexCache : ICatalogIndexCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<CatalogIndexCacheKey, CacheEntry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    public CatalogIndexCache()
        : this(TimeProvider.System, DefaultTtl)
    {
    }

    public CatalogIndexCache(TimeProvider timeProvider, TimeSpan ttl)
    {
        _timeProvider = timeProvider;
        _ttl = ttl;
    }

    public async Task<CatalogIndexBuildResult?> GetOrBuildAsync(
        string storeCode,
        DateTimeOffset? since,
        Func<CancellationToken, Task<CatalogIndexBuildResult?>> buildAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentNullException.ThrowIfNull(buildAsync);

        var key = new CatalogIndexCacheKey(NormalizeStoreCode(storeCode), since);
        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            if (_entries.TryGetValue(key, out var existing) &&
                (existing.ExpiresAt > now || IsBuildRunning(existing)))
            {
                Log(IsBuildRunning(existing)
                    ? $"cache build wait store={key.StoreCode} since={FormatSince(key.Since)}"
                    : $"cache hit store={key.StoreCode} since={FormatSince(key.Since)}");
                return await AwaitEntryAsync(key, existing, cancellationToken, allowWaitCancellation: true);
            }

            var newEntry = new CacheEntry(
                // 构建完成前使用永不过期占位，避免长构建刚完成就被判定过期。
                DateTimeOffset.MaxValue,
                new Lazy<Task<CatalogIndexBuildResult?>>(
                    // 共享构建不能绑定任一调用方的取消令牌，由缓存统一持有到完成。
                    () => buildAsync(CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            if (existing is null)
            {
                if (_entries.TryAdd(key, newEntry))
                {
                    Log($"cache miss store={key.StoreCode} since={FormatSince(key.Since)} ttlSeconds={_ttl.TotalSeconds:0}");
                    return await AwaitEntryAsync(key, newEntry, cancellationToken, allowWaitCancellation: false);
                }

                continue;
            }

            if (_entries.TryUpdate(key, newEntry, existing))
            {
                Log($"cache expired store={key.StoreCode} since={FormatSince(key.Since)} ttlSeconds={_ttl.TotalSeconds:0}");
                return await AwaitEntryAsync(key, newEntry, cancellationToken, allowWaitCancellation: false);
            }
        }
    }

    public void InvalidateStore(string storeCode)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        foreach (var key in _entries.Keys.Where(key =>
            string.Equals(key.StoreCode, normalizedStoreCode, StringComparison.OrdinalIgnoreCase)))
        {
            _entries.TryRemove(key, out _);
        }

        Log($"cache invalidated store={normalizedStoreCode}");
    }

    private async Task<CatalogIndexBuildResult?> AwaitEntryAsync(
        CatalogIndexCacheKey key,
        CacheEntry entry,
        CancellationToken cancellationToken,
        bool allowWaitCancellation)
    {
        try
        {
            // 创建共享任务的 owner 必须维持请求作用域到构建结束；复用者才可独立取消等待。
            var result = allowWaitCancellation
                ? await entry.BuildTask.Value.WaitAsync(cancellationToken)
                : await entry.BuildTask.Value;
            if (result is null)
            {
                _entries.TryRemove(new KeyValuePair<CatalogIndexCacheKey, CacheEntry>(key, entry));
            }
            else if (!allowWaitCancellation)
            {
                // 仅 build owner 从成功完成时起算 TTL；等待者不得延长缓存寿命。
                var completedEntry = entry with { ExpiresAt = _timeProvider.GetUtcNow().Add(_ttl) };
                _entries.TryUpdate(key, completedEntry, entry);
            }

            return result;
        }
        catch
        {
            // 仅构建本身失败时清除缓存；单个等待者取消不能丢弃仍在运行的共享任务。
            if (entry.BuildTask.Value.IsFaulted || entry.BuildTask.Value.IsCanceled)
            {
                _entries.TryRemove(new KeyValuePair<CatalogIndexCacheKey, CacheEntry>(key, entry));
            }

            throw;
        }
    }

    private static string NormalizeStoreCode(string value)
    {
        return value.Trim();
    }

    private static bool IsBuildRunning(CacheEntry entry)
    {
        return entry.BuildTask.IsValueCreated && !entry.BuildTask.Value.IsCompleted;
    }

    private static string FormatSince(DateTimeOffset? since)
    {
        return since?.ToString("O") ?? "<null>";
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[HBPOS][Api][CatalogIndexCache] {DateTimeOffset.Now:O} {message}");
    }

    private sealed record CacheEntry(
        DateTimeOffset ExpiresAt,
        Lazy<Task<CatalogIndexBuildResult?>> BuildTask);

    private sealed record CatalogIndexCacheKey(
        string StoreCode,
        DateTimeOffset? Since);
}
