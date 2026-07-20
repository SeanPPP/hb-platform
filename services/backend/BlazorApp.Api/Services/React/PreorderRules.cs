using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React;

public static class PreorderRules
{
    public static bool IntervalsOverlap(
        DateTime firstStart,
        DateTime firstEnd,
        DateTime secondStart,
        DateTime secondEnd
    ) => firstStart < secondEnd && secondStart < firstEnd;

    public static bool IsActivationActive(
        string status,
        DateTime startAtUtc,
        DateTime endAtUtc,
        DateTime nowUtc
    ) =>
        status == PreorderActivationStatuses.Active
        && startAtUtc <= nowUtc
        && nowUtc < endAtUtc;

    public static bool IsResponseCompleted(string status) =>
        status is PreorderWarehouseOrderStatuses.Submitted
            or PreorderWarehouseOrderStatuses.NoDemand
            or PreorderWarehouseOrderStatuses.Processing
            or PreorderWarehouseOrderStatuses.Completed
            or PreorderWarehouseOrderStatuses.Cancelled;

    public static bool IsEffectiveQuantityStatus(string status) =>
        status is PreorderWarehouseOrderStatuses.Submitted
            or PreorderWarehouseOrderStatuses.Processing
            or PreorderWarehouseOrderStatuses.Completed;

    public static bool IsStoreEditableOrderStatus(string status) =>
        status is PreorderWarehouseOrderStatuses.Draft
            or PreorderWarehouseOrderStatuses.ReturnedForRevision;

    public static int CalculateOrderedQuantity(int packCount, int minimumOrderQuantity)
    {
        if (packCount < 0 || minimumOrderQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(packCount),
                "份数必须为非负整数，最小订货量必须为正整数"
            );
        }

        try
        {
            return checked(packCount * minimumOrderQuantity);
        }
        catch (OverflowException)
        {
            // 客户端输入过大属于可预期的业务校验失败，不能泄漏为未处理的 500。
            throw new PreorderBusinessException(
                "订货份数超过允许范围",
                "PREORDER_INVALID_REQUEST",
                400
            );
        }
    }
}
