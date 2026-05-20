using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("AttendanceSchedule")]
    public class AttendanceSchedule : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false, Length = 50)]
        public string ScheduleGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false, Length = 50)]
        public string StoreCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 50)]
        public string UserGuid { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public DateTime WorkDate { get; set; }

        [SugarColumn(IsNullable = false)]
        public TimeSpan StartTime { get; set; }

        [SugarColumn(IsNullable = false)]
        public TimeSpan EndTime { get; set; }

        [SugarColumn(IsNullable = false, Length = 30)]
        public string Status { get; set; } = "Draft";

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? Remark { get; set; }
    }
}
