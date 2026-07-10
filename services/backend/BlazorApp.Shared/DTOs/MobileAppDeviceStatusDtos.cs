using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// Expo App 设备心跳上报。
    /// </summary>
    public class MobileAppDeviceHeartbeatDto
    {
        [Required]
        [StringLength(120)]
        public string HardwareId { get; set; } = string.Empty;

        [StringLength(120)]
        public string? SystemDeviceNumber { get; set; }

        [StringLength(30)]
        public string? DeviceSystem { get; set; }

        [StringLength(30)]
        public string? Platform { get; set; }

        [StringLength(50)]
        public string? StoreCode { get; set; }

        [StringLength(80)]
        public string? AppVersion { get; set; }

        [StringLength(80)]
        public string? AppBuildVersion { get; set; }

        [StringLength(120)]
        public string? RuntimeVersion { get; set; }

        [StringLength(120)]
        public string? Channel { get; set; }

        [StringLength(120)]
        public string? UpdateId { get; set; }

        [StringLength(40)]
        public string? UpdateSource { get; set; }
    }

    /// <summary>
    /// Expo App 设备列表查询。
    /// </summary>
    public class MobileAppDeviceStatusQueryDto
    {
        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? StoreCode { get; set; }

        public string? DeviceSystem { get; set; }

        public string? OnlineState { get; set; }

        public string? Keyword { get; set; }
    }

    /// <summary>
    /// Expo App 设备当前快照。
    /// </summary>
    public class MobileAppDeviceStatusDto
    {
        public Guid Id { get; set; }

        public string HardwareId { get; set; } = string.Empty;

        public string? SystemDeviceNumber { get; set; }

        public string? DeviceSystem { get; set; }

        public string? Platform { get; set; }

        public string? StoreCode { get; set; }

        public string? AppVersion { get; set; }

        public string? AppBuildVersion { get; set; }

        public string? RuntimeVersion { get; set; }

        public string? Channel { get; set; }

        public string? UpdateId { get; set; }

        public string? UpdateSource { get; set; }

        public DateTime LastSeenAtUtc { get; set; }

        public bool IsOnline { get; set; }

        public string LastAuthMode { get; set; } = string.Empty;

        public string? LastSeenUserGuid { get; set; }

        public string? LastSeenUsername { get; set; }

        public string? LastSeenUserFullName { get; set; }

        public int? RegisteredDeviceId { get; set; }
    }

    /// <summary>
    /// Expo App 设备系统与在线汇总。
    /// </summary>
    public class MobileAppDeviceStatusSummaryDto
    {
        public int Total { get; set; }

        public int Online { get; set; }

        public int Offline { get; set; }

        public int Android { get; set; }

        public int Ios { get; set; }

        public int UnknownSystem { get; set; }
    }
}
