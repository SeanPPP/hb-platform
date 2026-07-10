using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// Expo App 设备当前版本与在线快照。
    /// </summary>
    [SugarTable("MobileAppDeviceStatus")]
    public class MobileAppDeviceStatus : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(Length = 120, IsNullable = false)]
        public string HardwareId { get; set; } = string.Empty;

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? SystemDeviceNumber { get; set; }

        [SugarColumn(Length = 30, IsNullable = true)]
        public string? DeviceSystem { get; set; }

        [SugarColumn(Length = 30, IsNullable = true)]
        public string? Platform { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? StoreCode { get; set; }

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? AppVersion { get; set; }

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? AppBuildVersion { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? RuntimeVersion { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? Channel { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? UpdateId { get; set; }

        [SugarColumn(Length = 40, IsNullable = true)]
        public string? UpdateSource { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;

        [SugarColumn(Length = 30, IsNullable = false)]
        public string LastAuthMode { get; set; } = "unknown";

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? LastSeenUserGuid { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? LastSeenUsername { get; set; }

        [SugarColumn(Length = 160, IsNullable = true)]
        public string? LastSeenUserFullName { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? RegisteredDeviceId { get; set; }
    }
}
