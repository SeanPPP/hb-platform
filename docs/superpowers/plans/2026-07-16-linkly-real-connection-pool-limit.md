# Linkly 真实连接池上限 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在当前单个 `Hbpos.Api` 容器内，让 Linkly Token 与 REST 两个真实 HTTP 连接池各最多保留一条 TCP 连接，并用真实回环 socket 测试证明合计不超过两条。

**Architecture:** 保留两个现有 typed `HttpClient` 和同终端请求闸门；仅在两个客户端的 `SocketsHttpHandler` 上设置 `MaxConnectionsPerServer = 1`。测试通过项目真实 `IHttpClientFactory` 获取客户端，使用两个 `TcpListener` 模拟不同 Linkly 域名，统计真实 accepted connections。

**Tech Stack:** .NET 9、`IHttpClientFactory`、`SocketsHttpHandler`、`TcpListener`、xUnit。

---

## 文件结构

- Create: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudConnectionPoolTests.cs` — 真实 HTTP/1.1 keep-alive 回环服务器和连接池上限测试。
- Modify: `apps/pos-wpf/src/Hbpos.Api/ServiceRegistration.cs:104-112` — 给 Token/REST typed clients 分别设置每目标一条连接。
- Preserve: `apps/pos-wpf/src/Hbpos.Api/ServiceRegistration.cs` 中现有 Attendance 未提交改动；不重排、不格式化、不提交整个文件。

### Task 1: 先用真实 socket 复现四条连接

**Files:**
- Create: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudConnectionPoolTests.cs`

- [ ] **Step 1: 写入失败测试和最小回环服务器**

```csharp
using System.Collections.Concurrent;
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
    public async Task Linkly_token_and_rest_real_connection_pools_keep_at_most_two_tcp_connections()
    {
        await using var tokenServer = new KeepAliveLoopbackServer();
        await using var restServer = new KeepAliveLoopbackServer();
        var services = new ServiceCollection();
        services.AddHbposApiServices();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var tokenClient = factory.CreateClient(nameof(ILinklyCloudBackendTokenProvider));
        using var restClient = factory.CreateClient(nameof(ILinklyCloudBackendAsyncTransport));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // 先让 Token 连接进入 keep-alive idle，再建立 REST 连接，复现两个独立池叠加。
        await SendConcurrentPairAsync(tokenClient, tokenServer.BaseUri, timeout.Token);
        await SendConcurrentPairAsync(restClient, restServer.BaseUri, timeout.Token);

        Assert.Equal(1, tokenServer.AcceptedConnectionCount);
        Assert.Equal(1, restServer.AcceptedConnectionCount);
        Assert.Equal(2, tokenServer.AcceptedConnectionCount + restServer.AcceptedConnectionCount);
    }

    private static Task SendConcurrentPairAsync(
        HttpClient client,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        return Task.WhenAll(
            SendAsync(client, new Uri(baseUri, "first"), cancellationToken),
            SendAsync(client, new Uri(baseUri, "second"), cancellationToken));
    }

    private static async Task SendAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private sealed class KeepAliveLoopbackServer : IAsyncDisposable
    {
        private static readonly byte[] ResponseBytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: application/json\r\nConnection: keep-alive\r\n\r\n{}");
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<TcpClient> _clients = [];
        private readonly ConcurrentBag<Task> _clientTasks = [];
        private readonly Task _acceptTask;
        private int _acceptedConnectionCount;

        internal KeepAliveLoopbackServer()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/", UriKind.Absolute);
            _acceptTask = AcceptConnectionsAsync();
        }

        internal Uri BaseUri { get; }

        internal int AcceptedConnectionCount => Volatile.Read(ref _acceptedConnectionCount);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            await IgnoreShutdownAsync(_acceptTask);
            await Task.WhenAll(_clientTasks.Select(IgnoreShutdownAsync));
            _stop.Dispose();
        }

        private async Task AcceptConnectionsAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _clients.Add(client);
                Interlocked.Increment(ref _acceptedConnectionCount);
                _clientTasks.Add(HandleClientAsync(client));
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            while (!_stop.IsCancellationRequested)
            {
                var requestLine = await reader.ReadLineAsync(_stop.Token);
                if (requestLine is null)
                {
                    return;
                }

                string? header;
                do
                {
                    header = await reader.ReadLineAsync(_stop.Token);
                }
                while (!string.IsNullOrEmpty(header));

                // 保持两个并发请求重叠，默认无限连接池会稳定建立两条连接。
                await Task.Delay(TimeSpan.FromMilliseconds(200), _stop.Token);
                await stream.WriteAsync(ResponseBytes, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
        }

        private static async Task IgnoreShutdownAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
            {
            }
        }
    }
}
```

- [ ] **Step 2: 运行测试，确认 RED 原因是连接数为四**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudConnectionPoolTests" --artifacts-path .artifacts/linkly-real-pool-red --logger "console;verbosity=normal"
```

Expected: FAIL；Token expected `1` but actual `2`（或最终 total expected `2` but actual `4`）。如果测试因编译、超时或服务器关闭异常失败，先修正测试，直到只因真实 accepted connection 计数超限而失败。

### Task 2: 给两个真实连接池各设置一条连接

**Files:**
- Modify: `apps/pos-wpf/src/Hbpos.Api/ServiceRegistration.cs:104-112`
- Test: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudConnectionPoolTests.cs`

- [ ] **Step 1: 修改前执行影响检查**

Run GitNexus `impact` for `AddHbposApiServices`, direction `upstream`. 如果服务仍返回 `Transport closed`，记录降级原因，并使用 codebase-memory `trace_path`/`search_code` 核对调用者和两个 typed client 注册。

- [ ] **Step 2: 最小修改两个 handler**

将两个现有注册改为：

```csharp
        services.AddHttpClient<ILinklyCloudBackendAsyncTransport, HttpLinklyCloudBackendAsyncTransport>(client =>
        {
            client.Timeout = LinklyCloudBackendTimeoutPolicy.HttpTimeout;
        })
        .UseSocketsHttpHandler((handler, _) =>
        {
            // 关键逻辑：REST 目标只保留一条 TCP 连接，与 Token 目标合计最多两条。
            handler.MaxConnectionsPerServer = 1;
        });
        services.AddHttpClient<ILinklyCloudBackendTokenProvider, HttpLinklyCloudBackendTokenProvider>(client =>
        {
            client.Timeout = LinklyCloudBackendTimeoutPolicy.HttpTimeout;
        })
        .UseSocketsHttpHandler((handler, _) =>
        {
            // 关键逻辑：Token 目标只保留一条 TCP 连接，与 REST 目标合计最多两条。
            handler.MaxConnectionsPerServer = 1;
        });
```

只修改这两个注册块，不格式化 `ServiceRegistration.cs`，不改动其中现有 Attendance 代码。

- [ ] **Step 3: 运行真实连接池测试，确认 GREEN**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudConnectionPoolTests" --artifacts-path .artifacts/linkly-real-pool-green --logger "console;verbosity=normal"
```

Expected: PASS；Token accepted `1`、REST accepted `1`、total `2`，四个请求全部完成。

### Task 3: 回归验证和独立审查

**Files:**
- Verify: `apps/pos-wpf/src/Hbpos.Api/ServiceRegistration.cs`
- Verify: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudConnectionPoolTests.cs`
- Verify: `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudBackendAsyncServiceTests.cs`

- [ ] **Step 1: 运行 Linkly 请求闸门与真实连接池测试**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --filter "FullyQualifiedName~LinklyCloudConnectionPoolTests|FullyQualifiedName~Same_linkly_terminal_outbound_requests_share_a_maximum_concurrency_of_two|FullyQualifiedName~Different_linkly_terminals_do_not_share_the_same_two_request_limit" --artifacts-path .artifacts/linkly-real-pool-focused --logger "console;verbosity=minimal"
```

Expected: 3 passed, 0 failed。

- [ ] **Step 2: 运行完整 API 测试**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --artifacts-path .artifacts/linkly-real-pool-full --logger "console;verbosity=minimal"
```

Expected: 全部通过，0 failed。

- [ ] **Step 3: 检查差异**

Run:

```powershell
git diff --check -- apps/pos-wpf/src/Hbpos.Api/ServiceRegistration.cs apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudConnectionPoolTests.cs
git diff -- apps/pos-wpf/src/Hbpos.Api/ServiceRegistration.cs apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudConnectionPoolTests.cs
```

Expected: 无空白错误；只出现两个 Linkly handler 配置块和新的真实连接池测试。由于 `ServiceRegistration.cs` 已有用户 Attendance 改动，最终报告必须明确区分，不得执行整文件暂存或提交。

- [ ] **Step 4: 独立审查**

让独立 reviewer 核对：测试确实通过真实 `SocketsHttpHandler`/TCP、修复前 RED 原因是四条连接、修复后两个连接池各一条、没有通过 fake handler 或仅统计请求并发误判。

- [ ] **Step 5: 变更影响检查**

运行 GitNexus `detect_changes(scope: all)`；不可用时使用 codebase-memory `detect_changes`，再结合精确 diff 说明影响范围。此任务不自动提交实现代码，等待用户明确要求提交。
