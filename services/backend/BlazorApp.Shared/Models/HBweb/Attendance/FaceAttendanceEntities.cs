using SqlSugar;

namespace BlazorApp.Shared.Models;

[SugarTable("FaceAttendanceEnrollment")]
public sealed class FaceAttendanceEnrollment
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public int Id { get; set; }
    [SugarColumn(IsNullable = false, Length = 50)] public string UserGuid { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 50)] public string StoreCode { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false)] public long Version { get; set; }
    [SugarColumn(IsNullable = false, Length = 20)] public string Status { get; set; } = "active";
    [SugarColumn(IsNullable = false, Length = 4096)] public string ProtectedTemplatesJson { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false)] public DateTime CreatedAtUtc { get; set; }
    [SugarColumn(IsNullable = false)] public DateTime UpdatedAtUtc { get; set; }
    [SugarColumn(IsNullable = true, Length = 64)] public string? LastRequestHash { get; set; }
    [SugarColumn(IsNullable = true, Length = 100)] public string? UpdatedBy { get; set; }
}

[SugarTable("FaceAttendanceEvent")]
public sealed class FaceAttendanceEvent
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)] public string EventGuid { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 64)] public string ImmutablePayloadHash { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 50)] public string UserGuid { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 50)] public string StoreCode { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 50)] public string DeviceCode { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 100)] public string HardwareId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 20)] public string PunchType { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false)] public DateTime OccurredAtUtc { get; set; }
    [SugarColumn(IsNullable = false)] public DateTime DeviceObservedAtUtc { get; set; }
    [SugarColumn(IsNullable = false)] public long LocalSequence { get; set; }
    [SugarColumn(IsNullable = false)] public long RosterVersion { get; set; }
    [SugarColumn(IsNullable = false)] public long EnrollmentVersion { get; set; }
    [SugarColumn(IsNullable = false, Length = 64)] public string TimeAnchorId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false)] public bool TimeTrusted { get; set; }
    [SugarColumn(IsNullable = false, Length = 64)] public string PhotoSha256 { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 64)] public string KeyId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 4096)] public string ProtectedPhoto { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 128)] public string Signature { get; set; } = string.Empty;
    [SugarColumn(IsNullable = true, Length = 50)] public string? LeaseId { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? LeaseExpiresAtUtc { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? FaceVerifiedAtUtc { get; set; }
    [SugarColumn(IsNullable = true)] public double? FaceSimilarity { get; set; }
    [SugarColumn(IsNullable = true, Length = 50)] public string? ReviewedBy { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? ReviewedAtUtc { get; set; }
    [SugarColumn(IsNullable = true, Length = 500)] public string? ReviewReason { get; set; }
    [SugarColumn(IsNullable = false, Length = 20)] public string Status { get; set; } = "queued";
    [SugarColumn(IsNullable = true, Length = 80)] public string? ReasonCode { get; set; }
    [SugarColumn(IsNullable = true, Length = 50)] public string? PunchGuid { get; set; }
    [SugarColumn(IsNullable = false)] public int AttemptCount { get; set; }
    [SugarColumn(IsNullable = false)] public DateTime NextAttemptAtUtc { get; set; }
    [SugarColumn(IsNullable = false)] public DateTime ReceivedAtUtc { get; set; }
    [SugarColumn(IsNullable = false)] public DateTime UpdatedAtUtc { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? RetainUntilUtc { get; set; }
}

[SugarTable("FaceAttendanceTimeAnchor")]
public sealed class FaceAttendanceTimeAnchor
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string TimeAnchorId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 64)] public string KeyId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false)] public DateTime ServerObservedAtUtc { get; set; }
    [SugarColumn(IsNullable = false)] public DateTime DeviceObservedAtUtc { get; set; }
    [SugarColumn(IsNullable = false)] public DateTime ExpiresAtUtc { get; set; }
}

[SugarTable("FaceAttendanceRosterSnapshot")]
public sealed class FaceAttendanceRosterSnapshot
{
    [SugarColumn(IsPrimaryKey = true, Length = 50)] public string StoreCode { get; set; } = string.Empty;
    [SugarColumn(IsPrimaryKey = true)] public long Version { get; set; }
    // 只保存员工关系指纹，既不保存模板，也不把其他员工的调整当成本人的资料变更。
    [SugarColumn(IsNullable = false, Length = 1000000)] public string MemberFingerprintsJson { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false)] public DateTime CreatedAtUtc { get; set; }
}

[SugarTable("FaceAttendanceDeviceKey")]
public sealed class FaceAttendanceDeviceKey
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)] public string KeyId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 50)] public string StoreCode { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 50)] public string DeviceCode { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 100)] public string HardwareId { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 4096)] public string ProtectedSecret { get; set; } = string.Empty;
    [SugarColumn(IsNullable = false, Length = 20)] public string Status { get; set; } = "active";
    [SugarColumn(IsNullable = false)] public DateTime CreatedAtUtc { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? RevokedAtUtc { get; set; }
}
