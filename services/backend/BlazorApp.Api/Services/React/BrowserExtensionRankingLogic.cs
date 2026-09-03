using System.Runtime.CompilerServices;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Services.React;

public sealed class BrowserExtensionSupplierSalesAggregate
{
    public int Rank { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public decimal SalesQuantity { get; set; }
    public decimal SalesAmount { get; set; }
    public decimal? AverageSellingPrice { get; set; }
    public DateTime? SalesStatisticLastUpdate { get; set; }
    public string? SalesRankBand { get; set; }
}

internal sealed class BrowserExtensionSupplierSalesRankingSnapshot
{
    public string SupplierCode { get; init; } = string.Empty;
    public int Days { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public int EnabledStoreCount { get; init; }
    public int TotalProductCount { get; init; }
    public DateTime? SalesStatisticLastUpdate { get; init; }
    public IReadOnlyList<BrowserExtensionSupplierSalesAggregate> RankedTopThirty { get; init; } =
        Array.Empty<BrowserExtensionSupplierSalesAggregate>();
}

internal sealed class BrowserExtensionTopSalesPaging
{
    public bool IsLegacy { get; init; }
    public int TopPercent { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

internal sealed class BrowserExtensionRankingPageWindow
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public int Skip { get; init; }
}

internal sealed class BrowserExtensionRankingSnapshotCacheCoordinator
{
    public object Gate { get; } = new();
    public Dictionary<
        string,
        Lazy<Task<BrowserExtensionSupplierSalesRankingSnapshot>>
    > Inflight
    { get; } = new(StringComparer.Ordinal);
}

internal sealed class BrowserExtensionRankingSnapshotCache
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private static readonly ConditionalWeakTable<
        IMemoryCache,
        BrowserExtensionRankingSnapshotCacheCoordinator
    > Coordinators = new();

    private readonly IMemoryCache _cache;
    private readonly BrowserExtensionRankingSnapshotCacheCoordinator _coordinator;

    public BrowserExtensionRankingSnapshotCache(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _coordinator = Coordinators.GetValue(
            cache,
            _ => new BrowserExtensionRankingSnapshotCacheCoordinator()
        );
    }

    public async Task<BrowserExtensionSupplierSalesRankingSnapshot> GetOrCreateAsync(
        string cacheKey,
        Func<Task<BrowserExtensionSupplierSalesRankingSnapshot>> factory
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(factory);

        if (
            _cache.TryGetValue<BrowserExtensionSupplierSalesRankingSnapshot>(
                cacheKey,
                out var cached
            )
            && cached is not null
        )
        {
            return cached;
        }

        Lazy<Task<BrowserExtensionSupplierSalesRankingSnapshot>> pending;
        lock (_coordinator.Gate)
        {
            if (
                _cache.TryGetValue<BrowserExtensionSupplierSalesRankingSnapshot>(
                    cacheKey,
                    out cached
                )
                && cached is not null
            )
            {
                return cached;
            }

            if (!_coordinator.Inflight.TryGetValue(cacheKey, out pending!))
            {
                pending = new Lazy<Task<BrowserExtensionSupplierSalesRankingSnapshot>>(
                    factory,
                    LazyThreadSafetyMode.ExecutionAndPublication
                );
                _coordinator.Inflight[cacheKey] = pending;
            }
        }

        try
        {
            var snapshot = await pending.Value.ConfigureAwait(false);
            _cache.Set(
                cacheKey,
                snapshot,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration,
                }
            );
            return snapshot;
        }
        finally
        {
            lock (_coordinator.Gate)
            {
                if (
                    _coordinator.Inflight.TryGetValue(cacheKey, out var current)
                    && ReferenceEquals(current, pending)
                )
                {
                    _coordinator.Inflight.Remove(cacheKey);
                }
            }
        }
    }
}

public static class BrowserExtensionRankingLogic
{
    public const int DefaultDays = 60;
    public const int MaximumDays = 90;
    public const int LegacyTopPercent = 10;
    public const int MaximumTopPercent = 30;

    private static readonly HashSet<int> AllowedPageSizes = new() { 50, 100, 200 };

    public static int NormalizeDays(int days)
    {
        if (days is < 1 or > MaximumDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(days),
                $"统计天数必须在 1 到 {MaximumDays} 天之间。"
            );
        }

        return days;
    }

    public static int NormalizeSummaryDays(int days)
    {
        if (days is not (60 or 90))
        {
            throw new ArgumentOutOfRangeException(nameof(days), "销量排名周期仅支持 60 或 90 天。");
        }

        return days;
    }

    public static DateOnly ResolveStartDate(DateOnly today, int days) =>
        today.AddDays(-(NormalizeDays(days) - 1));

    public static int CalculateTopItemCount(int totalProductCount) =>
        CalculateTopItemCount(totalProductCount, LegacyTopPercent);

    public static int CalculateTopItemCount(int totalProductCount, int topPercent)
    {
        if (topPercent != LegacyTopPercent && topPercent != MaximumTopPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topPercent),
                "热销范围仅支持 TOP 10% 或 TOP 30%。"
            );
        }

        return totalProductCount <= 0
            ? 0
            : (int)Math.Ceiling(totalProductCount * topPercent / 100m);
    }

    public static decimal? CalculateAverageSellingPrice(decimal salesAmount, decimal salesQuantity) =>
        salesQuantity == 0m ? null : salesAmount / salesQuantity;

    public static List<BrowserExtensionSupplierSalesAggregate> RankTopDecile(
        IEnumerable<BrowserExtensionSupplierSalesAggregate> source
    ) => RankTopPercent(source, LegacyTopPercent);

    public static List<BrowserExtensionSupplierSalesAggregate> RankTopPercent(
        IEnumerable<BrowserExtensionSupplierSalesAggregate> source,
        int topPercent
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        var ranked = source
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductCode) && item.SalesQuantity > 0m
            )
            .OrderByDescending(item => item.SalesQuantity)
            .ThenBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var take = CalculateTopItemCount(ranked.Count, topPercent);

        return ranked
            .Select(
                (item, index) =>
                    new BrowserExtensionSupplierSalesAggregate
                    {
                        Rank = index + 1,
                        ProductCode = item.ProductCode,
                        ProductName = item.ProductName,
                        SalesQuantity = item.SalesQuantity,
                        SalesAmount = item.SalesAmount,
                        AverageSellingPrice = CalculateAverageSellingPrice(
                            item.SalesAmount,
                            item.SalesQuantity
                        ),
                        SalesStatisticLastUpdate = item.SalesStatisticLastUpdate,
                        SalesRankBand = ResolveSalesRankBand(index + 1, ranked.Count),
                    }
            )
            .Take(take)
            .ToList();
    }

    public static string? ResolveSalesRankBand(int rank, int totalProductCount)
    {
        if (rank < 1 || totalProductCount <= 0)
        {
            return null;
        }

        if (rank <= CalculateTopItemCount(totalProductCount, 10))
        {
            return BrowserExtensionSalesRankBands.Top10;
        }

        // TOP20 是展示档位，不是可请求的榜单范围，因此单独按百分比计算阈值。
        if (rank <= (int)Math.Ceiling(totalProductCount * 0.2m))
        {
            return BrowserExtensionSalesRankBands.Top20;
        }

        if (rank <= CalculateTopItemCount(totalProductCount, 30))
        {
            return BrowserExtensionSalesRankBands.Top30;
        }

        return null;
    }

    public static void ApplySalesRankBands(
        IEnumerable<BrowserExtensionProductSummaryDto> summaries,
        IEnumerable<BrowserExtensionSupplierSalesAggregate> rankedItems
    )
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(rankedItems);

        var rankByProductCode = rankedItems
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductCode)
                && !string.IsNullOrWhiteSpace(item.SalesRankBand)
            )
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().SalesRankBand,
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var summary in summaries)
        {
            summary.SalesRankBand = null;
            if (
                summary.MatchStatus
                    is not (
                        BrowserExtensionMatchStatuses.Matched
                        or BrowserExtensionMatchStatuses.NoPurchase
                    )
                || string.IsNullOrWhiteSpace(summary.ProductCode)
            )
            {
                continue;
            }

            if (rankByProductCode.TryGetValue(summary.ProductCode, out var salesRankBand))
            {
                summary.SalesRankBand = salesRankBand;
            }
        }
    }

    internal static BrowserExtensionTopSalesPaging ResolveTopSalesPaging(
        int? topPercent,
        int? page,
        int? pageSize
    )
    {
        if (!topPercent.HasValue && !page.HasValue && !pageSize.HasValue)
        {
            return new BrowserExtensionTopSalesPaging
            {
                IsLegacy = true,
                TopPercent = LegacyTopPercent,
            };
        }

        if (!topPercent.HasValue || !page.HasValue || !pageSize.HasValue)
        {
            throw new ArgumentException("topPercent、page 和 pageSize 必须同时提供。");
        }

        if (topPercent.Value != MaximumTopPercent)
        {
            throw new ArgumentException(
                "显式分页请求的 topPercent 仅支持 30；省略分页字段时兼容旧版 TOP 10%。",
                nameof(topPercent)
            );
        }

        if (page.Value < 1)
        {
            throw new ArgumentException("page 必须大于或等于 1。", nameof(page));
        }

        if (!AllowedPageSizes.Contains(pageSize.Value))
        {
            throw new ArgumentException("pageSize 仅支持 50、100 或 200。", nameof(pageSize));
        }

        return new BrowserExtensionTopSalesPaging
        {
            TopPercent = topPercent.Value,
            Page = page.Value,
            PageSize = pageSize.Value,
        };
    }

    internal static BrowserExtensionRankingPageWindow ResolvePageWindow(
        int requestedPage,
        int pageSize,
        int totalRankedCount
    )
    {
        if (requestedPage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPage));
        }

        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        if (totalRankedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalRankedCount));
        }

        var totalPages = totalRankedCount == 0
            ? 0
            : (int)Math.Ceiling(totalRankedCount / (decimal)pageSize);
        var page = totalPages == 0 ? 1 : Math.Min(requestedPage, totalPages);
        return new BrowserExtensionRankingPageWindow
        {
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            Skip = totalPages == 0 ? 0 : (page - 1) * pageSize,
        };
    }
}

public static class BrowserExtensionStoreSelection
{
    public static IReadOnlyList<string> NormalizeRelatedStoreCodes(
        IEnumerable<UserStoreDto>? stores
    ) =>
        (stores ?? Array.Empty<UserStoreDto>())
            .Where(store => store.IsActive)
            .Select(store => store.StoreCode?.Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
