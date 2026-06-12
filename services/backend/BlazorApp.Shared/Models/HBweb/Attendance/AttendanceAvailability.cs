using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("AttendanceAvailability")]
    public class AttendanceAvailability : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false, Length = 50)]
        public string AvailabilityGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false, Length = 50)]
        public string StoreCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 50)]
        public string UserGuid { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public DateTime WeekStartDate { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime AvailableDate { get; set; }

        [SugarColumn(IsNullable = false)]
        public TimeSpan StartTime { get; set; }

        [SugarColumn(IsNullable = false)]
        public TimeSpan EndTime { get; set; }

        [SugarColumn(IsNullable = false, Length = 30)]
        public string Status { get; set; } = "Active";

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? Remark { get; set; }
    }
}
