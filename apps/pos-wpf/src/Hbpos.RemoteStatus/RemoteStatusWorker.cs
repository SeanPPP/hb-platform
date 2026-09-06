using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hbpos.RemoteStatus;

/// <summary>
/// 低权限 Windows 服务的心跳循环。序号在调用网络前落盘，避免崩溃后重放旧心跳。
/// </summary>
public sealed class RemoteStatusWorker(
    RemoteStatusAgentOptions options,
    IRemoteStatusProbe probe,
    IRemoteStatusSequenceStore sequenceStore,
    IRemoteStatusHeartbeatSender sender,
    ILogger<RemoteStatusWorker> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private string? _lastStatus;
    private DateTimeOffset? _lastSendAtUtc;
    private bool _stoppedForUnauthorized;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !_stoppedForUnauthorized)
        {
            var changed = await SendOneAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested || _stoppedForUnauthorized)
            {
                break;
            }

            // 首次立即发送；状态变化后快速再观察一次，平稳状态按 15 秒轮询。
            var delay = changed
                ? TimeSpan.FromSeconds(2)
                : options.HeartbeatPeriod;
            try
            {
                await Task.Delay(delay, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<bool> SendOneAsync(CancellationToken cancellationToken)
    {
        if (_stoppedForUnauthorized)
        {
            return false;
        }

        RustDeskProbeResult observed;
        try
        {
            observed = await probe.ProbeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RustDesk 状态检查失败");
            observed = new RustDeskProbeResult(
                RemoteRustDeskServiceStatus.CheckFailed,
                options.RustDeskId,
                options.ClientVersion);
        }

        var status = ToWireStatus(observed.ServiceStatus);
        var changed = !string.Equals(_lastStatus, status, StringComparison.Ordinal);
        if (_lastSendAtUtc is { } lastSendAt &&
            _timeProvider.GetUtcNow() - lastSendAt < options.EffectiveMinimumHeartbeatInterval)
        {
            // 状态变化合并到下一个可发送窗口，遵守中心最小 10 秒间隔，避免 429。
            return true;
        }
        var sequence = await sequenceStore.AllocateNextAsync(cancellationToken);
        var payload = new RemoteHeartbeatPayload(
            sequence,
            options.AgentVersion,
            observed.RustDeskId,
            observed.ClientVersion,
            status);
        // 记录尝试发送的时间而不是仅记录成功时间；网络故障期间也必须遵守中心的
        // 最小 10 秒限频，避免每 2 秒快速轮询造成 429。
        _lastSendAtUtc = _timeProvider.GetUtcNow();
        HeartbeatSendResult result;
        try
        {
            result = await sender.SendAsync(
                payload,
                options.HeartbeatUrl,
                options.MonitorToken,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 网络故障不应退出 Windows 服务；本次尝试仍占用限频窗口，稍后安全重试。
            logger.LogWarning(ex, "远程状态心跳发送失败");
            result = HeartbeatSendResult.Retryable();
        }
        if (result.StopRetrying)
        {
            _stoppedForUnauthorized = true;
            logger.LogError("远程状态心跳被服务端拒绝，停止重试 statusCode={StatusCode}", result.StatusCode);
        }
        else if (result.Succeeded)
        {
            _lastStatus = status;
        }

        return changed;
    }

    internal static string ToWireStatus(RemoteRustDeskServiceStatus status) => status switch
    {
        RemoteRustDeskServiceStatus.NotInstalled => "notInstalled",
        RemoteRustDeskServiceStatus.Running => "running",
        RemoteRustDeskServiceStatus.Stopped => "stopped",
        RemoteRustDeskServiceStatus.Starting => "starting",
        RemoteRustDeskServiceStatus.Stopping => "stopping",
        _ => "checkFailed"
    };
}
