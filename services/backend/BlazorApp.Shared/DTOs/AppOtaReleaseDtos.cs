using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

public static class AppOtaReleaseErrorCodes
{
    public const string IdentityInvalid = "RELEASE_IDENTITY_INVALID";
    public const string FactConflict = "RELEASE_FACT_CONFLICT";
    public const string LegacyEndpointMigrated = "OTA_RELEASE_REGISTRATION_MIGRATED";
    public const string LegacyBootstrapInvalid = "LEGACY_OTA_BOOTSTRAP_IDENTITY_INVALID";
    public const string PosHandheldMigrationNotReady =
        "POS_HANDHELD_OTA_RELEASE_CHANNEL_NOT_READY";
}

public static class MobileOtaPolicyErrorCodes
{
    public const string LaneInvalid = "MOBILE_OTA_LANE_INVALID";
    public const string TargetRequired = "MOBILE_OTA_TARGET_REQUIRED";
    public const string TargetInvalid = "MOBILE_OTA_TARGET_INVALID";
    public const string ReleaseMessageInvalid = "MOBILE_OTA_RELEASE_MESSAGE_INVALID";
}

public sealed class AppOtaReleaseQuery
{
    public string? AppKey { get; set; }
    public string? Environment { get; set; }
    public string? Platform { get; set; }
}

public record AppOtaReleasePreflightRequest
{
    public Guid? ReleaseBatchId { get; init; }
    public string AppKey { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string ClientChannel { get; init; } = string.Empty;
    public string ReleaseChannel { get; init; } = string.Empty;
    public string EasBranch { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string? EasProjectId { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string RuntimeVersion { get; init; } = string.Empty;
    public Guid? RollbackOfReleaseId { get; init; }
    public bool BootstrapLegacyFixedChannel { get; init; }
}

public sealed record AppOtaReleaseRegisterRequest
{
    public Guid ReleaseBatchId { get; init; }
    public string AppKey { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string ClientChannel { get; init; } = string.Empty;
    public string ReleaseChannel { get; init; } = string.Empty;
    public string EasBranch { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string? EasProjectId { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string RuntimeVersion { get; init; } = string.Empty;
    public string UpdateGroupId { get; init; } = string.Empty;
    public string UpdateId { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string? GitCommitHash { get; init; }
    public string? DashboardUrl { get; init; }
    public DateTime PublishedAtUtc { get; init; }
    public bool IsRollback { get; init; }
    public Guid? RollbackOfReleaseId { get; init; }
}

public sealed class AppOtaReleasePreflightDto
{
    public bool Valid { get; set; }
}

public sealed class AppOtaReleaseDto
{
    public Guid Id { get; set; }
    public Guid ReleaseBatchId { get; set; }
    public string AppKey { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ClientChannel { get; set; } = string.Empty;
    public string ReleaseChannel { get; set; } = string.Empty;
    public string EasBranch { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string UpdateGroupId { get; set; } = string.Empty;
    public string UpdateId { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? GitCommitHash { get; set; }
    public string? DashboardUrl { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public bool IsRollback { get; set; }
    public Guid? RollbackOfReleaseId { get; set; }
    public string FactFingerprint { get; set; } = string.Empty;
    public bool Legacy { get; set; }
    public string? RegistrationSource { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class AppOtaReleaseRegistrationResultDto
{
    public AppOtaReleaseDto Release { get; set; } = new();
    public bool Idempotent { get; set; }
}

public sealed class PosHandheldOtaLegacyBackfillItemDto
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string UpdateId { get; set; } = string.Empty;
    public string UpdateGroupId { get; set; } = string.Empty;
    public string FactFingerprint { get; set; } = string.Empty;
    public bool AlreadyBackfilled { get; set; }
}

public sealed class PosHandheldOtaLegacyBackfillPreviewDto
{
    public bool Prepared { get; set; }
    public string PreparationFingerprint { get; set; } = string.Empty;
    public List<string> BlockingReasons { get; set; } = [];
    public List<PosHandheldOtaLegacyBackfillItemDto> Items { get; set; } = [];
}

public sealed class PosHandheldOtaLegacyBackfillApplyDto
{
    public string PreparationFingerprint { get; set; } = string.Empty;
    public int Inserted { get; set; }
    public int AlreadyBackfilled { get; set; }
}

public sealed class PosHandheldOtaLegacyBackfillApplyRequest
{
    public string PreparationFingerprint { get; set; } = string.Empty;
}

public sealed record MobileOtaPolicyRequest
{
    public long? ExpectedPolicyVersion { get; init; }
    public bool Enabled { get; init; }
    public bool Required { get; init; }
    public Guid? TargetReleaseId { get; init; }
    public string? ReleaseMessage { get; init; }
}

public sealed class MobileOtaPolicyDto
{
    public Guid? Id { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Required { get; set; }
    public long PolicyVersion { get; set; }
    public Guid? TargetReleaseId { get; set; }
    public string? TargetRuntimeVersion { get; set; }
    public string? ReleaseMessage { get; set; }
    public AppOtaReleaseDto? TargetRelease { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class MobileOtaPolicyRevisionDto
{
    public Guid Id { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public long PolicyVersion { get; set; }
    public string Operation { get; set; } = "save";
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class MobileOtaDecisionRequest
{
    public string? Platform { get; set; }
    public string? ClientChannel { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? CurrentUpdateId { get; set; }
    public string? CurrentUpdateGroupId { get; set; }
}

public sealed class MobileOtaDecisionDto
{
    public string State { get; set; } = AppUpdateStates.None;
    public string PolicyVersion { get; set; } = AppUpdateStates.None;
    public string AppKey { get; set; } = "mobile";
    public string Platform { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string ClientChannel { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ReleaseChannel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? RuntimeVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? UpdateId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? UpdateGroupId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ReleaseMessage { get; set; }
}
