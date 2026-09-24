using System.Collections.Concurrent;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Models;
using Hbpos.Api.Data;
using SqlSugar;

namespace Hbpos.Api.Services;

/// <summary>
/// 解析门店所在时区。POSM 的时间列存的是门店本地墙钟时间，写入和读取都要按门店自己的时区换算。
/// </summary>
public interface IStoreTimeZoneResolver
{
    Task<TimeZoneInfo> ResolveAsync(string? storeCode, CancellationToken cancellationToken);
}

public sealed class StoreTimeZoneResolver : IStoreTimeZoneResolver
{
    // 门店没配时区时回退到悉尼，与后端统计的营业日口径（SalesStatisticsBusinessDate）一致。
    private static readonly string[] FallbackTimeZoneIds = [StoreTimeZonePolicy.Sydney, "AUS Eastern Standard Time"];
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MissingStoreCacheTtl = TimeSpan.FromMinutes(1);

    private readonly Func<string, CancellationToken, Task<string?>> lookupConfiguredTimeZoneIdAsync;
    private readonly ILogger<StoreTimeZoneResolver>? logger;
    private readonly TimeProvider timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    public StoreTimeZoneResolver(
        IServiceScopeFactory scopeFactory,
        ILogger<StoreTimeZoneResolver>? logger = null,
        TimeProvider? timeProvider = null)
        : this(CreateStoreLookup(scopeFactory), logger, timeProvider)
    {
    }

    // 测试入口：直接注入"按门店码读取 TimeZoneId"的查询，便于构造查库成功与失败交错的时序。
    internal StoreTimeZoneResolver(
        Func<string, CancellationToken, Task<string?>> lookupConfiguredTimeZoneIdAsync,
        ILogger<StoreTimeZoneResolver>? logger = null,
        TimeProvider? timeProvider = null)
    {
        this.lookupConfiguredTimeZoneIdAsync = lookupConfiguredTimeZoneIdAsync;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        FallbackTimeZone = FindTimeZone(FallbackTimeZoneIds)
            ?? throw new TimeZoneNotFoundException($"找不到回退时区 {StoreTimeZonePolicy.Sydney}");
    }

    public TimeZoneInfo FallbackTimeZone { get; }

    public async Task<TimeZoneInfo> ResolveAsync(string? storeCode, CancellationToken cancellationToken)
    {
        var normalizedStoreCode = storeCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStoreCode))
        {
            logger?.LogWarning("Store timezone resolve missing storeCode, falling back to {TimeZoneId}", FallbackTimeZone.Id);
            return FallbackTimeZone;
        }

        if (cache.TryGetValue(normalizedStoreCode, out var cached) && cached.ExpiresAt > timeProvider.GetUtcNow())
        {
            return cached.TimeZone;
        }

        var lookup = await LoadAsync(normalizedStoreCode, cancellationToken);
        var now = timeProvider.GetUtcNow();
        // AddOrUpdate 保证"失败不覆盖成功"在并发下也成立：更新函数看到的是写入时刻的现值。
        var entry = cache.AddOrUpdate(
            normalizedStoreCode,
            _ => new CacheEntry(lookup.TimeZone, now.Add(lookup.Ttl), FromStoreLookup: lookup.Failure is null),
            (_, existing) => SelectCacheEntry(existing, lookup, now));
        if (lookup.Failure is not null)
        {
            if (entry.FromStoreLookup)
            {
                logger?.LogWarning(
                    lookup.Failure,
                    "Store timezone lookup failed storeCode={StoreCode}, keeping last resolved {TimeZoneId}",
                    normalizedStoreCode,
                    entry.TimeZone.Id);
            }
            else
            {
                logger?.LogWarning(
                    lookup.Failure,
                    "Store timezone lookup failed storeCode={StoreCode}, falling back to {TimeZoneId}",
                    normalizedStoreCode,
                    entry.TimeZone.Id);
            }
        }

        return entry.TimeZone;
    }

    private static CacheEntry SelectCacheEntry(CacheEntry existing, StoreTimeZoneLookup lookup, DateTimeOffset now)
    {
        if (lookup.Failure is null || !existing.FromStoreLookup)
        {
            return new CacheEntry(lookup.TimeZone, now.Add(lookup.Ttl), FromStoreLookup: lookup.Failure is null);
        }

        // 查库失败但此前查到过该门店时区：沿用它，不能用悉尼覆盖。库里只存墙钟时间，
        // 布里斯班门店在悉尼夏令时期间按悉尼写入会晚记 1 小时，事后无法识别和更正。
        // 已被并发的成功查询刷新过就原样保留；否则只把下次重查推迟一小段。
        return existing.ExpiresAt > now
            ? existing
            : existing with { ExpiresAt = now.Add(MissingStoreCacheTtl) };
    }

    private async Task<StoreTimeZoneLookup> LoadAsync(
        string storeCode,
        CancellationToken cancellationToken)
    {
        string? configuredTimeZoneId;
        try
        {
            configuredTimeZoneId = await lookupConfiguredTimeZoneIdAsync(storeCode, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 查询门店失败不能让订单上传失败：由调用方优先沿用上次查到的时区，没有时才回退悉尼，都只短暂缓存以便尽快重查。
            return new StoreTimeZoneLookup(FallbackTimeZone, MissingStoreCacheTtl, exception);
        }

        if (string.IsNullOrWhiteSpace(configuredTimeZoneId))
        {
            logger?.LogWarning(
                "Store timezone not configured storeCode={StoreCode}, falling back to {TimeZoneId}",
                storeCode,
                FallbackTimeZone.Id);
            return new StoreTimeZoneLookup(FallbackTimeZone, MissingStoreCacheTtl, Failure: null);
        }

        var timeZone = FindTimeZone([configuredTimeZoneId.Trim(), .. ToWindowsTimeZoneIds(configuredTimeZoneId.Trim())]);
        if (timeZone is null)
        {
            logger?.LogWarning(
                "Store timezone unavailable storeCode={StoreCode} timeZoneId={TimeZoneId}, falling back to {FallbackTimeZoneId}",
                storeCode,
                configuredTimeZoneId,
                FallbackTimeZone.Id);
            return new StoreTimeZoneLookup(FallbackTimeZone, MissingStoreCacheTtl, Failure: null);
        }

        return new StoreTimeZoneLookup(timeZone, CacheTtl, Failure: null);
    }

    private static Func<string, CancellationToken, Task<string?>> CreateStoreLookup(IServiceScopeFactory scopeFactory)
    {
        return async (storeCode, cancellationToken) =>
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HbposSqlSugarContext>();
            return await dbContext.MainDb.Queryable<Store>()
                .Where(store => store.StoreCode == storeCode && !store.IsDeleted)
                .Select(store => store.TimeZoneId)
                .FirstAsync(cancellationToken);
        };
    }

    private static TimeZoneInfo? FindTimeZone(IEnumerable<string> timeZoneIds)
    {
        foreach (var timeZoneId in timeZoneIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // 继续尝试下一个候选 ID。
            }
        }

        return null;
    }

    // 运行环境按 IANA ID 解析即可；这里额外给出 Windows ID，避免没有 ICU 的宿主解析失败。
    private static string[] ToWindowsTimeZoneIds(string ianaTimeZoneId)
    {
        return ianaTimeZoneId switch
        {
            StoreTimeZonePolicy.Brisbane => ["E. Australia Standard Time"],
            StoreTimeZonePolicy.Sydney or StoreTimeZonePolicy.Melbourne => ["AUS Eastern Standard Time"],
            _ => []
        };
    }

    // FromStoreLookup 表示该值来自一次成功的查库（含"门店没配时区"这种确定答复），查库失败时只沿用这类值。
    private sealed record CacheEntry(TimeZoneInfo TimeZone, DateTimeOffset ExpiresAt, bool FromStoreLookup);

    // Failure 非空表示查库本身失败（而不是门店没配时区），TimeZone 此时是回退时区。
    private sealed record StoreTimeZoneLookup(TimeZoneInfo TimeZone, TimeSpan Ttl, Exception? Failure);
}

/// <summary>
/// POSM 时间列与 DateTimeOffset 之间的换算：库里存的是门店本地墙钟时间，没有时区信息。
/// </summary>
public static class StoreWallClock
{
    /// <summary>把带时区的时刻换算成门店本地墙钟时间，用于写入 POSM。</summary>
    public static DateTime ToWallClock(DateTimeOffset instant, TimeZoneInfo storeTimeZone)
    {
        return TimeZoneInfo.ConvertTime(instant, storeTimeZone).DateTime;
    }

    /// <summary>把库里的门店本地墙钟时间还原成带门店偏移的时刻，用于返回给客户端。</summary>
    public static DateTimeOffset ToDateTimeOffset(DateTime wallClock, TimeZoneInfo storeTimeZone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        // 夏令时结束当天有一小时重复、开始当天有一小时不存在，取该时刻在门店时区的实际偏移。
        return new DateTimeOffset(unspecified, storeTimeZone.IsInvalidTime(unspecified)
            ? storeTimeZone.GetUtcOffset(unspecified.AddHours(1))
            : storeTimeZone.GetUtcOffset(unspecified));
    }
}
