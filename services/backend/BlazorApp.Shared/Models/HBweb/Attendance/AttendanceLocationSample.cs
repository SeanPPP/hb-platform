using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("AttendanceLocationSample")]
    public class AttendanceLocationSample : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false, Length = 50)]
        public string SampleGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false, Length = 50)]
        public string UserGuid { get; set; } = string.Empty;

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? StoreCode { get; set; }

        [SugarColumn(IsNullable = true, Length = 100)]
        public string? HardwareId { get; set; }

        [SugarColumn(IsNullable = true, Length = 100)]
        public string? SystemDeviceNumber { get; set; }

        [SugarColumn(IsNullable = true, Length = 30)]
        public string? DeviceSystem { get; set; }

        [SugarColumn(IsNullable = false, Length = 30)]
        public string EventType { get; set; } = "ShiftInterval";

        [SugarColumn(IsNullable = false)]
        public double LocationLatitude { get; set; }

        [SugarColumn(IsNullable = false)]
        public double LocationLongitude { get; set; }

        [SugarColumn(IsNullable = true)]
        public double? LocationAccuracyMeters { get; set; }

        [SugarColumn(IsNullable = true, Length = 30)]
        public string? LocationPermissionStatus { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime LocationCapturedAtUtc { get; set; }
    }
}
