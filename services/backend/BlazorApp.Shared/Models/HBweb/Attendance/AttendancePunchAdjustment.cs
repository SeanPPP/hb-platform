using SqlSugar;

namespace BlazorApp.Shared.Models;

[SugarTable("AttendancePunchAdjustment")]
public class AttendancePunchAdjustment : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(IsNullable = false, Length = 50)]
    public string AdjustmentGuid { get; set; } = Guid.NewGuid().ToString();

    [SugarColumn(IsNullable = false, Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 50)]
    public string UserGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 50)]
    public string? ScheduleGuid { get; set; }

    [SugarColumn(IsNullable = true, Length = 50)]
    public string? OriginalPunchGuid { get; set; }

    [SugarColumn(IsNullable = false, Length = 30)]
    public string PunchType { get; set; } = "ClockIn";

    [SugarColumn(IsNullable = false)]
    public DateTime RequestedPunchTimeLocal { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime RequestedPunchTimeUtc { get; set; }

    [SugarColumn(IsNullable = false, Length = 500)]
    public string Reason { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 30)]
    public string Status { get; set; } = "Pending";

    [SugarColumn(IsNullable = true, Length = 50)]
    public string? AppliedPunchGuid { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool IsManagerSelfDirect { get; set; }

    [SugarColumn(IsNullable = false, Length = 50)]
    public string RequestedByUserGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 50)]
    public string? ReviewedByUserGuid { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ReviewedAt { get; set; }
}
