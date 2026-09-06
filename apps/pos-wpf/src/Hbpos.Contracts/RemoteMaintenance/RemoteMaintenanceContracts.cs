namespace Hbpos.Contracts.RemoteMaintenance;

public sealed record RemoteMaintenancePrepareRequest(Guid OperationId, string ComputerName);

public sealed record RemoteMaintenanceCommitRequest(
    Guid OperationId,
    string RustdeskId,
    string ClientVersion,
    string Password);

public sealed record RemoteMaintenancePrepareResponse(
    Guid OperationId,
    Guid DeviceId,
    RemoteMaintenanceClientConfig Config,
    RemoteMaintenanceManifest ArtifactManifest);

public sealed record RemoteMaintenanceCommitResponse(
    Guid DeviceId,
    string MonitorToken,
    string HeartbeatUrl);

public sealed record RemoteMaintenanceClientConfig(
    string IdServer,
    string RelayServer,
    string PublicKey);

public sealed record RemoteMaintenanceManifest(
    string IdServer,
    string RelayServer,
    string PublicKey,
    RemoteMaintenanceArtifact Rustdesk,
    RemoteMaintenanceArtifact StatusAgent);

public sealed record RemoteMaintenanceArtifact(
    string Version,
    string FileName,
    string DownloadUrl,
    string Sha256,
    long SizeBytes);
