using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Hbpos.Api;
using Hbpos.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudConnectionPoolTests
{
    private const string MetricsLogPrefix = "[HBPOS][Api][LinklyTcp] ";

    [Fact]
    public void AddHbposApiServices_registers_one_linkly_connection_metrics_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddHbposApiServices();

        var registrations = services.Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(LinklyHttpConnectionMetricsService));

        Assert.Single(registrations);
    }

    [Fact]
    public void Token_and_rest_clients_disable_factory_handler_rotation()
    {
        var services = new ServiceCollection();
        services.AddHbposApiServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();

        Assert.Equal(
            Timeout.InfiniteTimeSpan,
            options.Get(nameof(ILinklyCloudBackendTokenProvider)).HandlerLifetime);
        Assert.Equal(
            Timeout.InfiniteTimeSpan,
            options.Get(nameof(ILinklyCloudBackendAsyncTransport)).HandlerLifetime);
    }

    [Fact]
    public async Task Token_and_rest_clients_each_keep_at_most_one_real_connection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var tokenServer = new KeepAliveHttpServer();
        await using var restServer = new KeepAliveHttpServer();
        var services = new ServiceCollection();
        services.AddHbposApiServices();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var tokenClient = factory.CreateClient(nameof(ILinklyCloudBackendTokenProvider));
        using var restClient = factory.CreateClient(nameof(ILinklyCloudBackendAsyncTransport));

        // 先让 Token 连接回池中保持空闲，再验证 REST 池，覆盖容器内两个真实连接池的总上限。
        var tokenCompleted = await SendTwoConcurrentRequestsAsync(tokenClient, tokenServer.BaseAddress, timeout.Token);
        var restCompleted = await SendTwoConcurrentRequestsAsync(restClient, restServer.BaseAddress, timeout.Token);

        Assert.Equal(4, tokenCompleted + restCompleted);
        Assert.Equal(1, tokenServer.AcceptedConnections);
        Assert.Equal(1, restServer.AcceptedConnections);
        Assert.Equal(2, tokenServer.AcceptedConnections + restServer.AcceptedConnections);
    }

    [Fact]
    public async Task Metrics_service_records_two_physical_connections_without_duplicate_state_logs_then_zero()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var tokenServer = new KeepAliveHttpServer(TimeSpan.FromMilliseconds(400));
        await using var restServer = new KeepAliveHttpServer(TimeSpan.FromMilliseconds(400));
        var logger = new RecordingLogger<LinklyHttpConnectionMetricsService>();
        var options = Options.Create(new LinklyCloudBackendAsyncOptions
        {
            ProductionAuthBaseUrl = "https://production-token.example/",
            ProductionRestBaseUrl = "https://production-rest.example/",
            SandboxAuthBaseUrl = tokenServer.BaseAddress.ToString(),
            SandboxRestBaseUrl = restServer.BaseAddress.ToString()
        });
        using var service = new LinklyHttpConnectionMetricsService(options, logger);
        await service.StartAsync(timeout.Token);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), timeout.Token);
            Assert.Empty(logger.Snapshots);

            using (var tokenHandler = new SocketsHttpHandler())
            using (var restHandler = new SocketsHttpHandler())
            using (var tokenClient = new HttpClient(tokenHandler, disposeHandler: false))
            using (var restClient = new HttpClient(restHandler, disposeHandler: false))
            {
                await Task.WhenAll(
                    SendRequestAsync(tokenClient, tokenServer.BaseAddress, timeout.Token),
                    SendRequestAsync(restClient, restServer.BaseAddress, timeout.Token));

                var openSnapshot = await WaitForSnapshotAsync(
                    logger,
                    snapshot => snapshot.Environment == "Sandbox" && snapshot.TotalConnections == 2,
                    timeout.Token);
                Assert.Equal(1, openSnapshot.TokenConnections);
                Assert.Equal(1, openSnapshot.RestConnections);
                Assert.True(openSnapshot.WithinLimit);
                Assert.Equal(openSnapshot.TotalConnections, openSnapshot.ActiveConnections + openSnapshot.IdleConnections);
                Assert.Equal(LogLevel.Information, openSnapshot.Level);
                Assert.Equal("api-linkly-tcp", openSnapshot.Source);
                Assert.Equal("physical-connection-count", openSnapshot.Operation);
                Assert.Equal("snapshot", openSnapshot.Phase);
                Assert.Equal("outbound", openSnapshot.Direction);
                Assert.Equal(2, openSnapshot.Limit);
                Assert.Equal(ToOrigin(tokenServer.BaseAddress), openSnapshot.TokenOrigin);
                Assert.Equal(ToOrigin(restServer.BaseAddress), openSnapshot.RestOrigin);
                Assert.NotEmpty(openSnapshot.PeerAddresses);
                Assert.NotEmpty(openSnapshot.ProtocolVersions);
                Assert.Equal(openSnapshot.PeerAddresses.Order(StringComparer.Ordinal), openSnapshot.PeerAddresses);
                Assert.Equal(openSnapshot.ProtocolVersions.Order(StringComparer.Ordinal), openSnapshot.ProtocolVersions);
                Assert.Equal(openSnapshot.PeerAddresses.Distinct(StringComparer.Ordinal).Count(), openSnapshot.PeerAddresses.Count);
                Assert.Equal(openSnapshot.ProtocolVersions.Distinct(StringComparer.Ordinal).Count(), openSnapshot.ProtocolVersions.Count);
                Assert.DoesNotContain('\r', openSnapshot.RawMessage);
                Assert.DoesNotContain('\n', openSnapshot.RawMessage);

                var snapshotCount = logger.Snapshots.Count;
                await Task.WhenAll(
                    SendRequestAsync(tokenClient, tokenServer.BaseAddress, timeout.Token),
                    SendRequestAsync(restClient, restServer.BaseAddress, timeout.Token));
                await Task.Delay(TimeSpan.FromMilliseconds(400), timeout.Token);

                // 关键断言：复用连接只改变 active/idle，不应重复记录相同物理连接总数。
                Assert.Equal(snapshotCount, logger.Snapshots.Count);
                Assert.DoesNotContain(logger.Snapshots, snapshot => snapshot.TotalConnections > 2);
            }

            var closedSnapshot = await WaitForSnapshotAsync(
                logger,
                snapshot => snapshot.Environment == "Sandbox" && snapshot.TotalConnections == 0,
                timeout.Token);
            Assert.Equal(0, closedSnapshot.TokenConnections);
            Assert.Equal(0, closedSnapshot.RestConnections);
            Assert.True(closedSnapshot.WithinLimit);
            Assert.Equal(LogLevel.Information, closedSnapshot.Level);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Metrics_service_flushes_first_snapshot_while_connection_reuse_remains_continuous()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var traffic = new CancellationTokenSource();
        await using var server = new KeepAliveHttpServer(TimeSpan.FromMilliseconds(40));
        var logger = new RecordingLogger<LinklyHttpConnectionMetricsService>();
        var options = Options.Create(new LinklyCloudBackendAsyncOptions
        {
            ProductionAuthBaseUrl = server.BaseAddress.ToString(),
            ProductionRestBaseUrl = "https://production-rest.example/",
            SandboxAuthBaseUrl = "https://sandbox-token.example/",
            SandboxRestBaseUrl = "https://sandbox-rest.example/"
        });
        using var service = new LinklyHttpConnectionMetricsService(options, logger);
        using var handler = new SocketsHttpHandler();
        using var client = new HttpClient(handler);
        await service.StartAsync(timeout.Token);
        var requestCount = 0;
        var startedAt = Stopwatch.GetTimestamp();
        var trafficTask = SendContinuouslyAsync();

        try
        {
            while (Volatile.Read(ref requestCount) < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            using var observation = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
            var snapshot = await WaitForSnapshotAsync(
                logger,
                value => value.Environment == "Production" && value.TotalConnections == 1,
                observation.Token);

            // 关键断言：连续请求仍在复用连接时，固定 250ms 窗口必须已经输出首次物理连接快照。
            Assert.False(trafficTask.IsCompleted);
            Assert.True(Volatile.Read(ref requestCount) >= 3);
            Assert.True(Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromMilliseconds(250));
            Assert.Equal(1, snapshot.TokenConnections);
        }
        finally
        {
            traffic.Cancel();
            await trafficTask;
            await service.StopAsync(CancellationToken.None);
        }

        async Task SendContinuouslyAsync()
        {
            try
            {
                while (true)
                {
                    await SendRequestAsync(client, server.BaseAddress, traffic.Token);
                    Interlocked.Increment(ref requestCount);
                }
            }
            catch (OperationCanceledException) when (traffic.IsCancellationRequested)
            {
            }
        }
    }

    [Fact]
    public async Task Metrics_service_does_not_log_between_connection_state_delta_pair()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var origin = new Uri("http://linkly-transition.test:32123/");
        var logger = new RecordingLogger<LinklyHttpConnectionMetricsService>();
        var options = Options.Create(new LinklyCloudBackendAsyncOptions
        {
            ProductionAuthBaseUrl = origin.ToString(),
            ProductionRestBaseUrl = "https://production-rest.example/",
            SandboxAuthBaseUrl = "https://sandbox-token.example/",
            SandboxRestBaseUrl = "https://sandbox-rest.example/"
        });
        using var service = new LinklyHttpConnectionMetricsService(options, logger);
        await service.StartAsync(timeout.Token);
        using var meter = new Meter("System.Net.Http");
        var connections = meter.CreateUpDownCounter<long>("http.client.open_connections");
        var idleTags = CreateConnectionMetricTags(origin, "idle");
        var activeTags = CreateConnectionMetricTags(origin, "active");

        try
        {
            connections.Add(1, idleTags);
            await WaitForSnapshotAsync(
                logger,
                snapshot => snapshot.Environment == "Production" && snapshot.TotalConnections == 1,
                timeout.Token);
            var initialSnapshotCount = logger.Snapshots.Count;

            // 先完成 idle -> active 并启动固定窗口，再让下一次 active -> idle 跨过原 250ms 边界。
            connections.Add(-1, idleTags);
            connections.Add(1, activeTags);
            await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token);
            connections.Add(-1, activeTags);
            await Task.Delay(TimeSpan.FromMilliseconds(140), timeout.Token);
            connections.Add(1, idleTags);
            await Task.Delay(TimeSpan.FromMilliseconds(400), timeout.Token);

            Assert.DoesNotContain(logger.Snapshots, snapshot => snapshot.TotalConnections == 0);
            Assert.Equal(initialSnapshotCount, logger.Snapshots.Count);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Metrics_service_keeps_negative_delta_settling_isolated_by_physical_connection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tokenOrigin = new Uri("http://linkly-token-transition.test:32124/");
        var restOrigin = new Uri("http://linkly-rest-transition.test:32125/");
        var logger = new RecordingLogger<LinklyHttpConnectionMetricsService>();
        var options = Options.Create(new LinklyCloudBackendAsyncOptions
        {
            ProductionAuthBaseUrl = tokenOrigin.ToString(),
            ProductionRestBaseUrl = restOrigin.ToString(),
            SandboxAuthBaseUrl = "https://sandbox-token.example/",
            SandboxRestBaseUrl = "https://sandbox-rest.example/"
        });
        using var service = new LinklyHttpConnectionMetricsService(options, logger);
        await service.StartAsync(timeout.Token);
        using var meter = new Meter("System.Net.Http");
        var connections = meter.CreateUpDownCounter<long>("http.client.open_connections");
        var tokenIdleTags = CreateConnectionMetricTags(tokenOrigin, "idle");
        var tokenActiveTags = CreateConnectionMetricTags(tokenOrigin, "active");
        var restIdleTags = CreateConnectionMetricTags(restOrigin, "idle");
        var restActiveTags = CreateConnectionMetricTags(restOrigin, "active");

        try
        {
            connections.Add(1, tokenIdleTags);
            connections.Add(1, restIdleTags);
            await WaitForSnapshotAsync(
                logger,
                snapshot => snapshot.Environment == "Production" && snapshot.TotalConnections == 2,
                timeout.Token);
            var initialSnapshotCount = logger.Snapshots.Count;

            connections.Add(-1, tokenIdleTags);
            connections.Add(1, tokenActiveTags);
            await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token);
            connections.Add(-1, tokenActiveTags);

            // B origin 的完整状态切换不能清除 A origin 尚未配对的负 delta。
            connections.Add(-1, restIdleTags);
            connections.Add(1, restActiveTags);
            await Task.Delay(TimeSpan.FromMilliseconds(140), timeout.Token);
            connections.Add(1, tokenIdleTags);
            await Task.Delay(TimeSpan.FromMilliseconds(400), timeout.Token);

            Assert.DoesNotContain(logger.Snapshots, snapshot => snapshot.TotalConnections == 1);
            Assert.Equal(initialSnapshotCount, logger.Snapshots.Count);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Metrics_service_warns_and_starts_when_urls_are_invalid_or_share_an_origin()
    {
        await using var sharedServer = new KeepAliveHttpServer();
        var logger = new RecordingLogger<LinklyHttpConnectionMetricsService>();
        var sharedOrigin = sharedServer.BaseAddress.ToString();
        var options = Options.Create(new LinklyCloudBackendAsyncOptions
        {
            ProductionAuthBaseUrl = "not-a-valid-url",
            ProductionRestBaseUrl = sharedOrigin,
            SandboxAuthBaseUrl = sharedOrigin,
            SandboxRestBaseUrl = "https://sandbox-rest.example/"
        });
        using var service = new LinklyHttpConnectionMetricsService(options, logger);

        var exception = await Record.ExceptionAsync(() => service.StartAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("not-a-valid-url", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(sharedServer.BaseAddress.Authority, StringComparison.Ordinal));
        using var handler = new SocketsHttpHandler();
        using var client = new HttpClient(handler);
        await SendRequestAsync(client, sharedServer.BaseAddress, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Empty(logger.Snapshots);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Metrics_service_stop_waits_for_an_inflight_snapshot_write()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = new KeepAliveHttpServer(TimeSpan.FromMilliseconds(400));
        var logger = new RecordingLogger<LinklyHttpConnectionMetricsService>(blockSnapshotWrites: true);
        var options = Options.Create(new LinklyCloudBackendAsyncOptions
        {
            ProductionAuthBaseUrl = server.BaseAddress.ToString(),
            ProductionRestBaseUrl = "https://production-rest.example/",
            SandboxAuthBaseUrl = "https://sandbox-token.example/",
            SandboxRestBaseUrl = "https://sandbox-rest.example/"
        });
        using var service = new LinklyHttpConnectionMetricsService(options, logger);
        using var handler = new SocketsHttpHandler();
        using var client = new HttpClient(handler);
        await service.StartAsync(timeout.Token);
        var requestTask = SendRequestAsync(client, server.BaseAddress, timeout.Token);
        Task? stopTask = null;

        try
        {
            await logger.SnapshotWriteStarted.WaitAsync(timeout.Token);
            stopTask = service.StopAsync(CancellationToken.None);

            // 关键断言：Stop 必须等待正在执行的 Timer 回调结束，不能返回后继续写过期快照。
            Assert.False(stopTask.IsCompleted);
        }
        finally
        {
            logger.ReleaseSnapshotWrite();
            await (stopTask ?? service.StopAsync(CancellationToken.None));
            await requestTask;
        }
    }

    private static async Task<int> SendTwoConcurrentRequestsAsync(
        HttpClient client,
        Uri baseAddress,
        CancellationToken cancellationToken)
    {
        using var firstRequest = CreateHttp11Request(baseAddress, 1);
        using var secondRequest = CreateHttp11Request(baseAddress, 2);
        var responses = await Task.WhenAll(
            client.SendAsync(firstRequest, cancellationToken),
            client.SendAsync(secondRequest, cancellationToken));

        try
        {
            foreach (var response in responses)
            {
                response.EnsureSuccessStatusCode();
            }

            return responses.Length;
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task SendRequestAsync(
        HttpClient client,
        Uri baseAddress,
        CancellationToken cancellationToken)
    {
        using var request = CreateHttp11Request(baseAddress, 1);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<MetricsSnapshot> WaitForSnapshotAsync(
        RecordingLogger<LinklyHttpConnectionMetricsService> logger,
        Func<MetricsSnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var snapshot = logger.Snapshots.LastOrDefault(predicate);
            if (snapshot is not null)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    private static HttpRequestMessage CreateHttp11Request(Uri baseAddress, int requestNumber) =>
        new(HttpMethod.Get, new Uri(baseAddress, $"request-{requestNumber}"))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

    private static string ToOrigin(Uri uri) =>
        $"{uri.Scheme}://{uri.DnsSafeHost}:{uri.Port}";

    private static KeyValuePair<string, object?>[] CreateConnectionMetricTags(Uri origin, string state) =>
    [
        new("url.scheme", origin.Scheme),
        new("server.address", origin.DnsSafeHost),
        new("server.port", origin.Port),
        new("http.connection.state", state),
        new("network.peer.address", "127.0.0.1"),
        new("network.protocol.version", "1.1")
    ];

    private sealed class KeepAliveHttpServer : IAsyncDisposable
    {
        private static readonly byte[] ResponseBytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: keep-alive\r\n\r\n{}");

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly object _sync = new();
        private readonly List<TcpClient> _clients = [];
        private readonly List<Task> _clientTasks = [];
        private readonly Task _acceptTask;
        private int _acceptedConnections;

        public KeepAliveHttpServer(TimeSpan? responseDelay = null)
        {
            ResponseDelay = responseDelay ?? TimeSpan.FromMilliseconds(200);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseAddress = new Uri($"http://{endpoint.Address}:{endpoint.Port}/");
            _acceptTask = AcceptConnectionsAsync();
        }

        public Uri BaseAddress { get; }

        private TimeSpan ResponseDelay { get; }

        public int AcceptedConnections => Volatile.Read(ref _acceptedConnections);

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();

            lock (_sync)
            {
                foreach (var client in _clients)
                {
                    client.Close();
                }
            }

            await IgnoreExpectedShutdownExceptionAsync(_acceptTask);

            Task[] clientTasks;
            lock (_sync)
            {
                clientTasks = [.. _clientTasks];
            }

            await Task.WhenAll(clientTasks.Select(IgnoreExpectedShutdownExceptionAsync));
            _shutdown.Dispose();
        }

        private async Task AcceptConnectionsAsync()
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                Interlocked.Increment(ref _acceptedConnections);

                lock (_sync)
                {
                    _clients.Add(client);
                    _clientTasks.Add(HandleClientAsync(client));
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            await using (var stream = client.GetStream())
            using (var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true))
            {
                while (true)
                {
                    var requestLine = await reader.ReadLineAsync(_shutdown.Token);
                    if (requestLine is null)
                    {
                        return;
                    }

                    string? header;
                    do
                    {
                        header = await reader.ReadLineAsync(_shutdown.Token);
                        if (header is null)
                        {
                            return;
                        }
                    }
                    while (header.Length > 0);

                    // 延迟响应，确保两个并发请求确实争用同一连接池额度。
                    await Task.Delay(ResponseDelay, _shutdown.Token);
                    await stream.WriteAsync(ResponseBytes, _shutdown.Token);
                    await stream.FlushAsync(_shutdown.Token);
                }
            }
        }

        private static async Task IgnoreExpectedShutdownExceptionAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();
        private readonly bool _blockSnapshotWrites;
        private readonly ManualResetEventSlim _snapshotWriteRelease = new(false);
        private readonly TaskCompletionSource _snapshotWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingLogger(bool blockSnapshotWrites = false)
        {
            _blockSnapshotWrites = blockSnapshotWrites;
        }

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public Task SnapshotWriteStarted => _snapshotWriteStarted.Task;

        public IReadOnlyList<MetricsSnapshot> Snapshots =>
            [.. _entries
                .Where(entry => IsSnapshotMessage(entry.Message))
                .Select(TryParseSnapshot)
                .OfType<MetricsSnapshot>()];

        public void ReleaseSnapshotWrite() => _snapshotWriteRelease.Set();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var entry = new LogEntry(logLevel, formatter(state, exception));
            if (_blockSnapshotWrites && IsSnapshotMessage(entry.Message))
            {
                _snapshotWriteStarted.TrySetResult();
                _snapshotWriteRelease.Wait();
            }

            _entries.Enqueue(entry);
        }

        private static bool IsSnapshotMessage(string message) =>
            message.StartsWith(MetricsLogPrefix + "{", StringComparison.Ordinal);

        private static MetricsSnapshot? TryParseSnapshot(LogEntry entry)
        {
            using var document = JsonDocument.Parse(entry.Message[MetricsLogPrefix.Length..]);
            var root = document.RootElement;
            if (root.GetProperty("operation").GetString() != "physical-connection-count")
            {
                return null;
            }

            var details = root.GetProperty("details");
            return new MetricsSnapshot(
                entry.Level,
                entry.Message,
                root.GetProperty("source").GetString()!,
                root.GetProperty("operation").GetString()!,
                root.GetProperty("phase").GetString()!,
                root.GetProperty("direction").GetString()!,
                root.GetProperty("environment").GetString()!,
                details.GetProperty("tokenConnections").GetInt64(),
                details.GetProperty("restConnections").GetInt64(),
                details.GetProperty("totalConnections").GetInt64(),
                details.GetProperty("activeConnections").GetInt64(),
                details.GetProperty("idleConnections").GetInt64(),
                details.GetProperty("limit").GetInt32(),
                details.GetProperty("withinLimit").GetBoolean(),
                details.GetProperty("tokenOrigin").GetString(),
                details.GetProperty("restOrigin").GetString(),
                details.GetProperty("peerAddresses").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                details.GetProperty("protocolVersions").EnumerateArray().Select(value => value.GetString()!).ToArray());
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed record MetricsSnapshot(
        LogLevel Level,
        string RawMessage,
        string Source,
        string Operation,
        string Phase,
        string Direction,
        string Environment,
        long TokenConnections,
        long RestConnections,
        long TotalConnections,
        long ActiveConnections,
        long IdleConnections,
        int Limit,
        bool WithinLimit,
        string? TokenOrigin,
        string? RestOrigin,
        IReadOnlyList<string> PeerAddresses,
        IReadOnlyList<string> ProtocolVersions);
}
