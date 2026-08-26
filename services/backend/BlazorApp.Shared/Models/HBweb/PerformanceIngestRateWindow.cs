using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceIngestRateWindow")]
public sealed class PerformanceIngestRateWindow : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 80, IsNullable = false)]
    public string ProjectCode { get; set; } = string.Empty;

    /// <summary>固定值 project 表示项目总预算；其他值为客户端地址的不可逆 SHA-256。</summary>
    [SugarColumn(Length = 64, IsNullable = false)]
    public string ClientKeyHash { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime WindowStartUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public long RequestCount { get; set; }

    [SugarColumn(IsNullable = false)]
    public long EventCount { get; set; }

    [SugarColumn(IsNullable = false)]
    public long ByteCount { get; set; }
}
