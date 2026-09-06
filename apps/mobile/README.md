# HB Expo 移动端

本目录是 HB Platform 的 Expo / React Native 移动端项目。

## EAS APK Webhook 与 App 下载二维码

后台的“App 下载”页面会展示当前 Android APK 下载二维码。EAS Webhook 用于在每次 Expo EAS Android APK 构建完成后，把新的 APK 下载地址写入后端，并标记为 COS 镜像待处理；后台服务再异步下载 EAS artifact、上传到腾讯云 COS，并把镜像状态展示到 Web 页面。

### 创建 EAS Webhook

在 `apps/mobile` 目录下使用 EAS CLI 创建 `BUILD` 事件 webhook：

```bash
eas webhook:create --event BUILD --url https://<backend-domain>/api/mobile-app-builds/eas-webhook --secret <secret>
```

注意事项：

- `<backend-domain>` 必须替换为后端公网域名占位对应的实际部署地址，文档和示例不要提交真实生产域名。
- `<secret>` 必须使用部署环境中的私密值，不能提交到 Git、README、脚本或前端产物。
- 当前 `eas.json` 的 `preview` 和 `production` profile 都配置为 Android APK 构建，适合用于二维码下载页。

### 受控 Mobile OTA 发布

Mobile OTA 的 Expo 发布事实与投放策略相互独立。发布脚本只创建平台独立、不可复用的 release channel，并登记不可变事实；它不会启用策略，也不会修改当前目标。

```bash
HBWEB_API_BASE_URL=https://<backend-domain> \
HBWEB_API_TOKEN=<hbsvc-manage-app-downloads-token> \
node scripts/publish-ota-update.mjs \
  --environment preview \
  --platform all \
  --runtime-version 1.0.4 \
  --message "验证 Mobile OTA"
```

`--platform all` 会使用同一个 `ReleaseBatchId` 顺序执行 Android、iOS 两次独立发布。脚本在任何 EAS 写入前先对两个平台调用 `/api/app-ota-releases/preflight`，再用固定 EAS CLI 穷尽 `channel:list` 分页并证明所有目标 channel 在 Expo 侧尚不存在；网络失败、失形或无法穷尽时一律停止。随后才发布到以下唯一 channel：

- `mobile-production-android-release-*`
- `mobile-production-ios-release-*`
- `mobile-preview-android-release-*`
- `mobile-preview-ios-release-*`

固定版本的 `eas update --json` 返回后，脚本先严格验证 platform、Runtime、branch、Update Group ID 和 Update ID，再用同版本 `eas channel:view <releaseChannel> --json --non-interactive` 权威回读 active channel、单一 branch mapping 及最新 group 的完整发布身份，最后才 POST `/api/app-ota-releases/register`。一端失败不会抹掉另一端的完整成功结果，但整批命令会返回非零。

回滚必须按平台单独执行，并把同 lane 的已登记来源 UUID 交给发布前 preflight；单个来源不能和 `--platform all` 混用：

```bash
node scripts/publish-ota-update.mjs \
  --environment production \
  --platform ios \
  --runtime-version 1.0.4 \
  --message "iOS 回滚重发" \
  --rollback-of-release-id <source-release-uuid>
```

管理员 JWT 不得放入环境变量，只能从 stdin 读取：

```bash
printf '%s' "$TEMP_ADMIN_JWT" | node scripts/publish-ota-update.mjs \
  --access-token-stdin \
  --environment production \
  --platform ios \
  --runtime-version 1.0.4 \
  --message "iOS 修复"
```

环境变量 `HBWEB_API_TOKEN` 只接受具备 `System.ManageAppDownloads` 的 `hbsvc_` 服务 token。所有后台凭据都会从 EAS 子进程环境和 recovery 文件中剔除。

如果 EAS 已成功但后台登记失败，脚本会写入权限为 `0600` 的 `.artifacts/mobile-ota-recovery/<batch-id>.json`。此时禁止重新发布。补登记仍会用固定 EAS CLI 逐条重做只读 `channel:view`，并严格核对 channel、branch、update/group、Runtime、platform、message、commit、Dashboard URL 和发布时间；权威回读不一致时不会调用后台登记：

```bash
node scripts/publish-ota-update.mjs --register-only .artifacts/mobile-ota-recovery/<batch-id>.json
```

旧客户端取得受控 coordinator 前，需要在迁移窗口对旧 `production` / `preview` 固定 channel 做最后一次 bootstrap。该模式必须显式指定且一次只允许一个平台；Android、iOS 必须分别执行：

```bash
HBWEB_API_BASE_URL=https://<backend-domain> \
HBWEB_API_TOKEN=<hbsvc-manage-app-downloads-token> \
node scripts/publish-ota-update.mjs \
  --bootstrap-legacy-fixed-channel \
  --environment preview \
  --platform ios \
  --runtime-version <legacy-runtime-version> \
  --message "安装受控 Mobile OTA coordinator"
```

bootstrap 会先向新 preflight 接口发送 `bootstrapLegacyFixedChannel: true`；服务端 `EasWebhook:AllowLegacyOtaBootstrapRegistration` 开关默认关闭，必须只在受控迁移窗口临时开启。preflight 全部通过后，脚本才会用固定 EAS CLI 发布到与环境同名的 fixed channel/branch。该模式不会执行普通 release channel 的 unused 检查，因为 fixed channel 必然已经存在；发布后仍会用 `channel:view` 严格核对当前最新 update 的 platform、Runtime、Update/Group ID、message、commit、Dashboard URL 和发布时间。

验证通过后，bootstrap 只 POST 旧 `/api/mobile-app-builds/ota-updates`，请求携带 `bootstrapLegacyFixedChannel: true`；它不会写 `AppOtaRelease`、不会选择目标，也不会启用或修改任何策略。登记失败与普通发布一样写入权限为 `0600` 的无凭据 recovery manifest；使用同一个 `--register-only` 命令补登记时，会先重新执行只读 `channel:view`，身份漂移时拒绝登记，绝不自动重新发布。

bootstrap 完成并验证客户端采用后，应立即关闭后端临时开关并冻结旧 fixed channel。后续发布只能使用上面的平台独立 release channel 流程。

### 后端配置项

后端 webhook 接口读取以下配置：

| 配置项 | 说明 |
| --- | --- |
| `EasWebhook:Secret` | 用于校验 `expo-signature` 的 webhook secret，必须和 `eas webhook:create --secret <secret>` 使用同一个值。 |
| `EasWebhook:AllowedAccountName` | 允许写入 APK 下载地址的 Expo account 名称；用于避免其他账号的构建误写入。 |
| `EasWebhook:AllowedProjectName` | 允许写入 APK 下载地址的 Expo project 名称；用于避免同账号其他项目误写入。 |
| `EasWebhook:AcceptedProfiles` | 允许同步到“App 下载”页的 EAS build profile，默认 `["preview", "production"]`。 |

示例配置只保留占位符：

```json
{
  "EasWebhook": {
    "Secret": "<secret>",
    "AllowedAccountName": "<expo-account>",
    "AllowedProjectName": "<expo-project>",
    "AcceptedProfiles": ["preview", "production"]
  }
}
```

部署到容器或服务器环境时，使用 ASP.NET Core 的双下划线环境变量写法注入，不要把真实值提交到仓库：

```bash
EasWebhook__Secret=<secret>
EasWebhook__AllowedAccountName=<expo-account>
EasWebhook__AllowedProjectName=<expo-project>
EasWebhook__AcceptedProfiles__0=preview
EasWebhook__AcceptedProfiles__1=production
```

服务启动时会兜底检查并创建 `MobileAppBuild` / `MobileAppOtaUpdate` 基础表、索引和缺失的 COS 镜像字段。手动迁移 SQL 仍保留在 `services/backend/BlazorApp.Api/Data/Migrations/`，用于发布前审查或紧急手工补库。

### 本地 mock 验证

本地验证的目标是确认后端可以完成签名校验、解析 EAS BUILD payload、保存最新 APK 地址、排队 COS 镜像，并通过最新版本接口读回。

1. 准备一份 Expo `BUILD` 事件 payload，至少包含后端解析所需的账号、项目、profile、平台、构建状态和 APK 下载地址字段。字段值使用测试占位内容，不要使用真实生产下载地址。
2. 使用与后端 `EasWebhook:Secret` 相同的 `<secret>` 对原始 JSON body 计算 HMAC-SHA1，生成请求头：

```text
expo-signature: sha1=<hex>
```

3. 将原始 payload POST 到后端 webhook 接口：

```bash
curl -X POST "http://localhost:5002/api/mobile-app-builds/eas-webhook" \
  -H "Content-Type: application/json" \
  -H "expo-signature: sha1=<hex>" \
  --data-binary @eas-build-payload.json
```

4. 调用最新 APK 信息接口确认写入结果：

```bash
curl "http://localhost:5002/api/mobile-app-builds/android-latest?profile=production"
```

验证通过的判断标准：

- webhook POST 返回成功状态。
- `/api/mobile-app-builds/android-latest?profile=production` 返回的 profile 和 APK 下载地址与 mock payload 一致；COS 未完成时会临时使用未过期的 EAS artifact，COS 成功后优先返回 COS 地址。
- 不匹配的 secret、非 Android 构建、非成功构建、非允许账号/项目/profile 的 payload 不应更新“App 下载”页。
