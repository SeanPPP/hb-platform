using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceCollectorState")]
public sealed class PerformanceCollectorState : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 160, IsNullable = false)]
    public string CollectorKey { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)] public DateTime? CursorUtc { get; set; }

    [SugarColumn(IsNullable = true)] public DateTime? LastSucceededAtUtc { get; set; }

    [SugarColumn(Length = 160, IsNullable = true)]
    public string? LeaseOwner { get; set; }

    [SugarColumn(IsNullable = true)] public DateTime? LeaseExpiresAtUtc { get; set; }
}
