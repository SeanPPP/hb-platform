using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Hosting;

namespace Hbpos.Client.Wpf.Services;

public interface IOperationAuditLogger
{
    void Record(OperationAuditEventDto auditEvent);
}

internal sealed record ClientLogIdentity(string InstanceId, string AppVersion)
{
    public static ClientLogIdentity CreateCurrent()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ClientLogIdentity).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        return new ClientLogIdentity(Guid.NewGuid().ToString("D"), version);
    }
}

internal sealed record QueuedClientLog(Guid EventId, DateTimeOffset OccurredAtUtc, string PayloadJson);

internal sealed class ClientLogOutboxWriter : BackgroundService, IApplicationLogSink, IOperationAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedRuntimeProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "category", "storeCode", "deviceCode", "errorCode", "elapsedMs", "source", "action",
        "status", "screen", "mode", "reason", "result", "itemCount"
    };
    private static readonly HashSet<string> AllowedOperationProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "source", "action", "status", "screen", "mode", "reason", "result",
        "paymentMethod", "cashDrawerMode", "itemCount", "requestingCashierId",
        "authorizingCashierId", "authorizingUserGuid", "permissionCode", "authorizationMode"
    };
    private readonly ClientLogOutboxStore _store;
    private readonly DeviceAuthorizationState _authorizationState;
    private readonly ICashierSessionContext _cashierSessionContext;
    private readonly ClientLogIdentity _identity;
    private readonly int _runtimeQueueCapacity;
    private readonly Channel<QueuedClientLog> _runtimeChannel;
    private readonly Channel<QueuedClientLog> _operationChannel;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private long _runtimeQueueDroppedCount;
    private CancellationToken _hostShutdownToken = CancellationToken.None;

    public ClientLogOutboxWriter(
        ClientLogOutboxStore store,
        DeviceAuthorizationState authorizationState,
        ICashierSessionContext cashierSessionContext,
        ClientLogIdentity identity,
        int runtimeQueueCapacity = 200)
    {
        _store = store;
        _authorizationState = authorizationState;
        _cashierSessionContext = cashierSessionContext;
        _identity = identity;
        _runtimeQueueCapacity = Math.Clamp(runtimeQueueCapacity, 10, 5_000);
        _runtimeChannel = Channel.CreateBounded<QueuedClientLog>(
            new BoundedChannelOptions(_runtimeQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            _ => Interlocked.Increment(ref _runtimeQueueDroppedCount));
        _operationChannel = Channel.CreateUnbounded<QueuedClientLog>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public long RuntimeQueueDroppedCount => Interlocked.Read(ref _runtimeQueueDroppedCount);

    public void Enqueue(ApplicationLogEntry entry)
    {
        try
        {
            var eventId = Guid.NewGuid();
            var session = _cashierSessionContext.CurrentSession;
            var device = _authorizationState.Current;
            var payload = new ApplicationLogIngestItemDto
            {
                ClientEventId = eventId,
                Level = entry.Level,
                Message = entry.Message,
                TimestampUtc = entry.TimestampUtc.UtcDateTime,
                ProjectCode = entry.ProjectCode,
                Environment = entry.Environment,
                SourceType = entry.SourceType,
                Category = entry.Category,
                ServiceName = entry.ServiceName,
                InstanceId = _identity.InstanceId,
                StoreCode = device?.StoreCode ?? session?.StoreCode,
                DeviceCode = device?.DeviceCode ?? session?.DeviceCode,
                AppVersion = _identity.AppVersion,
                TraceId = entry.TraceId,
                RequestPath = entry.RequestPath,
                RequestMethod = entry.RequestMethod,
                StatusCode = entry.StatusCode,
                UserId = entry.UserId ?? session?.CashierId,
                UserName = entry.UserName ?? session?.CashierName,
                ExceptionType = entry.ExceptionType,
                ExceptionMessage = entry.ExceptionMessage,
                StackTrace = entry.StackTrace,
                Properties = entry.Properties is null
                    ? null
                    : entry.Properties
                        .Where(property => AllowedRuntimeProperties.Contains(property.Key))
                        .ToDictionary(property => property.Key, property => property.Value, StringComparer.OrdinalIgnoreCase)
            };
            var queued = new QueuedClientLog(eventId, entry.TimestampUtc, ClientLogSanitizer.Serialize(payload));
            if (_runtimeChannel.Writer.TryWrite(queued))
            {
                TryReleaseSignal();
            }
        }
        catch (Exception ex)
        {
            WriteInternalDiagnostic($"runtime enqueue failed error={ex.GetType().Name}");
        }
    }

    public void Record(OperationAuditEventDto auditEvent)
    {
        try
        {
            if (auditEvent is null)
            {
                return;
            }

            // 在调用线程同步克隆和脱敏，后续业务对象变化不会污染已记录的审计快照。
            var snapshot = JsonSerializer.Deserialize<OperationAuditEventDto>(
                JsonSerializer.Serialize(auditEvent, JsonOptions),
                JsonOptions) ?? new OperationAuditEventDto();
            snapshot.EventId = snapshot.EventId == Guid.Empty ? Guid.NewGuid() : snapshot.EventId;
            snapshot.SchemaVersion = snapshot.SchemaVersion <= 0 ? 1 : snapshot.SchemaVersion;
            snapshot.OccurredAtUtc = snapshot.OccurredAtUtc == default ? DateTimeOffset.UtcNow : snapshot.OccurredAtUtc;
            var session = _cashierSessionContext.CurrentSession;
            var device = _authorizationState.Current;
            snapshot.CashierId ??= session?.CashierId;
            snapshot.UserGuid ??= session?.UserGuid;
            snapshot.CashierName ??= session?.CashierName;
            snapshot.IsOfflineCached |= session?.IsOfflineCached == true;
            snapshot.IsEmergencyOverride |= session?.IsEmergencyOverride == true;
            // 本机已授权设备上下文优先，避免调用方伪造或误传门店、终端标识。
            snapshot.StoreCode = FirstNonEmpty(device?.StoreCode, session?.StoreCode, snapshot.StoreCode);
            snapshot.DeviceCode = FirstNonEmpty(device?.DeviceCode, session?.DeviceCode, snapshot.DeviceCode);
            snapshot.AppVersion ??= _identity.AppVersion;
            snapshot.InstanceId ??= _identity.InstanceId;
            // V1 仅支持澳元，调用方传入的其他币种不能污染本地 outbox 或导致服务端永久拒绝。
            snapshot.CurrencyCode = "AUD";
            snapshot.Properties = snapshot.Properties?
                .Where(entry => AllowedOperationProperties.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
            var queued = new QueuedClientLog(
                snapshot.EventId,
                snapshot.OccurredAtUtc,
                ClientLogSanitizer.Serialize(snapshot));
            if (_operationChannel.Writer.TryWrite(queued))
            {
                TryReleaseSignal();
            }
        }
        catch (Exception ex)
        {
            // 审计记录失败不能反向打断支付、销售或退款主链路。
            WriteInternalDiagnostic($"operation enqueue failed error={ex.GetType().Name}");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _hostShutdownToken = cancellationToken;
        _operationChannel.Writer.TryComplete();
        _runtimeChannel.Writer.TryComplete();
        TryReleaseSignal();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await InitializeWithRetryAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(stoppingToken);
                await DrainAvailableAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                // 启动后立即退出时初始化可能尚未完成，最终落库前必须先保证 outbox 表存在。
                await _store.InitializeAsync(_hostShutdownToken);
                await DrainAvailableAsync(_hostShutdownToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WriteInternalDiagnostic($"final local flush failed error={ex.GetType().Name}");
            }
        }
    }

    private async Task InitializeWithRetryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _store.InitializeAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WriteInternalDiagnostic($"database initialize failed error={ex.GetType().Name}");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task DrainAvailableAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_operationChannel.Reader.TryRead(out var operation))
            {
                await PersistWithRetryAsync(ClientLogOutboxKind.OperationAudit, operation, cancellationToken);
                continue;
            }

            if (_runtimeChannel.Reader.TryRead(out var runtime))
            {
                await PersistWithRetryAsync(ClientLogOutboxKind.Runtime, runtime, cancellationToken);
                continue;
            }

            return;
        }
    }

    private async Task PersistWithRetryAsync(
        ClientLogOutboxKind kind,
        QueuedClientLog queued,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _store.EnqueueAsync(
                    kind,
                    queued.EventId,
                    queued.OccurredAtUtc,
                    queued.PayloadJson,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (
                cancellationToken != _hostShutdownToken &&
                !_hostShutdownToken.IsCancellationRequested)
            {
                // 事件已从内存队列取出时，切换到宿主共享退出预算重试，避免取消窗口造成最后一条丢失。
                cancellationToken = _hostShutdownToken;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WriteInternalDiagnostic($"database write failed kind={kind} error={ex.GetType().Name}");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static void WriteInternalDiagnostic(string message)
    {
        var line = $"[HBPOS][Client][LogOutbox] {DateTimeOffset.Now:O} {message}";
        Console.WriteLine(line);
        Debug.WriteLine(line);
        Trace.WriteLine(line);
    }

    private void TryReleaseSignal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

}
