using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("AttendanceStoreHoliday")]
    public class AttendanceStoreHoliday : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false, Length = 50)]
        public string HolidayGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false, Length = 50)]
        public string StoreCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public DateTime HolidayDate { get; set; }

        [SugarColumn(IsNullable = false, Length = 100)]
        public string HolidayName { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 30)]
        public string BusinessStatus { get; set; } = "Open";

        [SugarColumn(IsNullable = true)]
        public TimeSpan? OpenTime { get; set; }

        [SugarColumn(IsNullable = true)]
        public TimeSpan? CloseTime { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool IsPaidHoliday { get; set; }

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? Remark { get; set; }
    }
}
