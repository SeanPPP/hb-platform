using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("UserLoginDeviceRecord")]
    public class UserLoginDeviceRecord : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = false, Length = 50)]
        public string RecordGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? UserGuid { get; set; }

        [SugarColumn(IsNullable = true, Length = 100)]
        public string? Username { get; set; }

        [SugarColumn(IsNullable = true, Length = 100)]
        public string? HardwareId { get; set; }

        [SugarColumn(IsNullable = true, Length = 100)]
        public string? SystemDeviceNumber { get; set; }

        [SugarColumn(IsNullable = true, Length = 30)]
        public string? DeviceSystem { get; set; }

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? StoreCode { get; set; }

        [SugarColumn(IsNullable = false, Length = 30)]
        public string LoginSource { get; set; } = "AppLogin";

        [SugarColumn(IsNullable = false)]
        public DateTime LoginAtUtc { get; set; }

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? LoginIp { get; set; }

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? UserAgent { get; set; }

        [SugarColumn(IsNullable = true)]
        public double? LocationLatitude { get; set; }

        [SugarColumn(IsNullable = true)]
        public double? LocationLongitude { get; set; }

        [SugarColumn(IsNullable = true)]
        public double? LocationAccuracyMeters { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? LocationCapturedAtUtc { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool IsDeviceSwitched { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool IsCommonDevice { get; set; }
    }
}
