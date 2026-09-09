using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

// 人脸考勤的对外合同集中在这里，避免 iPad、网关和中心各自漂移字段或枚举值。
public static class FaceAttendanceStatuses
{
    public const string EnrollmentActive = "active";
    public const string EnrollmentRevoked = "revoked";
    public const string EventQueued = "queued";
    public const string EventVerifying = "verifying";
    public const string EventVerified = "verified";
    public const string EventNeedsReview = "needsReview";
    public const string EventRejected = "rejected";
    public const string ClockIn = "clockIn";
    public const string ClockOut = "clockOut";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FaceAttendanceEventCommandDto
{
    public string EventGuid { get; set; } = string.Empty;
    public string UserGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string PunchType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime DeviceObservedAtUtc { get; set; }
    public long LocalSequence { get; set; }
    public long RosterVersion { get; set; }
    public long EnrollmentVersion { get; set; }
    public string TimeAnchorId { get; set; } = string.Empty;
    public bool TimeTrusted { get; set; }
    public string PhotoSha256 { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public sealed class FaceDeviceSessionRequestDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public DateTime DeviceObservedAtUtc { get; set; }
    public string? KeyId { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string? Signature { get; set; }
}

public sealed class FaceDeviceSessionDto
{
    public string SessionId { get; set; } = string.Empty;
    public string TimeAnchorId { get; set; } = string.Empty;
    public DateTime ServerObservedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string KeyId { get; set; } = string.Empty;
    // 仅经已认证设备链路返回，服务端数据库从不保存明文。
    public string? DeviceKeySecret { get; set; }
}

public sealed class FaceAttendanceEmployeeDto
{
    public string UserGuid { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public long EnrollmentVersion { get; set; }
    public string EnrollmentStatus { get; set; } = FaceAttendanceStatuses.EnrollmentRevoked;
    public string? LastPunchType { get; set; }
    public DateTime? LastPunchTimeUtc { get; set; }
}

public sealed class FaceAttendanceRosterDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string StoreTimeZone { get; set; } = string.Empty;
    public DateTime ServerTimeUtc { get; set; }
    public long RosterVersion { get; set; }
    public bool CanManage { get; set; }
    public bool CanViewPhotos { get; set; }
    public bool CanReview { get; set; }
    public List<FaceAttendanceEmployeeDto> Employees { get; set; } = new();
}

public sealed class FaceEnrollmentDto
{
    public string UserGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public long EnrollmentVersion { get; set; }
    public string Status { get; set; } = FaceAttendanceStatuses.EnrollmentActive;
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class FaceAttendanceEventDto
{
    public string EventGuid { get; set; } = string.Empty;
    public string UserGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string PunchType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Status { get; set; } = FaceAttendanceStatuses.EventQueued;
    public string? ReasonCode { get; set; }
    public string? PunchGuid { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class FaceAttendanceEventPageDto
{
    public List<FaceAttendanceEventDto> Items { get; set; } = new();
    public string? NextCursor { get; set; }
}

public sealed class FaceEnrollmentRevokeRequestDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class FaceAttendanceReviewRequestDto
{
    public string Decision { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
