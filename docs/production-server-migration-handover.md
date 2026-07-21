# hotbargain.vip 生产服务器迁移交接文档

## 1. 文档目的

本文用于迁移 `hotbargain.vip` 当前生产服务器，覆盖：

- Web 前端静态站点
- 主后端 `hb-platform-vite-api`（端口 `5002`）
- 收银后端 `hbpos-api`（端口 `5003`）
- Nginx、TLS、Docker network、运行日志
- 数据库连接、第三方服务密钥和 DataProtection key ring
- 上线验证、考勤二维码验收与回滚

本文只记录路径和变量名，不保存任何密钥、密码、token 或数据库连接串值。

## 2. 当前生产拓扑

| 组件 | 宿主机位置 | 运行单元 | 内部端口 | 公网入口 |
| --- | --- | --- | --- | --- |
| Web 前端 | `/www/wwwroot/www.hotbargain.vip` | Nginx 静态文件 | 无 | `https://hotbargain.vip/` |
| 主后端 | `/www/HBWeb/BackEnd/master-Vite` | `hb-platform-vite-api` | `5002` | `/api/` |
| 收银后端 | `/www/HBWeb/BackEnd/hbpos-api` | `hbpos-api` | `5003` | `/pos-api/` |
| 旧前端容器 | Docker `hb-platform-frontend` | 遗留服务 | `8080` | 只保留，不作为新站主入口 |
| 旧后端 | Docker `hb-platform-api` | 遗留服务 | `5001` | 迁移时不要误当主后端 |

生产 Nginx 配置：

```text
/www/server/panel/vhost/nginx/www.hotbargain.vip.conf
```

关键代理关系：

```text
/             -> /www/wwwroot/www.hotbargain.vip
/api/         -> 127.0.0.1:5002
/pos-api/     -> 127.0.0.1:5003
```

## 3. 必须迁移的文件与目录

### 3.1 前端

```text
/www/wwwroot/www.hotbargain.vip/
/www/wwwroot/www.hotbargain.vip/.user.ini
/www/server/panel/vhost/nginx/www.hotbargain.vip.conf
```

前端静态文件也可以从固定 Git 提交重新构建，但 `.user.ini` 和生产 Nginx 配置必须单独保留。

### 3.2 主后端

```text
/www/HBWeb/BackEnd/master-Vite/.env
/www/HBWeb/BackEnd/master-Vite/docker-compose.yml
/www/HBWeb/BackEnd/master-Vite/data-protection-keys/
```

主后端源码建议从已确认的 Git commit 重新同步和构建，不要把服务器上的 `bin/`、`obj/`、`node_modules/` 当成迁移资产。

### 3.3 收银后端

```text
/www/HBWeb/BackEnd/hbpos-api/.env
/www/HBWeb/BackEnd/hbpos-api/apps/pos-wpf/docker-compose.hotbargain.yml
```

收银后端必须复用主后端的 DataProtection key ring，不应建立独立 key ring：

```text
/www/HBWeb/BackEnd/master-Vite/data-protection-keys
```

### 3.4 TLS 证书

当前 TLS 由宿主机 Nginx 使用，证书目录位于：

```text
/www/server/panel/vhost/cert/www.hotbargain.vip/
```

迁移方式二选一：

1. 使用加密通道迁移现有 `fullchain.pem` 和对应私钥，并严格保留权限。
2. 在新服务器重新签发证书，确认成功后再切换 DNS。

TLS 私钥不得复制进 Git、Docker 镜像、前端目录或应用容器。

## 4. 必须共享的 DataProtection key ring

主后端和收银后端必须挂载同一个宿主机目录：

```text
宿主机：/www/HBWeb/BackEnd/master-Vite/data-protection-keys
容器内：/app/App_Data/DataProtectionKeys
```

主后端挂载：

```yaml
volumes:
  - ./data-protection-keys:/app/App_Data/DataProtectionKeys
```

收银后端 `.env`：

```dotenv
DATA_PROTECTION_KEYS_HOST_PATH=/www/HBWeb/BackEnd/master-Vite/data-protection-keys
```

收银后端挂载：

```yaml
volumes:
  - ${DATA_PROTECTION_KEYS_HOST_PATH}:/app/App_Data/DataProtectionKeys
```

迁移要求：

- 必须复制完整 key ring，不能只复制最新文件。
- 保留文件名、内容、时间和访问权限。
- 两个容器都必须能够读取；需要生成新 key 时还必须能够写入。
- 不得在新服务器启动 API 后才补 key ring，否则容器可能先生成一套不兼容的新密钥。
- 不得删除旧 key。旧 key 仍用于解密数据库中的历史密文和已登记的考勤二维码签名密钥。

如果 key ring 丢失或两套 API 挂载不同目录，会出现：

```text
ATTENDANCE_QR_KEY_DECRYPT_FAILED
```

## 5. 环境变量与敏感配置清单

`.env` 文件只放在服务器上，建议权限为 `0600`。Docker Compose 使用 `--env-file .env` 注入变量，不需要把 `.env` 挂载到容器内。

### 5.1 主后端 `.env`

数据库：

```text
CONNECTION_STRING_DEFAULT
CONNECTION_STRING_STORE_HQ
CONNECTION_STRING_SALES
CONNECTION_STRING_POSTGRES
CONNECTION_STRING_POSM
CONNECTION_STRING_SALES_RECORD
```

认证与日志：

```text
JWT_KEY
JWT_ISSUER
JWT_AUDIENCE
CENTER_LOG_HBWEB_RV_KEY_SHA256
CENTER_LOG_HBPOS_API_KEY_SHA256
```

EAS webhook：

```text
EAS_WEBHOOK_SECRET
EAS_WEBHOOK_ALLOWED_ACCOUNT_NAME
EAS_WEBHOOK_ALLOWED_PROJECT_NAME
```

第三方服务：

```text
DEEPSEEK_API_KEY
TENCENT_SECRET_ID
TENCENT_SECRET_KEY
TENCENT_BUCKET_NAME
TENCENT_REGION
TENCENT_IMAGE_BUCKET_NAME
TENCENT_IMAGE_REGION
```

### 5.2 收银后端 `.env`

```text
CONNECTION_STRING_DEFAULT
CONNECTION_STRING_POSM
CENTER_LOG_HBPOS_API_KEY
DATA_PROTECTION_KEYS_HOST_PATH
LINKLY_CLOUD_PRODUCTION_NOTIFICATION_BEARER
LINKLY_CLOUD_SANDBOX_NOTIFICATION_BEARER
LINKLY_CLOUD_PRODUCTION_POS_VENDOR_ID
LINKLY_CLOUD_SANDBOX_POS_VENDOR_ID
SQUARE_WEBHOOK_SIGNATURE_KEY_PRODUCTION
SQUARE_WEBHOOK_SIGNATURE_KEY_SANDBOX
```

### 5.3 前端构建密钥

`VITE_CENTER_LOG_KEY` 属于前端构建期变量。它会在构建时进入产物，不是容器运行时挂载文件。必须通过受控 CI/CD 环境注入，不要写入仓库或迁移文档。

## 6. 可以重新创建的资源

以下内容通常不需要从旧服务器逐文件复制：

- Docker 镜像：在新服务器从固定源码重新构建。
- `bin/`、`obj/`、`node_modules/`：重新生成。
- 前端 `dist/`：可以从固定 commit 重新构建。
- Docker network `hb-network`：在新服务器重新创建。
- 健康检查和普通运行缓存：容器启动后重新生成。

日志卷可以按审计要求归档，但不应作为启动新服务的前置依赖：

```text
hb-api-logs
hbpos-api-logs
```

## 7. 数据库迁移边界

先确认迁移属于哪一种：

### 7.1 只迁移应用服务器

如果 SQL Server、PostgreSQL 和对象存储仍使用原服务：

- 不执行数据库恢复。
- 保持连接串不变。
- 更新数据库防火墙、IP allowlist 或 VPN 路由，允许新服务器访问。
- 在切换 DNS 前测试所有数据库连接。

### 7.2 同时迁移数据库

数据库迁移必须有单独的 DBA 方案，至少包含：

- 一致性备份和恢复点
- 停写窗口或增量同步
- 登录、用户、权限和证书迁移
- SQL Agent/定时任务迁移
- 回滚点和数据校验

禁止只复制数据库文件后直接启动生产 API。

## 8. 新服务器准备

建议先完成：

1. 安装 Docker Engine、Docker Compose、Nginx、`curl`、`rsync`。
2. 创建与旧服务器一致的部署目录。
3. 创建 Docker network：

```bash
sudo docker network inspect hb-network >/dev/null 2>&1 \
  || sudo docker network create hb-network
```

4. 开放公网 `80/443`。
5. `5002/5003` 只供本机 Nginx 代理；迁移验证完成后通过防火墙限制公网直连。
6. 确认新服务器可以访问数据库、腾讯云、EAS、SMTP、Linkly 和 Square。
7. 迁移前 24-48 小时降低 DNS TTL。

## 9. 推荐迁移顺序

### 9.1 冻结发布源

记录：

```bash
git rev-parse HEAD
git status --short --branch
```

生产迁移必须使用明确 commit。不要从包含未提交业务修改的工作树直接迁移。

### 9.2 备份旧服务器

备份至少包含：

```text
前端 webroot 与 .user.ini
两个生产 .env
完整 DataProtection key ring
Nginx vhost 配置
TLS 证书与私钥，或重新签发所需资料
Docker Compose 文件
必要的日志归档
```

敏感备份必须加密传输和加密保存，不得写入公开对象存储或普通聊天附件。

### 9.3 恢复目录与敏感文件

先恢复 DataProtection key ring 和 `.env`，再启动容器：

```bash
sudo install -d /www/HBWeb/BackEnd/master-Vite/data-protection-keys
sudo chmod 600 /www/HBWeb/BackEnd/master-Vite/.env
sudo chmod 600 /www/HBWeb/BackEnd/hbpos-api/.env
```

DataProtection 目录的 owner/mode 应从旧服务器原样保留，并验证两个容器都可读写。

### 9.4 部署主后端

```bash
cd /www/HBWeb/BackEnd/master-Vite
sudo docker compose --env-file .env up --build -d hb-api
```

等待：

```bash
sudo docker inspect hb-platform-vite-api --format '{{.State.Health.Status}}'
curl -fsS http://127.0.0.1:5002/api/health
```

### 9.5 部署收银后端

确认 `.env` 中的共享路径：

```dotenv
DATA_PROTECTION_KEYS_HOST_PATH=/www/HBWeb/BackEnd/master-Vite/data-protection-keys
```

启动：

```bash
cd /www/HBWeb/BackEnd/hbpos-api
sudo docker compose --env-file .env \
  -f apps/pos-wpf/docker-compose.hotbargain.yml \
  up --build -d hbpos-api
```

等待：

```bash
sudo docker inspect hbpos-api --format '{{.State.Health.Status}}'
curl -fsS http://127.0.0.1:5003/api/v1/health
```

验证两个容器挂载源一致：

```bash
sudo docker inspect hb-platform-vite-api \
  --format '{{range .Mounts}}{{println .Source "->" .Destination}}{{end}}'
sudo docker inspect hbpos-api \
  --format '{{range .Mounts}}{{println .Source "->" .Destination}}{{end}}'
```

### 9.6 部署前端与 Nginx

1. 恢复或重新构建前端静态文件。
2. 保留 `.user.ini`。
3. 恢复 Nginx vhost。
4. 安全迁移或重新签发 TLS 证书。
5. 运行：

```bash
sudo nginx -t
sudo systemctl reload nginx
```

### 9.7 DNS 切换前验证

使用新服务器 IP 验证域名和证书，不修改本机全局 hosts：

```bash
curl --resolve hotbargain.vip:443:NEW_SERVER_IP \
  https://hotbargain.vip/api/health
curl --resolve hotbargain.vip:443:NEW_SERVER_IP \
  https://hotbargain.vip/pos-api/api/v1/health
```

确认通过后再修改 DNS A/AAAA 记录。

## 10. 强制验收清单

基础服务：

- [ ] `https://hotbargain.vip/` 返回 `200`
- [ ] `https://hotbargain.vip/login` 返回 `200`
- [ ] `https://hotbargain.vip/api/health` 返回 `200`
- [ ] `https://hotbargain.vip/pos-api/api/v1/health` 返回 `200`
- [ ] `hb-platform-vite-api` 为 `healthy`
- [ ] `hbpos-api` 为 `healthy`
- [ ] Nginx 配置检查通过
- [ ] TLS 证书链和域名正确

挂载与密钥：

- [ ] 主后端和收银后端挂载同一个 DataProtection 宿主机目录
- [ ] 容器内路径都是 `/app/App_Data/DataProtectionKeys`
- [ ] 旧 DataProtection key 文件完整保留
- [ ] 两个 `.env` 权限正确且变量名齐全
- [ ] 日志中没有密钥、连接串或 token 泄漏

业务功能：

- [ ] Web 登录成功
- [ ] 移动端登录和 API 请求成功
- [ ] POS 设备认证成功
- [ ] Linkly/Square 回调可达
- [ ] EAS webhook 验签成功
- [ ] 图片/对象存储访问成功

考勤二维码：

- [ ] POS 生成 `HBATE1` 五段二维码
- [ ] 移动端扫描后 resolve 成功
- [ ] 不出现 `ATTENDANCE_QR_KEY_DECRYPT_FAILED`
- [ ] 有效二维码进入定位/打卡流程
- [ ] 真实过期二维码显示“二维码已过期”
- [ ] POS 与主后端 UTC 时间差在允许范围内

## 11. 切换与回滚

### 11.1 切换原则

- 不要长时间让新旧服务器同时执行后台任务或 webhook 消费。
- DNS 传播期间监控两台服务器请求量、错误率和数据库连接数。
- 旧服务器至少保留到 DNS TTL 完全过期并完成业务验收。
- 切换窗口内不要删除旧服务器、旧容器或旧 DataProtection key ring。

### 11.2 回滚触发条件

出现以下任一情况应考虑立即回滚：

- 主后端或收银后端持续 unhealthy
- 数据库连接失败或出现数据一致性风险
- 登录、支付、订单或考勤主链路不可用
- DataProtection 解密失败
- TLS、Nginx 路由或 webhook 大面积失败

### 11.3 回滚步骤

1. 停止新服务器对外写入，避免双写扩大。
2. 将 DNS 恢复为旧服务器地址。
3. 确认旧服务器主后端、收银后端和 Nginx 正常。
4. 验证旧服务器健康端点和考勤二维码。
5. 保留新服务器日志、容器状态和配置快照用于分析。
6. 禁止用新服务器生成的临时 DataProtection key 覆盖旧 key ring。

## 12. 迁移交接记录模板

```text
迁移日期：
负责人：
旧服务器：
新服务器：
DNS TTL：
发布 Git commit：
前端构建版本：
主后端镜像/容器：
收银后端镜像/容器：
数据库是否迁移：
DataProtection key 文件数量：
TLS 证书到期时间：
备份位置（不得记录密码）：
切换开始时间：
切换完成时间：
验收结果：
回滚截止时间：
遗留事项：
```
