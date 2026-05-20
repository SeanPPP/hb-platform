using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("AttendanceSettings")]
    public class AttendanceSettings : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false)]
        public int LateGraceMinutes { get; set; } = 5;

        [SugarColumn(IsNullable = false)]
        public int EarlyLeaveGraceMinutes { get; set; } = 5;

        [SugarColumn(IsNullable = false)]
        public bool AllowNoSchedulePunch { get; set; } = true;

        [SugarColumn(IsNullable = false)]
        public bool RequireApprovalForLate { get; set; } = true;

        [SugarColumn(IsNullable = false)]
        public bool RequireApprovalForEarlyLeave { get; set; } = true;

        [SugarColumn(IsNullable = false)]
        public bool RequireApprovalForNoSchedule { get; set; } = true;

        [SugarColumn(IsNullable = false)]
        public bool RequireApprovalForDuplicate { get; set; } = true;
    }
}
