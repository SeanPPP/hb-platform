using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

public sealed class LocalSellableItemIndex
{
    private readonly List<SellableItemDto> _items = [];

    public IReadOnlyList<SellableItemDto> Items => _items;

    public void ReplaceAll(IEnumerable<SellableItemDto> items)
    {
        _items.Clear();
        _items.AddRange(items.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase));
    }

    public IReadOnlyList<SellableItemDto> Search(string query, int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalized = Normalize(query);
        return _items
            .Select(item => new { Item = item, Rank = Rank(item, normalized) })
            .Where(match => match.Rank < int.MaxValue)
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(take)
            .Select(match => match.Item)
            .ToList();
    }

    private static int Rank(SellableItemDto item, string query)
    {
        if (EqualsNormalized(item.Barcode, query) || EqualsNormalized(item.LookupCode, query))
        {
            return 0;
        }

        if (EqualsNormalized(item.ItemNumber, query) || EqualsNormalized(item.ProductCode, query))
        {
            return 1;
        }

        if (ContainsNormalized(item.DisplayName, query))
        {
            return 2;
        }

        if (ContainsNormalized(item.LookupCode, query) || ContainsNormalized(item.ReferenceCode, query))
        {
            return 3;
        }

        return int.MaxValue;
    }

    private static bool EqualsNormalized(string? value, string query)
    {
        return Normalize(value) == query;
    }

    private static bool ContainsNormalized(string? value, string query)
    {
        return Normalize(value).Contains(query, StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}
