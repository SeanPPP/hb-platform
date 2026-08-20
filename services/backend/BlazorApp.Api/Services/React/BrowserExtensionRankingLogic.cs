using BlazorApp.Shared.DTOs;

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
}

public static class BrowserExtensionRankingLogic
{
    public const int DefaultDays = 60;
    public const int MaximumDays = 90;

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

    public static DateOnly ResolveStartDate(DateOnly today, int days) =>
        today.AddDays(-(NormalizeDays(days) - 1));

    public static int CalculateTopItemCount(int totalProductCount) =>
        totalProductCount <= 0 ? 0 : (int)Math.Ceiling(totalProductCount * 0.1m);

    public static decimal? CalculateAverageSellingPrice(decimal salesAmount, decimal salesQuantity) =>
        salesQuantity == 0m ? null : salesAmount / salesQuantity;

    public static List<BrowserExtensionSupplierSalesAggregate> RankTopDecile(
        IEnumerable<BrowserExtensionSupplierSalesAggregate> source
    )
    {
        var ranked = source
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductCode) && item.SalesQuantity > 0m
            )
            .OrderByDescending(item => item.SalesQuantity)
            .ThenBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var take = CalculateTopItemCount(ranked.Count);

        return ranked
            .Take(take)
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
                    }
            )
            .ToList();
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
