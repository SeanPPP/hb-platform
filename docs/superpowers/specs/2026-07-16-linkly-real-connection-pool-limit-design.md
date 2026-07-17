# Linkly 真实连接池上限设计

## 目标

在当前单个 `Hbpos.Api` 容器内，生产或沙箱环境中的 Linkly Cloud Token 与 REST 两个目标合计最多保留两条 TCP 连接，同时保留现有“同终端最多两个在途 HTTP 请求”的业务闸门。

## 非目标

- 不实现多 API 实例之间的分布式连接租约。
- 不创建每终端独立的 `SocketsHttpHandler` 缓存。
- 不修改支付、恢复、通知或状态判定逻辑。
- 不修改 WPF 到 `Hbpos.Api` 的连接池。

## 方案

Linkly Token 与 REST 使用不同域名，因此继续保留两个 typed `HttpClient`。在两个客户端的真实 `SocketsHttpHandler` 上分别配置：

```text
MaxConnectionsPerServer = 1
```

同一环境由一个 Token 目标和一个 REST 目标组成，所以最多保留一条 Token TCP 连接和一条 REST TCP 连接，合计两条。HTTP/1.1 的额外请求由 handler 排队；HTTP/2 可以在单连接内复用请求。

现有 `LinklyCloudTerminalConcurrencyGate` 继续限制同终端 Token 与 REST 的在途请求总数为二。连接池限制负责 socket 数量，请求闸门负责业务并发，两者职责不混合。

## 真实连接池测试

新增一个仅监听回环地址的集成测试：

1. 启动两个 `TcpListener`，分别模拟 Token 与 REST 服务。
2. 通过项目真实 `IHttpClientFactory` 注册创建两个 Linkly typed client 对应的 `HttpClient`。
3. 强制使用 HTTP/1.1，并让测试服务器支持同一 TCP 连接上的 keep-alive 顺序请求。
4. 先向 Token 目标并发发送两个请求，完成后保留 idle 连接。
5. 再向 REST 目标并发发送两个请求。
6. 修复前，默认连接池会接受四条 TCP 连接，测试应以“实际连接数超过二”失败。
7. 修复后，两个监听器各只接受一条连接，四个 HTTP 请求仍全部完成，合计连接数严格等于二。

测试直接统计 `TcpListener.AcceptTcpClientAsync` 接受的真实连接，不使用 fake `HttpMessageHandler`，并在测试结束时取消监听、释放客户端和 socket，避免影响其他测试。

## 错误与超时

- 保留现有 `HttpClient.Timeout`。
- 排队等待连接时继续响应调用方取消令牌和超时。
- 不添加重试；支付请求的未知结果和恢复语义保持不变。
- 测试服务器使用有界等待，失败时输出 Token、REST 和总连接数，避免测试挂死。

## 修改范围

- `apps/pos-wpf/src/Hbpos.Api/ServiceRegistration.cs`
- `apps/pos-wpf/tests/Hbpos.Api.Tests/LinklyCloudConnectionPoolTests.cs`

## 验收标准

- 新测试在生产修改前因真实连接数为四而失败。
- 生产修改后，新测试观察到 Token 一条、REST 一条、合计两条连接。
- 现有 Linkly 同终端/不同终端请求并发测试继续通过。
- `Hbpos.Api.Tests` 全套通过。
- `git diff --check` 通过，且不覆盖工作区内 Attendance 等无关改动。
