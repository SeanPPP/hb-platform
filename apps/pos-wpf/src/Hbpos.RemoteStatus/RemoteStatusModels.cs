namespace Hbpos.RemoteStatus;

public enum RemoteRustDeskServiceStatus
{
    NotInstalled,
    Running,
    Stopped,
    Starting,
    Stopping,
    CheckFailed
}

public sealed record RemoteStatusAgentOptions(
    string AgentVersion,
    string RustDeskId,
    string ClientVersion,
    string HeartbeatUrl,
    string MonitorToken,
    string DataDirectory,
    TimeSpan HeartbeatPeriod,
    TimeSpan? MinimumHeartbeatInterval = null)
{
    public static TimeSpan DefaultHeartbeatPeriod => TimeSpan.FromSeconds(15);

    public TimeSpan EffectiveMinimumHeartbeatInterval => MinimumHeartbeatInterval ?? TimeSpan.FromSeconds(10);
}

public sealed record RustDeskProbeResult(
    RemoteRustDeskServiceStatus ServiceStatus,
    string RustDeskId,
    string ClientVersion);

public sealed record RemoteHeartbeatPayload(
    long Sequence,
    string AgentVersion,
    string RustDeskId,
    string ClientVersion,
    string ServiceStatus);

public sealed record HeartbeatSendResult(bool Succeeded, bool StopRetrying, int? StatusCode = null)
{
    public static HeartbeatSendResult Success() => new(true, false);

    public static HeartbeatSendResult Retryable(int? statusCode = null) => new(false, false, statusCode);

    public static HeartbeatSendResult Unauthorized(int statusCode) => new(false, true, statusCode);
}

public interface IRemoteStatusProbe
{
    Task<RustDeskProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

public interface IRemoteStatusSequenceStore
{
    Task<long> AllocateNextAsync(CancellationToken cancellationToken);
}

public interface IRemoteStatusHeartbeatSender
{
    Task<HeartbeatSendResult> SendAsync(
        RemoteHeartbeatPayload payload,
        string heartbeatUrl,
        string monitorToken,
        CancellationToken cancellationToken);
}
