using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Features.StoreOrders.Sync.Domain;

internal sealed class StoreOrderSyncLocalOrderSnapshot
{
    public string OrderGUID { get; set; } = string.Empty;
    public string? StoreCode { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

internal sealed class HqOrderDetailFingerprint
{
    public string? DetailGuid { get; set; }
    public string? OrderGuid { get; set; }
    public string? StoreCode { get; set; }
    public string? StoreProductCode { get; set; }
    public string? ProductCode { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? AllocQuantity { get; set; }
    public decimal? LastCost { get; set; }
    public decimal? ImportPrice { get; set; }
    public decimal? ImportAmount { get; set; }
    public decimal? OemPrice { get; set; }
    public decimal? OemAmount { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

internal sealed class LocalOrderDetailFingerprint
{
    public string? DetailGuid { get; set; }
    public string? OrderGuid { get; set; }
    public string? StoreCode { get; set; }
    public string? StoreProductCode { get; set; }
    public string? ProductCode { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? AllocQuantity { get; set; }
    public decimal? LastCost { get; set; }
    public decimal? ImportPrice { get; set; }
    public decimal? ImportAmount { get; set; }
    public decimal? OemPrice { get; set; }
    public decimal? OemAmount { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

internal sealed record StoreOrderSyncPreparation(
    List<CBP_RED_分店订货单主表Store> TargetHqOrders,
    Dictionary<string, List<CBP_RED_分店订单详情表Store>> HqDetailsByOrder,
    Dictionary<string, WareHouseOrderDetails> LocalDetailByGuid,
    HashSet<string> MissingOrderGuids,
    HashSet<string> ReactivatedOrderGuids,
    HashSet<string> UpdatedOrderGuids
);

internal sealed record StoreOrderSyncQueryResult(
    string Message,
    StoreOrderSyncPreparation? Preparation
)
{
    internal static StoreOrderSyncQueryResult NoChanges(string message) => new(message, null);

    internal static StoreOrderSyncQueryResult Ready(StoreOrderSyncPreparation preparation) =>
        new(string.Empty, preparation);
}

internal readonly record struct StoreOrderSyncWriteResult(
    int OrdersSynced,
    int OrdersUpdated,
    int DetailsSynced,
    int DetailsUpdated
);

internal static class StoreOrderSyncRules
{
    internal static bool IsHqOrderNewerThanLocal(
        CBP_RED_分店订货单主表Store hqOrder,
        Dictionary<string, DateTime> localUpdatedAtMap
    )
    {
        if (!localUpdatedAtMap.TryGetValue(hqOrder.HGUID!, out var localUpdated))
        {
            return true;
        }

        return hqOrder.FGC_LastModifyDate.HasValue
            && hqOrder.FGC_LastModifyDate.Value > localUpdated;
    }

    internal static HashSet<string> GetDetailChangedOrderGuids(
        List<HqOrderDetailFingerprint> hqDetails,
        List<LocalOrderDetailFingerprint> localDetails
    )
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localDetailMap = localDetails
            .Where(detail => !string.IsNullOrWhiteSpace(detail.DetailGuid))
            .GroupBy(detail => detail.DetailGuid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var hqDetail in hqDetails)
        {
            if (
                string.IsNullOrWhiteSpace(hqDetail.OrderGuid)
                || string.IsNullOrWhiteSpace(hqDetail.DetailGuid)
            )
            {
                continue;
            }

            if (!localDetailMap.TryGetValue(hqDetail.DetailGuid, out var localDetail))
            {
                result.Add(hqDetail.OrderGuid);
                continue;
            }

            if (IsHqDetailFingerprintChanged(hqDetail, localDetail))
            {
                result.Add(hqDetail.OrderGuid);
            }
        }

        return result;
    }

    internal static bool IsHqDetailChanged(
        CBP_RED_分店订单详情表Store hqDetail,
        WareHouseOrderDetails localDetail
    )
    {
        if (localDetail.IsDeleted || !localDetail.UpdatedAt.HasValue)
        {
            return true;
        }

        if (
            hqDetail.FGC_LastModifyDate.HasValue
            && hqDetail.FGC_LastModifyDate.Value > localDetail.UpdatedAt.Value
        )
        {
            return true;
        }

        return !SameText(localDetail.OrderGUID, TrimLen(hqDetail.主表GUID, 50))
            || !SameText(localDetail.StoreCode, TrimLen(hqDetail.分店代码, 50))
            || !SameText(localDetail.StoreProductCode, TrimLen(hqDetail.分店商品编码, 50))
            || !SameText(localDetail.ProductCode, TrimLen(hqDetail.商品编码, 50))
            || !SameDecimal(localDetail.Quantity, hqDetail.数量)
            || !SameDecimal(localDetail.AllocQuantity, hqDetail.配货数量)
            || !SameDecimal(localDetail.LastCost, hqDetail.上次成本)
            || !SameDecimal(localDetail.ImportPrice, hqDetail.进口价格)
            || !SameDecimal(localDetail.ImportAmount, hqDetail.合计进口金额)
            || !SameDecimal(localDetail.OEMPrice, hqDetail.贴牌价格)
            || !SameDecimal(localDetail.OEMAmount, hqDetail.合计贴牌金额);
    }

    private static bool IsHqDetailFingerprintChanged(
        HqOrderDetailFingerprint hqDetail,
        LocalOrderDetailFingerprint localDetail
    )
    {
        if (localDetail.IsDeleted || !localDetail.UpdatedAt.HasValue)
        {
            return true;
        }

        if (
            hqDetail.UpdatedAt.HasValue
            && hqDetail.UpdatedAt.Value > localDetail.UpdatedAt.Value
        )
        {
            return true;
        }

        // HQ 明细偶发只改字段、不推进 FGC_LastModifyDate；轻量行逐字段精确比对兜底。
        return !SameText(localDetail.OrderGuid, TrimLen(hqDetail.OrderGuid, 50))
            || !SameText(localDetail.StoreCode, TrimLen(hqDetail.StoreCode, 50))
            || !SameText(localDetail.StoreProductCode, TrimLen(hqDetail.StoreProductCode, 50))
            || !SameText(localDetail.ProductCode, TrimLen(hqDetail.ProductCode, 50))
            || !SameDecimal(localDetail.Quantity, hqDetail.Quantity)
            || !SameDecimal(localDetail.AllocQuantity, hqDetail.AllocQuantity)
            || !SameDecimal(localDetail.LastCost, hqDetail.LastCost)
            || !SameDecimal(localDetail.ImportPrice, hqDetail.ImportPrice)
            || !SameDecimal(localDetail.ImportAmount, hqDetail.ImportAmount)
            || !SameDecimal(localDetail.OemPrice, hqDetail.OemPrice)
            || !SameDecimal(localDetail.OemAmount, hqDetail.OemAmount);
    }

    private static bool SameText(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameDecimal(decimal? left, decimal? right)
    {
        return left == right;
    }

    private static string? TrimLen(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
