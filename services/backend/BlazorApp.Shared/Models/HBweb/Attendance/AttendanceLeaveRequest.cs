using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("AttendanceLeaveRequest")]
    public class AttendanceLeaveRequest : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false, Length = 50)]
        public string LeaveGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false, Length = 50)]
        public string StoreCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 50)]
        public string UserGuid { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 30)]
        public string LeaveType { get; set; } = "AnnualLeave";

        [SugarColumn(IsNullable = false)]
        public DateTime StartDate { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime EndDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public TimeSpan? StartTime { get; set; }

        [SugarColumn(IsNullable = true)]
        public TimeSpan? EndTime { get; set; }

        [SugarColumn(IsNullable = true, Length = 1000)]
        public string? Reason { get; set; }

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? AttachmentUrl { get; set; }

        [SugarColumn(IsNullable = false, Length = 30)]
        public string Status { get; set; } = "Pending";

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ReviewedBy { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? ReviewedAt { get; set; }

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? ReviewRemark { get; set; }
    }
}
