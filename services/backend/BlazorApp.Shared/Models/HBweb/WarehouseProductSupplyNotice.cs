using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>
/// 仓库商品供货说明：仓库下架（暂停向分店供货）时登记“以后还会不会有、预计什么时候恢复订货”。
/// “现在能不能订”仍只看 <see cref="WarehouseProduct.IsActive"/>；本表只回答“以后”，两者分开保存。
/// 同一商品同一时刻最多一条未关闭说明，恢复上架时关闭。
/// </summary>
[SugarTable("WarehouseProductSupplyNotice")]
public sealed class WarehouseProductSupplyNotice
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = false, Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>后续计划，见 <see cref="WarehouseProductSupplyPlans"/>。</summary>
    [SugarColumn(IsNullable = false, Length = 20)]
    public string SupplyPlan { get; set; } = WarehouseProductSupplyPlans.Undecided;

    /// <summary>预计恢复订货的时间段起点（纯业务日期，无时区）。</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "date")]
    public DateTime? ExpectedFrom { get; set; }

    /// <summary>预计恢复订货的时间段终点；逾期判断只看这一天。</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "date")]
    public DateTime? ExpectedTo { get; set; }

    /// <summary>时间精度，见 <see cref="WarehouseProductSupplyExpectedPrecisions"/>；决定前端按“某日 / 某几日 / 某月”展示。</summary>
    [SugarColumn(IsNullable = false, Length = 10)]
    public string ExpectedPrecision { get; set; } = WarehouseProductSupplyExpectedPrecisions.Unknown;

    /// <summary>给分店看的说明。</summary>
    [SugarColumn(IsNullable = true, Length = 500)]
    public string? StoreFacingNote { get; set; }

    /// <summary>内部备注（下架原因等），不得返回给分店端接口。</summary>
    [SugarColumn(IsNullable = true, Length = 500)]
    public string? InternalNote { get; set; }

    /// <summary>登记来源代码（WarehouseProducts / MobileWarehouse / StoreOrderProductStatus ...）。</summary>
    [SugarColumn(IsNullable = false, Length = 80)]
    public string Source { get; set; } = "Unknown";

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "System";

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAtUtc { get; set; }

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UpdatedBy { get; set; } = "System";

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>恢复上架（或被新说明取代）时写入；为空表示当前有效。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? ClosedAtUtc { get; set; }

    [SugarColumn(IsNullable = true, Length = 100)]
    public string? ClosedBy { get; set; }
}

/// <summary>供货后续计划。分店端没有说明记录时按 <see cref="Undecided"/> 展示。</summary>
public static class WarehouseProductSupplyPlans
{
    /// <summary>会补货。</summary>
    public const string WillRestock = "WillRestock";

    /// <summary>尚未确定。</summary>
    public const string Undecided = "Undecided";

    /// <summary>季节性商品，下一季恢复。</summary>
    public const string Seasonal = "Seasonal";

    /// <summary>不再供应。</summary>
    public const string Discontinued = "Discontinued";

    public static readonly IReadOnlyList<string> All = new[]
    {
        WillRestock,
        Undecided,
        Seasonal,
        Discontinued,
    };

    public static bool IsValid(string? value) =>
        value != null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>预计恢复时间的精度。</summary>
public static class WarehouseProductSupplyExpectedPrecisions
{
    /// <summary>时间待定：ExpectedFrom / ExpectedTo 均为空。</summary>
    public const string Unknown = "Unknown";

    /// <summary>具体某一天：ExpectedFrom == ExpectedTo。</summary>
    public const string Day = "Day";

    /// <summary>日期范围。</summary>
    public const string Range = "Range";

    /// <summary>某个月：ExpectedFrom 为当月 1 日，ExpectedTo 为当月最后一天。</summary>
    public const string Month = "Month";

    public static readonly IReadOnlyList<string> All = new[] { Unknown, Day, Range, Month };

    public static bool IsValid(string? value) =>
        value != null && All.Contains(value, StringComparer.Ordinal);
}
