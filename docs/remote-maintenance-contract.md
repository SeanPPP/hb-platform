# RustDesk 远程维护契约

版本：`1`。此文档是 HBWeb、中心 API、POS API 和独立 Windows 状态 Agent 的共享边界。

## 目标与边界

- RustDesk 原客户端直接连接配置的 relay/id server；HBWeb 不实现 RustDesk 通讯录或兼容协议。
- 中心 API 只向系统管理员的 HBWeb 暴露设备列表、一次性密码读取和已认证 artifact 下载；普通角色即使拥有其他设备权限也不能访问远程维护台账。
- POS API 仅为当前已经通过 POS 设备鉴权、仍有效的 Windows POS 设备转发 `prepare`/`commit`；远程维护策略要求有效 cashier ticket 与分店、设备号、硬件号绑定，并重新核对硬件号对应的全局最新 POSM 授权码、分店、设备号、状态、类型和系统，不要求 `Settings.DeviceRegistration` 管理权限，也不受 Audit 模式空请求绕过。中心 API 与 POS API 之间使用服务端专用 API key，不能下发到客户端。
- 独立 Windows status agent 不使用 POS 设备 secret，只持有某设备的随机 monitor token。token 只在传输中出现，中心数据库只保存其 hash；commit 响应中 token 复用主后端持久化 DataProtection key ring 并以独立 purpose 加密保存，以便同一 `operationId` 重试恢复。
- feature 默认关闭；中心只有在配置完整且所有 manifest artifact 的路径、SHA-256、大小和下载地址有效时才允许 prepare/commit。

## JSON 与错误

所有 JSON 使用普通 DTO，不包 `ApiResult` 外壳。成功返回 HTTP 2xx。错误返回 Problem Details（或现有异常中间件等价的 HTTP 错误），至少包含稳定 `code`；客户端不得根据错误文案判断状态。credential 返回始终设置 `Cache-Control: no-store`、`Pragma: no-cache`，响应体只包含密码。

### 管理员设备列表

`GET /api/remote-maintenance/admin/devices`

权限：系统管理员。查询参数：`page=1`、`pageSize=20`（服务端限制最大值）、`keyword`、`storeCode`、`onlineStatus`、`serviceStatus`。

```json
{
  "items": [{
    "id": "00000000-0000-0000-0000-000000000000",
    "deviceRegistrationId": 123,
    "storeCode": "S001",
    "deviceCode": "POS-01",
    "computerName": "POS-01",
    "rustdeskId": "123456789",
    "clientVersion": "1.0.0",
    "agentVersion": "1.0.0",
    "onlineStatus": "online",
    "serviceStatus": "running",
    "lastSeenAtUtc": "2026-01-01T00:00:00Z",
    "registeredAtUtc": "2026-01-01T00:00:00Z",
    "isStale": false
  }],
  "total": 1,
  "page": 1,
  "pageSize": 20,
  "serverTimeUtc": "2026-01-01T00:00:00Z"
}
```

`onlineStatus` 只有 `online`、`offline`、`never`。服务端接收心跳时间在最近 60 秒内为 `online`；没有任何成功心跳为 `never`；曾经心跳但已超过 60 秒为 `offline`。`isStale` 表示持久化快照已经超过 60 秒，供 UI 区分旧状态。`serviceStatus` 只有 `notInstalled`、`running`、`stopped`、`starting`、`stopping`、`checkFailed`。

### 管理员读取密码

`GET /api/remote-maintenance/admin/devices/{id}/credential`

权限：系统管理员。

```json
{ "password": "random-password" }
```

密码在中心持久化时使用独立 DataProtection purpose 加密，随机生成；数据库和列表永远不返回明文。只有该 endpoint 返回明文，并且每次读取都写入不含密码、token 或其他 secret 的管理员审计事件。

### 管理员读取 manifest

`GET /api/remote-maintenance/admin/manifest`

权限：系统管理员。返回：

```json
{
  "idServer": "hotbargain.vip:21116",
  "relayServer": "hotbargain.vip:21117",
  "publicKey": "public-key",
  "rustdesk": {
    "version": "1.0.0", "fileName": "rustdesk.exe",
    "downloadUrl": "/api/remote-maintenance/admin/artifacts/rustdesk",
    "sha256": "64-lowercase-hex", "sizeBytes": 12345678
  },
  "statusAgent": {
    "version": "1.0.0", "fileName": "hb-status-agent.exe",
    "downloadUrl": "/api/remote-maintenance/admin/artifacts/status-agent",
    "sha256": "64-lowercase-hex", "sizeBytes": 123456
  }
}
```

`downloadUrl` 必须是受认证的管理员地址或设备专属短期签名地址；不得是未经认证的 secret 数据地址。

POS `prepare` 返回的两个 `downloadUrl` 必须改写为 POS 本地的
`GET /api/remote-maintenance/artifacts/rustdesk` 与
`GET /api/remote-maintenance/artifacts/status-agent`。这两个 POS proxy endpoint
要求当前设备鉴权，再由 POS API 使用 server-only key 访问中心同路径的 internal
artifact endpoint；因此收银端不会直接拿管理员 URL 下载。

### 管理员下载 artifact

`GET /api/remote-maintenance/admin/artifacts/{kind}`

权限：系统管理员。`kind` 仅允许 `rustdesk`、`status-agent`。以流返回配置的固定文件，响应使用 `no-store`；服务端根据配置中的已校验路径读取，绝不信任请求体、query 或客户端提供的路径。artifact 不齐全、hash/size 不匹配时返回 `503 REMOTE_MAINTENANCE_NOT_READY`。

### POS prepare

`POST /api/remote-maintenance/prepare`

权限：当前 POS API 的既有设备鉴权（设备 header 中的硬件号/授权码）。请求：

```json
{ "operationId": "00000000-0000-0000-0000-000000000000", "computerName": "POS-01" }
```

POS API 必须从现有设备鉴权上下文取得 hardware id，并向中心验证当前有效的 POSM 登记和归属；请求中的 `computerName` 只作为待登记值。中心确认 feature 已启用、manifest 已准备好、设备登记有效且为 Windows POS 后，返回：

POS 端还必须从原始 `Authorization: Bearer <deviceAuthorizationCode>` 重新核对以硬件号查询到的全局最新 POSM 登记，授权码、状态、类型和系统必须完全匹配；旧授权码即使曾经生成过也不得继续进入远程维护。

```json
{
  "operationId": "00000000-0000-0000-0000-000000000000",
  "deviceId": "00000000-0000-0000-0000-000000000000",
  "config": {
    "idServer": "hotbargain.vip:21116", "relayServer": "hotbargain.vip:21117", "publicKey": "public-key"
  },
  "artifactManifest": { "rustdesk": {}, "statusAgent": {} }
}
```

`config` 与 `artifactManifest` 字段形状与 manifest 相同，但不得包含管理员凭据、monitor token 或 POS 服务 key。下载必须再次走设备鉴权的 POS proxy 或短期 signed URL。

### POS commit

`POST /api/remote-maintenance/commit`

权限：同一 POS 设备鉴权。请求：

```json
{
  "operationId": "00000000-0000-0000-0000-000000000000",
  "rustdeskId": "123456789",
  "clientVersion": "1.0.0",
  "password": "random-password"
}
```

服务端验证 operation 属于当前设备、RustDesk ID/版本格式、密码与中心已加密凭据一致；不得借 commit 改密码。成功返回：

```json
{
  "deviceId": "00000000-0000-0000-0000-000000000000",
  "monitorToken": "random-monitor-token",
  "heartbeatUrl": "/api/remote-maintenance/devices/00000000-0000-0000-0000-000000000000/heartbeat"
}
```

第一次 commit 的密码由本机生成并提交；prepare 不预置密码，中心第一次 commit 只加密保存收到的密码。之后同一 `operationId`、同一有效设备的重复 commit 必须比较相同密码并恢复同一 device 与 monitor token；不得新建凭据、轮换密码或生成不同 token。不同设备、不同密码或不匹配 operation 返回冲突/禁止错误。monitor token 的加密操作响应复用主后端已持久化 DataProtection key ring，但使用独立 purpose；key 丢失时 fail closed，而不是生成不可恢复的新 token。

设备存在未完成或已提交的 operation 时，prepare 不允许新 operation 接管，以免覆盖凭据或心跳状态；客户端必须恢复本机原 operation journal。确需重新安装/重置时走运维受控清理流程后再生成新 operation。

### Agent heartbeat

`POST /api/remote-maintenance/devices/{deviceId}/heartbeat`

权限：`Authorization: Bearer <monitorToken>`，token 只属于 path 中的 device，Agent 独立运行且不持有 POS secret。请求：

```json
{
  "sequence": 42,
  "agentVersion": "1.0.0",
  "rustdeskId": "123456789",
  "clientVersion": "1.0.0",
  "serviceStatus": "running"
}
```

服务端仅接受严格大于持久化 `lastAcceptedSequence` 的 sequence。重复或乱序请求返回冲突/无效 sequence，不能刷新 `lastSeenAtUtc`；token hash 恒时比较，设备必须仍有效且未吊销，接口限频。心跳只更新 agent/service/sequence/lastSeen 字段，保留 commit 登记的 RustDesk ID 与客户端版本。只保存该设备最新快照（不产生心跳历史）。成功心跳以服务端 UTC 接收时间作为 `lastSeenAtUtc`。

## 权限、审计与配置

所有远程维护 admin、credential、prepare/commit 管理动作和 artifact 下载均要求系统管理员角色；不得只靠可委派的普通权限代码放行。管理员入口只显示给系统管理员。所有 credential、prepare、commit、设备状态变更审计均不含 password、monitorToken、POS key、Authorization header 或下载内容。

中心配置 section：`RemoteMaintenance`，默认 `Enabled=false`，并包含 `IdServer`、`RelayServer`、`PublicKey`、`RustDeskArtifact`、`StatusAgentArtifact`（每项 `Version`、`FileName`、绝对或受控根目录下的 `Path`、`Sha256`、`SizeBytes`）、`HeartbeatBaseUrl`、`InterApiKey`、`OnlineThresholdSeconds=60`、`HeartbeatMinIntervalSeconds=10`、`MaxPageSize=100`。复用主后端既有持久 DataProtection key ring，仅使用远程维护独立 purpose；server-only `InterApiKey` 不得写入仓库。容器内 artifact 目录固定为 `/app/App_Data/RemoteMaintenance/artifacts`（宿主机由部署挂载到 `/www/HBWeb/remote-maintenance/artifacts`）。当前 RustDesk artifact 固定版本 `1.4.9`、文件名 `rustdesk-1.4.9-x86_64.exe`、SHA-256 `eaedeb0088e687bf46f7c46a9c6ea5493ce51f3134dfd6acbedb47b5b9136274`、大小 `24472432`。

## 数据库迁移与启动门禁

迁移至少建立：远程设备最新快照/归属表（唯一 `DeviceRegistrationId`）、凭据密文、monitor token hash、加密 commit 操作响应、严格 sequence、时间戳和 schema 版本；凭据密文、token hash、操作响应不得进入列表 DTO。中心主库沿既有正确 db 模式，POS 归属读取既有 POSM 有效登记。

增量 migration 由运维显式执行，应用正常启动只读检查，不隐式创建或修改生产 schema。当前可执行命令为（连接参数从受保护运维环境注入，不写入仓库）：

```sh
dotnet run --project services/backend/BlazorApp.Api -- --schema=remote-maintenance
```

该命令通过既有 schema 程序的独立迁移入口，在主库事务中幂等建立远程维护表和索引；它不会由正常 Web 启动触发。只读门禁为 `dotnet run --project services/backend/BlazorApp.Api -- --schema=remote-maintenance-check`，另外可用全局 `--schema=check` 验证主库基础账本。应用启动或每次 prepare 前都执行只读 readiness 检查；feature 未启用、迁移缺失、配置缺失、文件不存在、大小或 SHA-256 不匹配时 fail closed，并保持旧业务启动。
