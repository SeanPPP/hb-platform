namespace BlazorApp.Shared.DTOs;

/// <summary>远程维护设备列表查询参数。</summary>
public sealed class RemoteMaintenanceDeviceQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Keyword { get; set; }
    public string? StoreCode { get; set; }
    public string? OnlineStatus { get; set; }
    public string? ServiceStatus { get; set; }
}

public sealed class RemoteMaintenanceDeviceListResponseDto
{
    public IReadOnlyList<RemoteMaintenanceDeviceListItemDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public DateTime ServerTimeUtc { get; init; }
}

public sealed class RemoteMaintenanceDeviceListItemDto
{
    public Guid Id { get; init; }
    public int DeviceRegistrationId { get; init; }
    public string StoreCode { get; init; } = string.Empty;
    public string DeviceCode { get; init; } = string.Empty;
    public string ComputerName { get; init; } = string.Empty;
    public string? RustdeskId { get; init; }
    public string? ClientVersion { get; init; }
    public string? AgentVersion { get; init; }
    public string OnlineStatus { get; init; } = RemoteMaintenanceOnlineStatuses.Never;
    public string ServiceStatus { get; init; } = RemoteMaintenanceServiceStatuses.NotInstalled;
    public DateTime? LastSeenAtUtc { get; init; }
    public DateTime RegisteredAtUtc { get; init; }
    public bool IsStale { get; init; }
}

public sealed class RemoteMaintenanceCredentialResponseDto
{
    public string Password { get; init; } = string.Empty;
}

public sealed class RemoteMaintenanceManifestDto
{
    public string IdServer { get; init; } = string.Empty;
    public string RelayServer { get; init; } = string.Empty;
    public string PublicKey { get; init; } = string.Empty;
    public RemoteMaintenanceArtifactDto Rustdesk { get; init; } = new();
    public RemoteMaintenanceArtifactDto StatusAgent { get; init; } = new();
}

public sealed class RemoteMaintenanceArtifactDto
{
    public string Version { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}

public sealed class RemoteMaintenanceClientConfigDto
{
    public string IdServer { get; init; } = string.Empty;
    public string RelayServer { get; init; } = string.Empty;
    public string PublicKey { get; init; } = string.Empty;
}

public sealed class RemoteMaintenancePrepareRequestDto
{
    public Guid OperationId { get; set; }
    public string ComputerName { get; set; } = string.Empty;
}

public sealed class RemoteMaintenancePrepareResponseDto
{
    public Guid OperationId { get; init; }
    public Guid DeviceId { get; init; }
    public RemoteMaintenanceClientConfigDto Config { get; init; } = new();
    public RemoteMaintenanceManifestDto ArtifactManifest { get; init; } = new();
}

public sealed class RemoteMaintenanceCommitRequestDto
{
    public Guid OperationId { get; set; }
    public string RustdeskId { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RemoteMaintenanceCommitResponseDto
{
    public Guid DeviceId { get; init; }
    public string MonitorToken { get; init; } = string.Empty;
    public string HeartbeatUrl { get; init; } = string.Empty;
}

public sealed class RemoteMaintenanceHeartbeatRequestDto
{
    public long Sequence { get; set; }
    public string AgentVersion { get; set; } = string.Empty;
    public string RustdeskId { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
    public string ServiceStatus { get; set; } = string.Empty;
}

public static class RemoteMaintenanceOnlineStatuses
{
    public const string Online = "online";
    public const string Offline = "offline";
    public const string Never = "never";
}

public static class RemoteMaintenanceServiceStatuses
{
    public const string NotInstalled = "notInstalled";
    public const string Running = "running";
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Stopping = "stopping";
    public const string CheckFailed = "checkFailed";

    public static bool IsValid(string? value) => value is
        NotInstalled or Running or Stopped or Starting or Stopping or CheckFailed;
}
