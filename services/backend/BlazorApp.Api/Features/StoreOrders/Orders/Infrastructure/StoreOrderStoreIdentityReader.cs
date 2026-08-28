using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;

internal sealed class StoreOrderStoreIdentityReader(SqlSugarContext context)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<HashSet<string>> GetUnmatchedOrderStoreCodesAsync()
    {
        var usedStoreCodes = await _db.Queryable<WareHouseOrder>()
            .Where(order =>
                !order.IsDeleted && order.StoreCode != null && order.StoreCode != ""
            )
            .Select(order => order.StoreCode)
            .Distinct()
            .ToListAsync();
        var normalizedUsedStoreCodes = usedStoreCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedUsedStoreCodes.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var matchedStores = await _db.Queryable<Store>()
            .Where(store =>
                (
                    !string.IsNullOrEmpty(store.StoreCode)
                    && normalizedUsedStoreCodes.Contains(store.StoreCode)
                )
                || (
                    !string.IsNullOrEmpty(store.StoreGUID)
                    && normalizedUsedStoreCodes.Contains(store.StoreGUID)
                )
            )
            .Select(store => new { store.StoreCode, store.StoreGUID })
            .ToListAsync();
        var matchedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var store in matchedStores)
        {
            if (!string.IsNullOrWhiteSpace(store.StoreCode))
            {
                matchedSet.Add(store.StoreCode);
            }
            if (!string.IsNullOrWhiteSpace(store.StoreGUID))
            {
                matchedSet.Add(store.StoreGUID);
            }
        }

        return normalizedUsedStoreCodes
            .Where(code => !matchedSet.Contains(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
