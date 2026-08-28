using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.StoreOrders.Common;

internal interface IStoreOrderAccessOrderReader
{
    Task<string?> GetActiveOrderStoreCodeAsync(string orderGuid);
}

internal sealed class SqlSugarStoreOrderAccessOrderReader(SqlSugarContext context)
    : IStoreOrderAccessOrderReader
{
    public async Task<string?> GetActiveOrderStoreCodeAsync(string orderGuid)
    {
        return await context.Db.Queryable<WareHouseOrder>()
            .Where(order => order.OrderGUID == orderGuid && !order.IsDeleted)
            .Select(order => order.StoreCode)
            .FirstAsync();
    }
}
