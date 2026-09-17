namespace BlazorApp.Api.Features.ProductInsights;

/// <summary>仓库商品进销查询中不依赖数据库的口径判断，供控制器、服务与回归测试共享。</summary>
public static class WarehouseProductInsightRules
{
    /// <summary>查询区间上限；含首尾计算，可覆盖整年同期对比与跨年货柜周期。</summary>
    public const int MaxRangeDays = 400;

    /// <summary>默认区间天数；与商品进销查询的默认近 90 天保持一致。</summary>
    public const int DefaultRangeDays = 90;

    /// <summary>
    /// 货柜进货固定按最近一年统计，不跟随查询区间。
    /// 仓库靠库存供货，短区间内往往没有到货货柜，按一年看才能反映这批货的真实来量。
    /// </summary>
    public const int InboundRangeDays = 365;

    /// <summary>货柜进货区间的起始日，以查询结束日为锚点往前一年，含首尾。</summary>
    public static DateTime InboundStartDate(DateTime endDate) =>
        endDate.Date.AddDays(-(InboundRangeDays - 1));

    /// <summary>区间天数含首尾：同一天为 1 天。</summary>
    public static int CountDays(DateTime startDate, DateTime endDate) =>
        (endDate.Date - startDate.Date).Days + 1;

    public static bool IsChronological(DateTime startDate, DateTime endDate) =>
        startDate.Date <= endDate.Date;

    public static bool IsWithinMaxRange(DateTime startDate, DateTime endDate) =>
        IsChronological(startDate, endDate) && CountDays(startDate, endDate) <= MaxRangeDays;

    /// <summary>以结束日为锚点收敛出合法的最长区间起始日。</summary>
    public static DateTime ClampStartDate(DateTime endDate) =>
        endDate.Date.AddDays(-(MaxRangeDays - 1));

    public static DateTime DefaultStartDate(DateTime endDate) =>
        endDate.Date.AddDays(-(DefaultRangeDays - 1));

    /// <summary>
    /// 货柜是否算作区间内的真实进货：只认实际到货日。
    /// 仅有预计到岸日的货柜属于在途，单列展示且不计入进货合计。
    /// </summary>
    public static bool IsArrivedInbound(DateTime? actualArrivalDate, DateTime startDate, DateTime endDate) =>
        actualArrivalDate.HasValue
        && actualArrivalDate.Value.Date >= startDate.Date
        && actualArrivalDate.Value.Date <= endDate.Date;

    /// <summary>待发数量按分店计算，发货超过订货时不产生负数。</summary>
    public static decimal PendingQuantity(decimal orderedQuantity, decimal shippedQuantity) =>
        Math.Max(orderedQuantity - shippedQuantity, 0m);

    /// <summary>售罄率 = 销售数量 / 发货数量；未发货时无法计算，返回 null 而不是 0。</summary>
    public static decimal? SellThroughRate(decimal shippedQuantity, int salesQuantity) =>
        shippedQuantity <= 0m ? null : Math.Round(salesQuantity / shippedQuantity, 4);
}
