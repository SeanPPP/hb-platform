# .vip RustDesk 维护

## 当前部署

- Compose 项目：`rustdesk-server`，服务目录：`/www/rustdesk-server`。
- `rustdesk-server` 运行 `hbbs`，`rustdesk-relay` 运行 `hbbr`。
- 官方镜像固定 `1.1.16` 及 `compose.yml` 中的摘要。
- 进程工作目录为 `/root`，宿主机 `/www/rustdesk-server/data` 必须挂载到该目录。
- TCP：21115、21116、21117；UDP：21116。Web 客户端端口不发布。
- 客户端固定 `1.4.9`，文件 `rustdesk-1.4.9-x86_64.exe`，大小 24472432 字节，SHA-256：`eaedeb0088e687bf46f7c46a9c6ea5493ce51f3134dfd6acbedb47b5b9136274`。

## 免配置客户端

`prepare-client.py` 验证官方原版 SHA-256 后，按 RustDesk 1.4.9 的 `custom_server.rs` 文件名协议封装服务器、relay 和公钥。源文件为原版 Windows x64 EXE，`--public-key` 必须指向现有 `id_ed25519.pub`，禁止使用私钥文件。`--output` 使用新的精确输出目录；已有同名客户端不会被覆盖。

生成的 EXE 保留官方二进制内容及签名，配置仅存于文件名。下载后必须保留完整文件名；改名会丢失该预配置入口。密码与 monitor token 不参与封装。这个下载客户端本身不提供设备状态上报，状态上报由 POS 安装流程部署的独立 Windows 服务完成。

服务端已生成并核对 `/www/HBWeb/remote-maintenance/artifacts/rustdesk-manifest.json` 及其对应 EXE。已验证解码后的服务器、公钥匹配当前运行服务，文件名长度 168，复制后的 SHA-256 与原版一致。实际 Windows 启动配置仍需试点验证。

官方协议依据：<https://github.com/rustdesk/rustdesk/blob/1.4.9/src/custom_server.rs>。

## 本次切换证据

2026-09-06（Brisbane）从旧 `hbbs 1.1.15` 完成切换。已发现并修正旧 Compose 挂载 `/data` 而实际数据库和密钥位于容器 `/root` 的问题。

受保护备份：`/www/backups/rustdesk-migration-20260905T214252Z`（名称采用 UTC）。目录包含原配置、原镜像标识、最终 SQLite 快照和服务器身份；不得上传到 Git 或输出文件内容。

- `sha256-stable.json` 校验可恢复的持久文件，不包含 SQLite 临时 WAL/SHM。
- `hbbs-final` 为停止旧进程后取得并通过 integrity_check/checkpoint 的快照。
- `SERVER_CUTOVER_OK` 标记新进程 TCP/UDP 监听及公钥一致性已验证。
- `addressbook-final` 保存旧通讯录最终数据；`ADDRESSBOOK_RETIRED_OK` 标记两个旧容器已移除。
- 旧通讯录配置保留为 `/www/rustdesk-addressbook/docker-compose.retired.yml`，不会被默认 Compose 自动启动。
- 本次已从外网验证 UDP 21116 RegisterPeer 响应、TCP 21115 NAT 响应，以及 TCP 21116/21117 可达。21118/21119/8088/8089 不再提供对应公网服务。
- 主 API、POS API 和旧业务容器在 RustDesk 切换期间 ID 未改变；主站、主 API、POS API 健康端点返回 200。

以上网络检查不能替代两台 Windows 设备的实际直连、中继与无人值守验收。

## 切换脚本边界

`cutover.py` 只用于这次已有服务的数据挂载修复：要求目标数据目录为空，备份完整且校验通过，当前容器 ID 与备份一致。不能作为重复执行的通用升级脚本。

脚本失败时保留失败数据，并从快照重建原版本。执行前仍需核对目标、备份、Compose 和预期影响范围。正常升级应先制作新的可恢复备份，再使用已验证镜像摘要逐服务更新。

## 回滚

1. 核对备份的 `containers.json` 与 `rollback-compose.json`，确认目标仅包含 RustDesk。保留当前数据及镜像。
2. 停止 `rustdesk-server`、`rustdesk-relay`，将当前数据另存到新的受保护目录。
3. 从 `hbbs-final` 恢复已验证的密钥和 SQLite 快照到持久目录，使用 `rollback-compose.json` 中记录的旧镜像启动 `rustdesk-server`。
4. 如需恢复旧通讯录，使用其原目录内的 `docker-compose.retired.yml` 指定服务启动；保留原数据，不从其他目录误解析相对卷路径。
5. 复核 RustDesk 公钥、进程、监听及业务健康。UFW 新增规则记录在本次变更中，只能按精确规则撤销，不能重置整个防火墙。

HBWeb/POS API/WPF 的应用发布与 RustDesk 容器切换分别验证和回滚；正常应用回退保留新增维护数据表和 DataProtection 密钥。

应用发布前已建立独立备份 `/www/backups/remote-maintenance-apps-20260905T220712Z`，包含主 API/POS 源码与 `.env`、Web 静态站点（含 `.user.ini`）、容器配置和回滚镜像标签。归档可读性及 SHA-256 已验证。该备份建立本身不代表应用已发布；应用切换须另行记录验证证据。
