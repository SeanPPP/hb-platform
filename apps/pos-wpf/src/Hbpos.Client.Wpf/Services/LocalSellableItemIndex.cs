using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

public sealed class LocalSellableItemIndex
{
    private readonly object _gate = new();
    private readonly List<SellableItemDto> _items = [];
    private readonly Dictionary<ExactLookupKey, List<SellableItemDto>> _exactLookupIndex = [];
    private readonly Dictionary<ExactLookupKey, List<SellableItemDto>> _metadataLookupIndex = [];

    public IReadOnlyList<SellableItemDto> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public void ReplaceAll(IEnumerable<SellableItemDto> items)
    {
        var orderedItems = items
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var (exactLookupIndex, metadataLookupIndex) = BuildIndexes(orderedItems);

        lock (_gate)
        {
            ReplaceLocked(orderedItems, exactLookupIndex, metadataLookupIndex);
        }
    }

    public void Upsert(SellableItemDto item)
    {
        var normalizedStoreCode = Normalize(item.StoreCode);
        var normalizedLookupCode = Normalize(item.LookupCode);
        if (normalizedStoreCode.Length == 0 || normalizedLookupCode.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            // 单商品回写只维护受影响的索引项，避免持锁重建全量目录阻塞扫码。
            RemoveLookupLocked(new ExactLookupKey(normalizedStoreCode, normalizedLookupCode));
            InsertItemLocked(item);
        }
    }

    public bool RemoveLookup(string storeCode, string lookupCode)
    {
        var normalizedStoreCode = Normalize(storeCode);
        var normalizedLookupCode = Normalize(lookupCode);
        if (normalizedStoreCode.Length == 0 || normalizedLookupCode.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            return RemoveLookupLocked(new ExactLookupKey(normalizedStoreCode, normalizedLookupCode));
        }
    }

    private static (
        Dictionary<ExactLookupKey, List<SellableItemDto>> ExactLookupIndex,
        Dictionary<ExactLookupKey, List<SellableItemDto>> MetadataLookupIndex) BuildIndexes(
        IReadOnlyList<SellableItemDto> orderedItems)
    {
        var exactLookupIndex = new Dictionary<ExactLookupKey, List<SellableItemDto>>();
        var metadataLookupIndex = new Dictionary<ExactLookupKey, List<SellableItemDto>>();

        foreach (var item in orderedItems)
        {
            AddLookup(exactLookupIndex, item, item.LookupCode);
            AddLookup(metadataLookupIndex, item, item.Barcode);
            AddLookup(metadataLookupIndex, item, item.ItemNumber);
            AddLookup(metadataLookupIndex, item, item.ProductCode);
        }

        return (exactLookupIndex, metadataLookupIndex);
    }

    private void ReplaceLocked(
        IEnumerable<SellableItemDto> orderedItems,
        Dictionary<ExactLookupKey, List<SellableItemDto>> exactLookupIndex,
        Dictionary<ExactLookupKey, List<SellableItemDto>> metadataLookupIndex)
    {
        _items.Clear();
        _items.AddRange(orderedItems);
        _exactLookupIndex.Clear();
        foreach (var pair in exactLookupIndex)
        {
            _exactLookupIndex[pair.Key] = pair.Value;
        }

        _metadataLookupIndex.Clear();
        foreach (var pair in metadataLookupIndex)
        {
            _metadataLookupIndex[pair.Key] = pair.Value;
        }
    }

    private bool RemoveLookupLocked(ExactLookupKey key)
    {
        if (!_exactLookupIndex.TryGetValue(key, out var existingItems))
        {
            return false;
        }

        while (existingItems.Count > 0)
        {
            RemoveItemLocked(existingItems[0]);
        }

        return true;
    }

    private void RemoveItemLocked(SellableItemDto item)
    {
        for (var index = 0; index < _items.Count; index++)
        {
            if (ReferenceEquals(_items[index], item))
            {
                _items.RemoveAt(index);
                break;
            }
        }

        RemoveLookup(_exactLookupIndex, item, item.LookupCode);
        RemoveLookup(_metadataLookupIndex, item, item.Barcode);
        RemoveLookup(_metadataLookupIndex, item, item.ItemNumber);
        RemoveLookup(_metadataLookupIndex, item, item.ProductCode);
    }

    private void InsertItemLocked(SellableItemDto item)
    {
        _items.Insert(FindInsertionIndex(_items, item), item);
        AddLookup(_exactLookupIndex, item, item.LookupCode, keepSorted: true);
        AddLookup(_metadataLookupIndex, item, item.Barcode, keepSorted: true);
        AddLookup(_metadataLookupIndex, item, item.ItemNumber, keepSorted: true);
        AddLookup(_metadataLookupIndex, item, item.ProductCode, keepSorted: true);
    }

    private static int FindInsertionIndex(List<SellableItemDto> items, SellableItemDto item)
    {
        var low = 0;
        var high = items.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (StringComparer.CurrentCultureIgnoreCase.Compare(items[middle].DisplayName, item.DisplayName) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    public IReadOnlyList<SellableItemDto> Search(string query, int take = 20)
    {
        return Search(null, query, take);
    }

    public IReadOnlyList<SellableItemDto> Search(string? storeCode, string query, int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalizedStoreCode = Normalize(storeCode);
        var normalized = Normalize(query);
        SellableItemDto[] snapshot;
        lock (_gate)
        {
            snapshot = _items.ToArray();
        }

        return snapshot
            .Where(item => normalizedStoreCode.Length == 0 || Normalize(item.StoreCode) == normalizedStoreCode)
            .Select(item => new { Item = item, Rank = Rank(item, normalized) })
            .Where(match => match.Rank < int.MaxValue)
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(take)
            .Select(match => match.Item)
            .ToList();
    }

    public IReadOnlyList<SellableItemDto> FindExactMatches(string storeCode, string query)
    {
        var normalizedStoreCode = Normalize(storeCode);
        var normalizedQuery = Normalize(query);
        if (normalizedStoreCode.Length == 0 || normalizedQuery.Length == 0)
        {
            return [];
        }

        lock (_gate)
        {
            return _exactLookupIndex.TryGetValue(new ExactLookupKey(normalizedStoreCode, normalizedQuery), out var matches)
                ? matches.ToArray()
                : [];
        }
    }

    internal IReadOnlyList<SellableItemDto> FindMetadataExactMatches(string storeCode, string query)
    {
        var normalizedStoreCode = Normalize(storeCode);
        var normalizedQuery = Normalize(query);
        if (normalizedStoreCode.Length == 0 || normalizedQuery.Length == 0)
        {
            return [];
        }

        lock (_gate)
        {
            return _metadataLookupIndex.TryGetValue(new ExactLookupKey(normalizedStoreCode, normalizedQuery), out var matches)
                ? matches.ToArray()
                : [];
        }
    }

    private static void AddLookup(
        Dictionary<ExactLookupKey, List<SellableItemDto>> index,
        SellableItemDto item,
        string? lookupCode,
        bool keepSorted = false)
    {
        var normalizedLookupCode = Normalize(lookupCode);
        if (normalizedLookupCode.Length == 0)
        {
            return;
        }

        var key = new ExactLookupKey(Normalize(item.StoreCode), normalizedLookupCode);
        if (!index.TryGetValue(key, out var matches))
        {
            matches = [];
            index[key] = matches;
        }

        if (!matches.Contains(item))
        {
            if (keepSorted)
            {
                matches.Insert(FindInsertionIndex(matches, item), item);
            }
            else
            {
                matches.Add(item);
            }
        }
    }

    private static void RemoveLookup(
        Dictionary<ExactLookupKey, List<SellableItemDto>> index,
        SellableItemDto item,
        string? lookupCode)
    {
        var normalizedLookupCode = Normalize(lookupCode);
        if (normalizedLookupCode.Length == 0)
        {
            return;
        }

        var key = new ExactLookupKey(Normalize(item.StoreCode), normalizedLookupCode);
        if (!index.TryGetValue(key, out var matches))
        {
            return;
        }

        matches.Remove(item);
        if (matches.Count == 0)
        {
            index.Remove(key);
        }
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

    private sealed record ExactLookupKey(string StoreCode, string LookupCode);
}
