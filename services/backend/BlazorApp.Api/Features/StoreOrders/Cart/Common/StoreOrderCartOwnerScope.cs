using System.Security.Claims;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.Constants;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Common;

internal sealed class StoreOrderCartOwnerScope(IStoreOrderActorContext actorContext)
    : IStoreOrderCartOwnerScope
{
    public bool IsWarehouseStaffOnly =>
        HasAnyRole("WarehouseStaff", "仓库员工")
        && !HasAnyRole(Permissions.SuperAdminRoleNames)
        && !HasAnyRole(Permissions.WarehouseManagerRoleNames);

    public StoreOrderCartScope Resolve(string? storeCode)
    {
        var normalizedStoreCode = StoreOrderCartRules.NormalizeStoreCode(storeCode);
        if (!IsWarehouseStaffOnly)
        {
            // 普通门店及持有管理角色的仓库员工继续使用门店共享购物车。
            return new StoreOrderCartScope(normalizedStoreCode, null);
        }

        var user = actorContext.User;
        var userGuid = user?.FindFirst("userId")?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst("userGuid")?.Value
            ?? user?.FindFirst("userGUID")?.Value
            ?? user?.FindFirst("UserGuid")?.Value
            ?? user?.FindFirst("sub")?.Value
            ?? string.Empty;
        userGuid = userGuid.Trim();
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            throw new InvalidOperationException("无法识别当前仓库员工");
        }

        return new StoreOrderCartScope(normalizedStoreCode, userGuid);
    }

    private bool HasAnyRole(params string[] roles)
    {
        return roles.Any(actorContext.HasRole);
    }
}
