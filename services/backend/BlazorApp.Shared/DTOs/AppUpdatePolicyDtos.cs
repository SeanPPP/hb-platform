using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

public static class AppUpdateApps
{
    public const string MobileIos = "mobile-ios";
    public const string PosIpad = "pos-ipad";
}

public static class AppUpdateStates
{
    public const string None = "none";
    public const string Optional = "optional";
    public const string Required = "required";
}

public static class AppUpdateTargetScopes
{
    public const string All = "all";
    public const string Stores = "stores";
}

public sealed class IosAppStoreReleaseCreateRequest
{
    public string App { get; set; } = string.Empty;
    public string AppStoreId { get; set; } = string.Empty;
    public string BuildNumber { get; set; } = string.Empty;
    public string Storefront { get; set; } = "au";
}

public sealed class IosAppStoreReleaseQuery
{
    public string? App { get; set; }
    public string? Storefront { get; set; } = "au";
}

public sealed class IosAppStoreReleaseDto
{
    public Guid Id { get; set; }
    public string App { get; set; } = string.Empty;
    public string AppStoreId { get; set; } = string.Empty;
    public string BundleIdentifier { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string BuildNumber { get; set; } = string.Empty;
    public string Storefront { get; set; } = string.Empty;
    public string AppStoreUrl { get; set; } = string.Empty;
    public DateTime AppleVerifiedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class NativeUpdatePolicyRequest
{
    public bool Enabled { get; set; }
    public Guid? ReleaseId { get; set; }
    public string? MinimumSupportedVersion { get; set; }
    public string? ReleaseMessage { get; set; }
}

public sealed class PosIpadNativeUpdatePolicyRequest : NativeUpdatePolicyRequest
{
    public string? TargetScope { get; set; } = AppUpdateTargetScopes.All;
    public List<string> TargetStoreGuids { get; set; } = new();
}

public sealed class NativeUpdatePolicyDto
{
    public Guid? Id { get; set; }
    public bool Enabled { get; set; }
    public long PolicyVersion { get; set; }
    public Guid? ReleaseId { get; set; }
    public string? LatestVersion { get; set; }
    public string? MinimumSupportedVersion { get; set; }
    public string? AppStoreUrl { get; set; }
    public string? ReleaseMessage { get; set; }
    public string TargetScope { get; set; } = AppUpdateTargetScopes.All;
    public List<string> TargetStoreGuids { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class AppUpdateTargetStoreOptionDto
{
    public string StoreGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
}

public sealed class NativeAppUpdateDecisionDto
{
    public string State { get; set; } = AppUpdateStates.None;
    public string PolicyVersion { get; set; } = AppUpdateStates.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? LatestVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? MinimumSupportedVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AppStoreUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ReleaseMessage { get; set; }
}

public sealed class PosIpadNativeDecisionRequest
{
    public string? StoreCode { get; set; }
    public string? Version { get; set; }
    public string? Build { get; set; }
}

public sealed class PosIpadOtaReleaseCreateRequest
{
    public string UpdateGroupId { get; set; } = string.Empty;
    public string IosUpdateId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string? GitCommitHash { get; set; }
    public string? DashboardUrl { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public bool IsRollback { get; set; }
    public Guid? RollbackOfReleaseId { get; set; }
}

public sealed class PosIpadOtaChannelPreflightRequest
{
    public string Channel { get; set; } = string.Empty;
}

public sealed class PosIpadOtaChannelPreflightDto
{
    public string Channel { get; set; } = string.Empty;
    public bool Available { get; set; }
}

public sealed class PosIpadOtaReleaseDto
{
    public Guid Id { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string UpdateGroupId { get; set; } = string.Empty;
    public string IosUpdateId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string? GitCommitHash { get; set; }
    public string? DashboardUrl { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public bool IsRollback { get; set; }
    public Guid? RollbackOfReleaseId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class PosIpadOtaRolloutRequest
{
    public bool Enabled { get; set; }
    public Guid? ReleaseId { get; set; }
    public bool ForceUpdate { get; set; }
    public string? TargetScope { get; set; } = AppUpdateTargetScopes.All;
    public List<string> TargetStoreGuids { get; set; } = new();
    public string? ReleaseMessage { get; set; }
}

public sealed class PosIpadOtaRolloutDto
{
    public Guid? Id { get; set; }
    public bool Enabled { get; set; }
    public long PolicyVersion { get; set; }
    public Guid? ReleaseId { get; set; }
    public bool ForceUpdate { get; set; }
    public string TargetScope { get; set; } = AppUpdateTargetScopes.All;
    public List<string> TargetStoreGuids { get; set; } = new();
    public string? ReleaseMessage { get; set; }
    public PosIpadOtaReleaseDto? Release { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class PosIpadOtaDecisionRequest
{
    public string? StoreCode { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? CurrentUpdateId { get; set; }
    public string? CurrentUpdateGroupId { get; set; }
}

public sealed class PosIpadOtaDecisionDto
{
    public string State { get; set; } = AppUpdateStates.None;
    public string PolicyVersion { get; set; } = AppUpdateStates.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Channel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? RuntimeVersion { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? IosUpdateId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? UpdateGroupId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ReleaseMessage { get; set; }
}
