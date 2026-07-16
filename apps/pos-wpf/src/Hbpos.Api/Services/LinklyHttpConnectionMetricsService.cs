using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

internal sealed class LinklyHttpConnectionMetricsService : IHostedService, IDisposable
{
    private const string MeterName = "System.Net.Http";
    private const string InstrumentName = "http.client.open_connections";
    private const string LogPrefix = "[HBPOS][Api][LinklyTcp] ";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StateTransitionSettlingDelay = TimeSpan.FromMilliseconds(75);

    private readonly ILogger<LinklyHttpConnectionMetricsService> _logger;
    private readonly LinklyCloudBackendAsyncOptions _options;
    private readonly MeterListener _listener = new();
    private readonly Timer _timer;
    private readonly object _sync = new();
    private readonly Dictionary<OriginKey, OriginAssignment> _origins = [];
    private readonly Dictionary<EnvironmentRole, string> _roleOrigins = [];
    private readonly Dictionary<ConnectionKey, long> _connections = [];
    private readonly Dictionary<PhysicalConnectionKey, long> _pendingNegativeDeltas = [];
    private readonly HashSet<PhysicalConnectionKey> _unsettledPendingConnections = [];
    private readonly Dictionary<string, ConnectionTotals> _lastLoggedTotals = new(StringComparer.Ordinal);
    private Task? _stopTask;
    private bool _flushPending;
    private bool _started;
    private bool _stopped;

    public LinklyHttpConnectionMetricsService(
        IOptions<LinklyCloudBackendAsyncOptions> options,
        ILogger<LinklyHttpConnectionMetricsService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _timer = new Timer(FlushSnapshots, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_started || _stopped)
            {
                return Task.CompletedTask;
            }

            ConfigureOrigins();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument is UpDownCounter<long> &&
                    instrument.Meter.Name == MeterName &&
                    instrument.Name == InstrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(OnMeasurement);
            _listener.Start();
            _started = true;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Stop();
    }

    public void Dispose() => Stop().GetAwaiter().GetResult();

    private void ConfigureOrigins()
    {
        var candidates = new[]
        {
            CreateOriginAssignment("Production", ConnectionRole.Token, _options.ProductionAuthBaseUrl),
            CreateOriginAssignment("Production", ConnectionRole.Rest, _options.ProductionRestBaseUrl),
            CreateOriginAssignment("Sandbox", ConnectionRole.Token, _options.SandboxAuthBaseUrl),
            CreateOriginAssignment("Sandbox", ConnectionRole.Rest, _options.SandboxRestBaseUrl)
        }
        .OfType<OriginAssignment>()
        .ToArray();

        foreach (var group in candidates.GroupBy(candidate => candidate.Origin))
        {
            var assignments = group.ToArray();
            if (assignments.Length > 1)
            {
                var owners = string.Join(",", assignments
                    .Select(candidate => $"{candidate.Environment}/{candidate.Role}")
                    .Order(StringComparer.Ordinal));
                var environments = assignments
                    .Select(candidate => candidate.Environment)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var environment = environments.Length == 1 ? environments[0] : "Shared";
                var sharedAssignment = new OriginAssignment(
                    environment,
                    ConnectionRole.Shared,
                    assignments[0].Origin,
                    assignments[0].DisplayOrigin);
                _origins.Add(sharedAssignment.Origin, sharedAssignment);
                foreach (var owner in assignments)
                {
                    _roleOrigins.TryAdd(
                        new EnvironmentRole(owner.Environment, owner.Role),
                        owner.DisplayOrigin);
                }

                _logger.LogWarning(
                    "{Prefix}duplicate Linkly origin counted as shared origin={Origin} owners={Owners}",
                    LogPrefix,
                    sharedAssignment.DisplayOrigin,
                    owners);
                continue;
            }

            var assignment = assignments[0];
            _origins.Add(assignment.Origin, assignment);
            _roleOrigins.Add(new EnvironmentRole(assignment.Environment, assignment.Role), assignment.DisplayOrigin);
        }
    }

    private OriginAssignment? CreateOriginAssignment(
        string environment,
        ConnectionRole role,
        string? value)
    {
        if (!TryNormalizeOrigin(value, out var origin, out var displayOrigin))
        {
            _logger.LogWarning(
                "{Prefix}invalid Linkly URL skipped environment={Environment} role={Role} url=<invalid>",
                LogPrefix,
                environment,
                role);
            return null;
        }

        return new OriginAssignment(environment, role, origin, displayOrigin);
    }

    private void OnMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        string? scheme = null;
        string? host = null;
        string? connectionState = null;
        string? peerAddress = null;
        string? protocolVersion = null;
        int? port = null;

        foreach (var tag in tags)
        {
            switch (tag.Key)
            {
                case "url.scheme":
                    scheme = tag.Value?.ToString();
                    break;
                case "server.address":
                    host = tag.Value?.ToString();
                    break;
                case "server.port" when TryReadPort(tag.Value, out var parsedPort):
                    port = parsedPort;
                    break;
                case "http.connection.state":
                    connectionState = tag.Value?.ToString();
                    break;
                case "network.peer.address":
                    peerAddress = tag.Value?.ToString();
                    break;
                case "network.protocol.version":
                    protocolVersion = tag.Value?.ToString();
                    break;
            }
        }

        if (!TryNormalizeOrigin(scheme, host, port, out var origin) ||
            connectionState is not ("active" or "idle") ||
            string.IsNullOrWhiteSpace(peerAddress) ||
            string.IsNullOrWhiteSpace(protocolVersion))
        {
            return;
        }

        lock (_sync)
        {
            if (_stopped || !_origins.TryGetValue(origin, out var assignment))
            {
                return;
            }

            var physicalConnection = new PhysicalConnectionKey(
                assignment.Environment,
                assignment.Role,
                origin,
                peerAddress,
                protocolVersion);
            TrackPendingNegativeDelta(physicalConnection, measurement);
            var key = new ConnectionKey(
                assignment.Environment,
                assignment.Role,
                origin,
                connectionState,
                peerAddress,
                protocolVersion);
            _connections.TryGetValue(key, out var current);
            var updated = current + measurement;

            // 关键逻辑：UpDownCounter 上报增减量，归零立即移除，避免连接关闭后残留陈旧维度。
            if (updated > 0)
            {
                _connections[key] = updated;
            }
            else
            {
                _connections.Remove(key);
            }

            if (!_flushPending)
            {
                _flushPending = true;
                _timer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void FlushSnapshots(object? state)
    {
        List<LogSnapshot> snapshots = [];

        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            // 状态切换的 -1/+1 是两个独立事件；每个物理连接维度只额外等待一次短窗口。
            if (_unsettledPendingConnections.Count > 0)
            {
                _unsettledPendingConnections.Clear();
                _timer.Change(StateTransitionSettlingDelay, Timeout.InfiniteTimeSpan);
                return;
            }

            _pendingNegativeDeltas.Clear();
            // 固定窗口只合并首个 250ms 内的事件，避免持续流量无限推迟首次连接快照。
            _flushPending = false;

            var environments = new[] { "Production", "Sandbox" }
                .Concat(_connections.Keys.Select(key => key.Environment))
                .Concat(_lastLoggedTotals.Keys)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (var environment in environments)
            {
                var relevant = _connections
                    .Where(pair => pair.Key.Environment == environment && pair.Value > 0)
                    .ToArray();
                var tokenConnections = relevant
                    .Where(pair => pair.Key.Role == ConnectionRole.Token)
                    .Sum(pair => pair.Value);
                var restConnections = relevant
                    .Where(pair => pair.Key.Role == ConnectionRole.Rest)
                    .Sum(pair => pair.Value);
                var sharedConnections = relevant
                    .Where(pair => pair.Key.Role == ConnectionRole.Shared)
                    .Sum(pair => pair.Value);
                var totalConnections = tokenConnections + restConnections + sharedConnections;
                var totals = new ConnectionTotals(tokenConnections, restConnections, sharedConnections, totalConnections);

                if (!_lastLoggedTotals.TryGetValue(environment, out var previous))
                {
                    if (totalConnections == 0)
                    {
                        continue;
                    }
                }
                else if (previous == totals)
                {
                    continue;
                }

                _lastLoggedTotals[environment] = totals;
                snapshots.Add(new LogSnapshot(
                    environment,
                    totals,
                    relevant
                        .Where(pair => pair.Key.State == "active")
                        .Sum(pair => pair.Value),
                    relevant
                        .Where(pair => pair.Key.State == "idle")
                        .Sum(pair => pair.Value),
                    GetRoleOrigin(environment, ConnectionRole.Token),
                    GetRoleOrigin(environment, ConnectionRole.Rest),
                    relevant
                        .Where(pair => pair.Key.Role == ConnectionRole.Shared)
                        .Select(pair => _origins[pair.Key.Origin].DisplayOrigin)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    relevant.Select(pair => pair.Key.PeerAddress).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    relevant.Select(pair => pair.Key.ProtocolVersion).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
            }
        }

        foreach (var snapshot in snapshots)
        {
            WriteSnapshot(snapshot);
        }
    }

    private void WriteSnapshot(LogSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(new
        {
            source = "api-linkly-tcp",
            operation = "physical-connection-count",
            phase = "snapshot",
            direction = "outbound",
            environment = snapshot.Environment,
            details = new
            {
                tokenConnections = snapshot.Totals.Token,
                restConnections = snapshot.Totals.Rest,
                sharedConnections = snapshot.Totals.Shared,
                totalConnections = snapshot.Totals.Total,
                activeConnections = snapshot.Active,
                idleConnections = snapshot.Idle,
                limit = 2,
                withinLimit = snapshot.Totals.Total <= 2,
                tokenOrigin = snapshot.TokenOrigin,
                restOrigin = snapshot.RestOrigin,
                sharedOrigins = snapshot.SharedOrigins,
                peerAddresses = snapshot.PeerAddresses,
                protocolVersions = snapshot.ProtocolVersions
            }
        });
        var message = LogPrefix + json;

        if (snapshot.Totals.Total <= 2)
        {
            _logger.LogInformation("{Message}", message);
        }
        else
        {
            _logger.LogWarning("{Message}", message);
        }
    }

    private string? GetRoleOrigin(string environment, ConnectionRole role) =>
        _roleOrigins.GetValueOrDefault(new EnvironmentRole(environment, role));

    private void TrackPendingNegativeDelta(PhysicalConnectionKey connection, long measurement)
    {
        if (measurement < 0)
        {
            _pendingNegativeDeltas.TryGetValue(connection, out var pending);
            _pendingNegativeDeltas[connection] = pending - measurement;
            _unsettledPendingConnections.Add(connection);
            return;
        }

        if (measurement <= 0 || !_pendingNegativeDeltas.TryGetValue(connection, out var pendingNegative))
        {
            return;
        }

        if (measurement >= pendingNegative)
        {
            _pendingNegativeDeltas.Remove(connection);
            _unsettledPendingConnections.Remove(connection);
        }
        else
        {
            _pendingNegativeDeltas[connection] = pendingNegative - measurement;
        }
    }

    private Task Stop()
    {
        lock (_sync)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            _stopped = true;
            _stopTask = StopCoreAsync();
            return _stopTask;
        }
    }

    private async Task StopCoreAsync()
    {
        // 关键逻辑：先退出同步锁，再等待 Timer 回调结束，确保 Stop 返回后不会继续写过期快照。
        await Task.Yield();
        _listener.Dispose();
        await _timer.DisposeAsync();

        lock (_sync)
        {
            _connections.Clear();
            _pendingNegativeDeltas.Clear();
            _unsettledPendingConnections.Clear();
        }
    }

    private static bool TryNormalizeOrigin(
        string? value,
        out OriginKey origin,
        out string displayOrigin)
    {
        origin = default;
        displayOrigin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.DnsSafeHost))
        {
            return false;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.DnsSafeHost.ToLowerInvariant();
        var port = uri.Port;
        origin = new OriginKey(scheme, host, port);
        displayOrigin = $"{scheme}://{(host.Contains(':') ? $"[{host}]" : host)}:{port}";
        return true;
    }

    private static bool TryNormalizeOrigin(
        string? scheme,
        string? host,
        int? port,
        out OriginKey origin)
    {
        var normalizedScheme = scheme?.Trim().ToLowerInvariant();
        var effectivePort = port ?? normalizedScheme switch
        {
            "http" => 80,
            "https" => 443,
            _ => -1
        };

        if (string.IsNullOrWhiteSpace(normalizedScheme) ||
            string.IsNullOrWhiteSpace(host) ||
            effectivePort is <= 0 or > ushort.MaxValue)
        {
            origin = default;
            return false;
        }

        try
        {
            var uri = new UriBuilder(normalizedScheme, host, effectivePort).Uri;
            return TryNormalizeOrigin(uri.GetLeftPart(UriPartial.Authority), out origin, out _);
        }
        catch (UriFormatException)
        {
            origin = default;
            return false;
        }
    }

    private static bool TryReadPort(object? value, out int port)
    {
        switch (value)
        {
            case int intPort:
                port = intPort;
                return true;
            case long longPort when longPort is > 0 and <= ushort.MaxValue:
                port = (int)longPort;
                return true;
            default:
                return int.TryParse(value?.ToString(), out port);
        }
    }

    private enum ConnectionRole
    {
        Token,
        Rest,
        Shared
    }

    private readonly record struct OriginKey(string Scheme, string Host, int Port);

    private readonly record struct EnvironmentRole(string Environment, ConnectionRole Role);

    private sealed record OriginAssignment(
        string Environment,
        ConnectionRole Role,
        OriginKey Origin,
        string DisplayOrigin);

    private readonly record struct ConnectionKey(
        string Environment,
        ConnectionRole Role,
        OriginKey Origin,
        string State,
        string PeerAddress,
        string ProtocolVersion);

    private readonly record struct PhysicalConnectionKey(
        string Environment,
        ConnectionRole Role,
        OriginKey Origin,
        string PeerAddress,
        string ProtocolVersion);

    private readonly record struct ConnectionTotals(long Token, long Rest, long Shared, long Total);

    private sealed record LogSnapshot(
        string Environment,
        ConnectionTotals Totals,
        long Active,
        long Idle,
        string? TokenOrigin,
        string? RestOrigin,
        IReadOnlyList<string> SharedOrigins,
        IReadOnlyList<string> PeerAddresses,
        IReadOnlyList<string> ProtocolVersions);
}
