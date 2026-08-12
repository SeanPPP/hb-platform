using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>
/// 仓库商品主档字段变更事件。该表只追加，不提供业务更新或删除操作。
/// </summary>
[SugarTable("WarehouseProductChangeHistory")]
public sealed class WarehouseProductChangeHistory
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, IsNullable = false)]
    public long Id { get; set; }

    [SugarColumn(IsNullable = false)]
    public Guid EventGuid { get; set; } = Guid.NewGuid();

    [SugarColumn(IsNullable = false, Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 40)]
    public string Action { get; set; } = "Update";

    [SugarColumn(IsNullable = false, Length = 80)]
    public string Source { get; set; } = "Unknown";

    [SugarColumn(IsNullable = true, Length = 200)]
    public string? SourceReference { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? BatchGuid { get; set; }

    [SugarColumn(IsNullable = true, Length = 80)]
    public string? ActorUserGuid { get; set; }

    [SugarColumn(IsNullable = false, Length = 120)]
    public string ActorName { get; set; } = "System";

    [SugarColumn(IsNullable = false, Length = 30)]
    public string ActorType { get; set; } = "System";

    [SugarColumn(IsNullable = false)]
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = false)]
    public string ChangesJson { get; set; } = "[]";
}
