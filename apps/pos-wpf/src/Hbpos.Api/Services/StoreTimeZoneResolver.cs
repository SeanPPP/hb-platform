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

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<StoreTimeZoneResolver>? logger;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    public StoreTimeZoneResolver(
        IServiceScopeFactory scopeFactory,
        ILogger<StoreTimeZoneResolver>? logger = null)
    {
        this.scopeFactory = scopeFactory;
        this.logger = logger;
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

        if (cache.TryGetValue(normalizedStoreCode, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.TimeZone;
        }

        var (timeZone, ttl) = await LoadAsync(normalizedStoreCode, cancellationToken);
        cache[normalizedStoreCode] = new CacheEntry(timeZone, DateTimeOffset.UtcNow.Add(ttl));
        return timeZone;
    }

    private async Task<(TimeZoneInfo TimeZone, TimeSpan Ttl)> LoadAsync(
        string storeCode,
        CancellationToken cancellationToken)
    {
        string? configuredTimeZoneId;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HbposSqlSugarContext>();
            configuredTimeZoneId = await dbContext.MainDb.Queryable<Store>()
                .Where(store => store.StoreCode == storeCode && !store.IsDeleted)
                .Select(store => store.TimeZoneId)
                .FirstAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 查询门店失败不能让订单上传失败，按回退时区继续，并且只短暂缓存以便尽快恢复。
            logger?.LogWarning(
                exception,
                "Store timezone lookup failed storeCode={StoreCode}, falling back to {TimeZoneId}",
                storeCode,
                FallbackTimeZone.Id);
            return (FallbackTimeZone, MissingStoreCacheTtl);
        }

        if (string.IsNullOrWhiteSpace(configuredTimeZoneId))
        {
            logger?.LogWarning(
                "Store timezone not configured storeCode={StoreCode}, falling back to {TimeZoneId}",
                storeCode,
                FallbackTimeZone.Id);
            return (FallbackTimeZone, MissingStoreCacheTtl);
        }

        var timeZone = FindTimeZone([configuredTimeZoneId.Trim(), .. ToWindowsTimeZoneIds(configuredTimeZoneId.Trim())]);
        if (timeZone is null)
        {
            logger?.LogWarning(
                "Store timezone unavailable storeCode={StoreCode} timeZoneId={TimeZoneId}, falling back to {FallbackTimeZoneId}",
                storeCode,
                configuredTimeZoneId,
                FallbackTimeZone.Id);
            return (FallbackTimeZone, MissingStoreCacheTtl);
        }

        return (timeZone, CacheTtl);
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

    private sealed record CacheEntry(TimeZoneInfo TimeZone, DateTimeOffset ExpiresAt);
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
