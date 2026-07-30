namespace Hbpos.Api.Services;

public sealed record CatalogBaseData(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProductPriceRecord> Products,
    IReadOnlyList<ProductSetCodeRecord> SetCodes,
    DateTimeOffset? ValidUntil = null);

public interface ICatalogBaseDataCache
{
    Task<CatalogBaseData> GetOrCreateAsync(
        Func<CancellationToken, Task<CatalogBaseData>> factory,
        CancellationToken waiterCancellationToken,
        CancellationToken buildCancellationToken = default);

    void Invalidate();
}

/// <summary>
/// 共享不区分门店的商品与套装码读取，避免多个门店目录同时重复扫描全局大表。
/// 等待请求可以独立取消，但不会取消所有请求共用的底层构建任务。
/// </summary>
public sealed class CatalogBaseDataCache : ICatalogBaseDataCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(20);
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private CatalogBaseData? _current;
    private DateTimeOffset _expiresAt;
    private Task<CatalogBaseData>? _buildTask;
    private long _buildGeneration;

    public CatalogBaseDataCache()
        : this(TimeProvider.System, DefaultTtl)
    {
    }

    internal CatalogBaseDataCache(TimeProvider timeProvider, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl));
        }

        _timeProvider = timeProvider;
        _ttl = ttl;
    }

    public Task<CatalogBaseData> GetOrCreateAsync(
        Func<CancellationToken, Task<CatalogBaseData>> factory,
        CancellationToken waiterCancellationToken,
        CancellationToken buildCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Task<CatalogBaseData> sharedTask;
        lock (_gate)
        {
            if (_current is not null && _timeProvider.GetUtcNow() < _expiresAt)
            {
                return Task.FromResult(_current);
            }

            if (_buildTask is null)
            {
                var generation = ++_buildGeneration;
                _buildTask = BuildAndPublishAsync(generation, factory, buildCancellationToken);
            }

            sharedTask = _buildTask;
        }

        return waiterCancellationToken.CanBeCanceled
            ? sharedTask.WaitAsync(waiterCancellationToken)
            : sharedTask;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _current = null;
            _expiresAt = default;
        }
    }

    private async Task<CatalogBaseData> BuildAndPublishAsync(
        long generation,
        Func<CancellationToken, Task<CatalogBaseData>> factory,
        CancellationToken buildCancellationToken)
    {
        // 先退出锁再调用外部工厂，避免同步工厂在缓存锁内执行数据库查询。
        await Task.Yield();

        try
        {
            // 请求断开只影响该 waiter；共享商品批次仅跟随 host stopping 取消。
            var built = await factory(buildCancellationToken);
            ArgumentNullException.ThrowIfNull(built);
            var validUntil = _timeProvider.GetUtcNow() + _ttl;
            var result = built with { ValidUntil = validUntil };

            lock (_gate)
            {
                if (generation == _buildGeneration)
                {
                    _current = result;
                    _expiresAt = validUntil;
                    _buildTask = null;
                }
            }

            return result;
        }
        catch
        {
            lock (_gate)
            {
                if (generation == _buildGeneration)
                {
                    _buildTask = null;
                }
            }

            throw;
        }
    }
}
