using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceReleaseEvent")]
public sealed class PerformanceReleaseEvent : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; }

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Action { get; set; } = string.Empty;

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(Length = 60, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string Component { get; set; } = string.Empty;

    [SugarColumn(Length = 80, IsNullable = false)]
    public string Commit { get; set; } = string.Empty;

    [SugarColumn(Length = 80, IsNullable = true)]
    public string? Version { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime StartedAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime CompletedAtUtc { get; set; }

    [SugarColumn(Length = 120, IsNullable = false)]
    public string Source { get; set; } = string.Empty;
}
