using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.StoreOrders.Common;

internal interface IStoreOrderAccessScope
{
    /// <summary>
    /// 返回 null 表示拥有全门店范围；空集合表示当前用户没有可访问门店。
    /// </summary>
    Task<IReadOnlyList<string>?> GetAccessibleStoreCodesAsync();
}

internal sealed class StoreOrderAccessScope(
    SqlSugarContext context,
    IStoreOrderActorContext actorContext
) : IStoreOrderAccessScope
{
    private readonly SqlSugar.ISqlSugarClient _db = context.Db;

    public async Task<IReadOnlyList<string>?> GetAccessibleStoreCodesAsync()
    {
        if (HasElevatedOrderAccess())
        {
            return null;
        }

        var user = actorContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userGuid = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            var username = user.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return Array.Empty<string>();
            }

            userGuid = await _db.Queryable<User>()
                .Where(candidate => candidate.Username == username)
                .Select(candidate => candidate.UserGUID)
                .FirstAsync();
        }

        if (string.IsNullOrWhiteSpace(userGuid))
        {
            return Array.Empty<string>();
        }

        return await _db.Queryable<UserStore>()
            .InnerJoin<Store>((relation, store) => relation.StoreGUID == store.StoreGUID)
            .Where((relation, store) => relation.UserGUID == userGuid)
            .Select((relation, store) => store.StoreCode)
            .ToListAsync();
    }

    private bool HasElevatedOrderAccess()
    {
        return actorContext.HasRole("Admin")
            || actorContext.HasRole("Manager")
            || actorContext.HasRole("WarehouseManager")
            || actorContext.HasRole("WarehouseStaff");
    }
}
