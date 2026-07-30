using System.Text.Json.Serialization;

namespace Hbpos.Contracts.AppUpdates;

public sealed record AppUpdateCheckRequest
{
    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "production";
}

public sealed record AppUpdateCheckResponse
{
    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; init; }

    [JsonPropertyName("forceUpdate")]
    public bool ForceUpdate { get; init; }

    [JsonPropertyName("isRollback")]
    public bool IsRollback { get; init; }

    [JsonPropertyName("checkFailed")]
    public bool CheckFailed { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; init; } = string.Empty;

    [JsonPropertyName("targetVersion")]
    public string TargetVersion { get; init; } = string.Empty;

    [JsonPropertyName("minimumSupportedVersion")]
    public string? MinimumSupportedVersion { get; init; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    [JsonPropertyName("fileSize")]
    public long? FileSize { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("installerType")]
    public string? InstallerType { get; init; }

    [JsonPropertyName("installerArguments")]
    public string? InstallerArguments { get; init; }

    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; init; }

    public static AppUpdateCheckResponse NoUpdate(string currentVersion) => new()
    {
        UpdateAvailable = false,
        CurrentVersion = currentVersion,
        TargetVersion = currentVersion
    };

    public static AppUpdateCheckResponse Failed(
        string currentVersion,
        string? errorCode,
        string? errorMessage) => new()
        {
            CheckFailed = true,
            UpdateAvailable = false,
            CurrentVersion = currentVersion,
            TargetVersion = currentVersion,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// iPad Unlisted App 的更新合同；与 Windows 的 EXE/MSI 更新合同严格隔离。
/// </summary>
public sealed record PosIpadAppUpdateResponse(
    bool Enabled,
    string? MinimumSupportedVersion,
    string? LatestVersion,
    bool ForceUpdate,
    string? AppStoreUrl,
    string? ReleaseMessage);

/// <summary>
/// iPad OTA 投放决策；与 App Store 原生更新及 Windows 安装包合同严格隔离。
/// </summary>
public sealed record PosIpadOtaUpdateResponse(
    string State,
    string PolicyVersion,
    string? Channel,
    string? RuntimeVersion,
    string? IosUpdateId,
    string? UpdateGroupId,
    string? ReleaseMessage);
