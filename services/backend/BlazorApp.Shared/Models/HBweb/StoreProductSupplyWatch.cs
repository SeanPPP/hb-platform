using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>
/// 分店对暂停供货商品的“关注恢复订货”。Web 与移动端共用，按「分店 + 商品」去重。
/// 关注只表示接收信息，不产生订单、不占库存。
/// “已恢复订货”不落库：关注仍有效且商品当前可订，即视为已恢复、待分店确认，
/// 这样无论商品从哪个入口重新上架（含货柜回写等裸 SQL 路径），提醒都不会漏。
/// </summary>
[SugarTable("StoreProductSupplyWatch")]
public sealed class StoreProductSupplyWatch
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = false, Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Watching / Closed，见 <see cref="StoreProductSupplyWatchStatuses"/>。</summary>
    [SugarColumn(IsNullable = false, Length = 20)]
    public string Status { get; set; } = StoreProductSupplyWatchStatuses.Watching;

    [SugarColumn(IsNullable = false, Length = 100)]
    public string CreatedBy { get; set; } = "System";

    [SugarColumn(IsNullable = false)]
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>取消关注或确认“已恢复订货”提醒时写入。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? ClosedAtUtc { get; set; }

    [SugarColumn(IsNullable = true, Length = 100)]
    public string? ClosedBy { get; set; }

    /// <summary>关闭原因：Unwatched（主动取消）/ Acknowledged（已恢复并确认）。</summary>
    [SugarColumn(IsNullable = true, Length = 20)]
    public string? CloseReason { get; set; }
}

public static class StoreProductSupplyWatchStatuses
{
    public const string Watching = "Watching";
    public const string Closed = "Closed";
}

public static class StoreProductSupplyWatchCloseReasons
{
    public const string Unwatched = "Unwatched";
    public const string Acknowledged = "Acknowledged";
}
