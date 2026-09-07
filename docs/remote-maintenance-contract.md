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

## 启用与停用（运维顺序）

以下操作只适用于受保护的部署机环境；`InterApiKey`、连接串或其他 secret 不得写入仓库。`REMOTE_MAINTENANCE_PUBLIC_KEY` 是 RustDesk 公钥，不是 secret，但仍应由受控部署配置注入。中心 API 使用 `services/backend/docker-compose.yml` 的 `hb-api`，POS API 使用 `apps/pos-wpf/docker-compose.hotbargain.yml` 的 `hbpos-api`。

1. **部署前只读核对并准备回退。** 先确认当前服务器、域名、Compose project、目标服务（`hb-api`、`hbpos-api`）和容器确为本次变更对象；只读核对两个服务当前镜像/tag 或 digest、容器状态及健康状态。通过受保护运维配置核对中心主库和 POSM 库的目标与连接配置指纹，不在终端或日志输出连接串/密钥。确认已有可恢复的数据库备份/快照、当前运行镜像及其回退镜像/tag（记录备份标识和 SHA-256）；无法验证备份或回退路径时停止，不打开功能。
2. **先保持两端关闭并准备 RustDesk 服务。** 两个应用的 `REMOTE_MAINTENANCE_ENABLED` 都保持 `false`。确认 `rustdesk-server` Compose 项目已在 `/www/rustdesk-server` 启动 `rustdesk-server` 和 `rustdesk-relay`，且持久数据挂载到 `/www/rustdesk-server/data:/root`；端口、公钥和回滚边界见 [`scripts/ops/rustdesk/README.md`](../scripts/ops/rustdesk/README.md)。
3. **准备中心公钥和完整 artifact。** 将与正在运行的 RustDesk 服务匹配的 `id_ed25519.pub` 内容作为一行值注入 `REMOTE_MAINTENANCE_PUBLIC_KEY`，只能使用公钥，不能使用私钥。把 RustDesk 客户端和 status agent 两个文件放入宿主机 `/www/HBWeb/remote-maintenance/artifacts`，并为每个文件填入真实的版本、文件名、字节数和 SHA-256；目录以只读方式挂载到容器 `/app/App_Data/RemoteMaintenance/artifacts`。Compose 中的 `REMOTE_MAINTENANCE_RUSTDESK_FILE` 和 `REMOTE_MAINTENANCE_AGENT_FILE` 应填写容器内目录下的文件名（不要填只存在于宿主机的路径）。RustDesk 当前固定值为 `1.4.9`、`rustdesk-1.4.9-x86_64.exe`、`24472432` 字节及本节上方记录的 SHA-256；status agent 必须使用实际发布值，不能留空或使用示例值。
4. **写入两端配置。** 在对应 Compose 使用的受保护 `.env` 中，中心 API 至少提供以下变量；POS API 只提供标记为 POS 的两项。`REMOTE_MAINTENANCE_INTER_API_KEY` 在两端必须是同一随机高熵值，且仅用于中心 API 与 POS API 的服务端请求，绝不能下发到 WPF 或 status agent。

   ```dotenv
   # 中心 API（services/backend/docker-compose.yml）
   REMOTE_MAINTENANCE_ENABLED=false
   REMOTE_MAINTENANCE_PUBLIC_KEY=<RustDesk id_ed25519.pub 的完整公钥行>
   REMOTE_MAINTENANCE_INTER_API_KEY=<仅服务器保存的随机值>
   REMOTE_MAINTENANCE_RUSTDESK_VERSION=1.4.9
   REMOTE_MAINTENANCE_RUSTDESK_FILE=rustdesk-1.4.9-x86_64.exe
   REMOTE_MAINTENANCE_RUSTDESK_SHA256=eaedeb0088e687bf46f7c46a9c6ea5493ce51f3134dfd6acbedb47b5b9136274
   REMOTE_MAINTENANCE_RUSTDESK_SIZE=24472432
   REMOTE_MAINTENANCE_AGENT_VERSION=<status-agent 实际版本>
   REMOTE_MAINTENANCE_AGENT_FILE=<status-agent 实际文件名>
   REMOTE_MAINTENANCE_AGENT_SHA256=<status-agent 实际 SHA-256>
   REMOTE_MAINTENANCE_AGENT_SIZE=<status-agent 实际字节数>

   # POS API（apps/pos-wpf/docker-compose.hotbargain.yml）
   REMOTE_MAINTENANCE_ENABLED=false
   REMOTE_MAINTENANCE_INTER_API_KEY=<与中心 API 完全相同的值>
   ```

   `RemoteMaintenance__IdServer`、`RemoteMaintenance__RelayServer`、`RemoteMaintenance__HeartbeatBaseUrl`、`RemoteMaintenance__ArtifactRootPath` 及默认阈值已由 Compose 明确设置。中心的 `DataProtection__KeysPath=/app/App_Data/DataProtectionKeys` 必须继续挂载原有持久 key ring；POS API 的 `hbpos-api-data-protection-keys` 是独立 ring，不要为了共享 `InterApiKey` 而互换或删除任何 key ring。
5. **先只读检查，确有缺失才迁移并复验。** 两端仍关闭时，用与 API 完全相同的受保护连接配置先执行只读检查：

   ```sh
   dotnet run --project services/backend/BlazorApp.Api -- --schema=remote-maintenance-check
   ```

   只有当该检查明确报告远程维护 schema 缺失时，才执行一次显式 migration，然后重复远程维护检查；连接失败、权限错误或其他异常不得直接迁移：

   ```sh
   dotnet run --project services/backend/BlazorApp.Api -- --schema=remote-maintenance
   dotnet run --project services/backend/BlazorApp.Api -- --schema=remote-maintenance-check
   dotnet run --project services/backend/BlazorApp.Api -- --schema=check
   ```

   最终的远程维护只读检查和全局 `schema=check` 均须成功；若初检已经通过，跳过 migration，继续执行全局检查即可。migration 在主库事务中幂等建立远程维护表和索引。容器化部署也可使用已构建镜像和 Compose 的 `hb-api-migrate` schema profile，但必须确认注入的是同一数据库配置；正常 `hb-api`/`hbpos-api` 启动不会替代显式迁移。
6. **同一窗口打开两端并重建容器。** 仅在 migration（如确有缺失）、复验、公钥匹配及两个 artifact 的路径/大小/SHA-256 均通过后，将中心和 POS API 的 `REMOTE_MAINTENANCE_ENABLED` 同时改为 `true`。`.env` 改动不能使用 `docker compose restart`（它不会重新读取并应用所有环境变量）；必须沿用原部署的 `--env-file`、`-p/--project-name` 等参数，仅将服务重建为：

   ```sh
   docker compose --env-file <原中心部署使用的 env-file> -p <原中心 project> -f services/backend/docker-compose.yml up -d --no-deps --force-recreate hb-api
   docker compose --env-file <原 POS 部署使用的 env-file> -p <原 POS project> -f apps/pos-wpf/docker-compose.hotbargain.yml up -d --no-deps --force-recreate hbpos-api
   ```

   尖括号参数必须替换为已核对的原部署值；原部署未显式使用某参数时才省略该参数，不得自行猜测 env-file 或 project。不要只打开一端：中心未启用会返回 `REMOTE_MAINTENANCE_DISABLED`，中心已开但 POS 未开则客户端仍无法完成代理下载/提交。
7. **健康检查与小范围验收。** 先在各自 Compose project 下检查 `docker compose ps` 中目标服务为 healthy，再核对中心 `http://localhost:5002/api/health` 和 POS API `http://localhost:5003/api/v1/health` 返回成功；随后只用一台已授权 Windows POS 验证 `prepare → artifact 下载 → commit → status agent 心跳`，确认管理员列表状态在 60 秒阈值内变为 `online`。`REMOTE_MAINTENANCE_NOT_READY` 表示配置、schema 或 artifact 未达标，应修复门禁条件而不是反复点击重试。

**回滚：** 任何健康检查、试点或公钥/产物验收失败，先把中心和 POS API 的 `REMOTE_MAINTENANCE_ENABLED` 都改回 `false`，仍须使用原部署的 `--env-file`、project 和两个 `-f` 路径执行 `up -d --no-deps --force-recreate hb-api`、`up -d --no-deps --force-recreate hbpos-api`，再确认旧业务健康。此开关只控制中心/POS API 的远程维护接口、代理下载、提交和心跳，不会卸载或停止已安装的 RustDesk 客户端、status agent Windows 服务，也不会停止 RustDesk server/relay 容器；若要求彻底停用无人值守访问，必须另行执行受控的客户端/Agent 停止或卸载及 RustDesk 服务停用流程。保留新增 schema、中心 DataProtection key ring、artifact 和审计数据，不用回滚或删除它们。若故障只在 RustDesk 服务，按 [`scripts/ops/rustdesk/README.md`](../scripts/ops/rustdesk/README.md) 的精确回滚步骤处理；应用回滚与 RustDesk 容器回滚分开进行。

## 数据库迁移与启动门禁

迁移至少建立：远程设备最新快照/归属表（唯一 `DeviceRegistrationId`）、凭据密文、monitor token hash、加密 commit 操作响应、严格 sequence、时间戳和 schema 版本；凭据密文、token hash、操作响应不得进入列表 DTO。中心主库沿既有正确 db 模式，POS 归属读取既有 POSM 有效登记。

增量 migration 由运维显式执行，应用正常启动只读检查，不隐式创建或修改生产 schema。当前可执行命令为（连接参数从受保护运维环境注入，不写入仓库）：

```sh
dotnet run --project services/backend/BlazorApp.Api -- --schema=remote-maintenance
```

该命令通过既有 schema 程序的独立迁移入口，在主库事务中幂等建立远程维护表和索引；它不会由正常 Web 启动触发。只读门禁为 `dotnet run --project services/backend/BlazorApp.Api -- --schema=remote-maintenance-check`，另外可用全局 `--schema=check` 验证主库基础账本。应用启动或每次 prepare 前都执行只读 readiness 检查；feature 未启用、迁移缺失、配置缺失、文件不存在、大小或 SHA-256 不匹配时 fail closed，并保持旧业务启动。
