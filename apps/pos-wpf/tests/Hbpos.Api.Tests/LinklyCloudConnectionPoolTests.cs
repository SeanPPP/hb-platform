using System.Net;
using System.Net.Sockets;
using System.Text;
using Hbpos.Api;
using Hbpos.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudConnectionPoolTests
{
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

    private static HttpRequestMessage CreateHttp11Request(Uri baseAddress, int requestNumber) =>
        new(HttpMethod.Get, new Uri(baseAddress, $"request-{requestNumber}"))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

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

        public KeepAliveHttpServer()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseAddress = new Uri($"http://{endpoint.Address}:{endpoint.Port}/");
            _acceptTask = AcceptConnectionsAsync();
        }

        public Uri BaseAddress { get; }

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
                    await Task.Delay(TimeSpan.FromMilliseconds(200), _shutdown.Token);
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
}
