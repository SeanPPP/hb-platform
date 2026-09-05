namespace BlazorApp.Api.Services;

public sealed class RemoteMaintenanceOptions
{
    public const string SectionName = "RemoteMaintenance";

    public bool Enabled { get; set; }
    public string IdServer { get; set; } = "hotbargain.vip:21116";
    public string RelayServer { get; set; } = "hotbargain.vip:21117";
    public string PublicKey { get; set; } = string.Empty;
    public string? ArtifactRootPath { get; set; }
    public RemoteMaintenanceArtifactOptions RustdeskArtifact { get; set; } = new();
    public RemoteMaintenanceArtifactOptions StatusAgentArtifact { get; set; } = new();
    public string InterApiKey { get; set; } = string.Empty;
    public int OnlineThresholdSeconds { get; set; } = 60;
    public int HeartbeatMinIntervalSeconds { get; set; } = 10;
    public int MaxPageSize { get; set; } = 100;
    public string? HeartbeatBaseUrl { get; set; }
}

public sealed class RemoteMaintenanceArtifactOptions
{
    public string Version { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed record RemoteMaintenanceArtifactFile(
    string Kind,
    string FileName,
    string Path,
    long SizeBytes,
    string Sha256);

public sealed record RemoteMaintenanceResult<T>(
    bool Success,
    T? Data,
    string? Code = null,
    string? Message = null)
{
    public static RemoteMaintenanceResult<T> Ok(T value) => new(true, value);
    public static RemoteMaintenanceResult<T> Fail(string code, string message) =>
        new(false, default, code, message);
}
