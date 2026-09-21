using BlazorApp.Shared.Constants;

namespace BlazorApp.Api.Features.StoreOrders.Common;

/// <summary>
/// 暂停供货（仓库下架）商品的订货闸门。
/// 分店侧用户不能把暂停供货的商品加进购物车或提交；仓库侧（管理员、仓库经理、仓库员工）
/// 代分店下单时允许，例如把剩余库存清给某家店——这是仓库有意为之的操作。
/// 必须在服务端判断：旧缓存页面与旧版移动端不会做前端拦截。
/// </summary>
internal static class StoreOrderSupplyGuard
{
    internal const string PausedMessage = "该商品已暂停供货，暂时不能订货";

    /// <summary>提交被拦截时的错误码；前端据此高亮受影响的购物车行。</summary>
    internal const string PausedErrorCode = "SUPPLY_PAUSED";

    private static readonly string[] WarehouseSideRoles = Permissions.SuperAdminRoleNames
        .Concat(Permissions.WarehouseManagerRoleNames)
        .Concat(new[] { "WarehouseStaff", "仓库员工" })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static bool CanOrderPausedProducts(IStoreOrderActorContext actorContext)
    {
        return WarehouseSideRoles.Any(actorContext.HasRole);
    }
}
