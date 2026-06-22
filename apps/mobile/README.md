# HB Expo 移动端

本目录是 HB Platform 的 Expo / React Native 移动端项目。

## EAS APK Webhook 与 App 下载二维码

后台的“App 下载”页面会展示当前 Android APK 下载二维码。EAS Webhook 用于在每次 Expo EAS Android APK 构建完成后，把新的 APK 下载地址同步给后端，再由后端的“App 下载”接口提供给 Web 页面生成二维码。

### 创建 EAS Webhook

在 `apps/mobile` 目录下使用 EAS CLI 创建 `BUILD` 事件 webhook：

```bash
eas webhook:create --event BUILD --url https://<backend-domain>/api/mobile-app-builds/eas-webhook --secret <secret>
```

注意事项：

- `<backend-domain>` 必须替换为后端公网域名占位对应的实际部署地址，文档和示例不要提交真实生产域名。
- `<secret>` 必须使用部署环境中的私密值，不能提交到 Git、README、脚本或前端产物。
- 当前 `eas.json` 的 `preview` 和 `production` profile 都配置为 Android APK 构建，适合用于二维码下载页。

### 给旧 APK 发布 OTA 下载提醒

新增原生依赖或安装权限后，旧 APK 不能通过 OTA 获得新原生能力。旧 runtime 只能发布兼容 OTA，提醒用户下载并重新安装新版 APK。当前项目约定：

- 新 APK 使用 `runtimeVersion=1.0.2`，并启用 App 内后台下载和打开安装器。
- 旧 APK 过渡提醒使用 `runtimeVersion=1.0.1`，并关闭原生安装器，只用系统浏览器打开后端稳定下载入口，由后端实时跳转到最新未过期 APK。

在 `apps/mobile` 目录下发布旧 APK 过渡 OTA：

```bash
npm run ota:legacy-apk-notice:preview
npm run ota:legacy-apk-notice:production
```

这两个 npm script 会通过 `scripts/publish-ota-update.mjs` 调用 `npx eas-cli@latest update --platform android`，并固定注入以下 OTA 环境变量：

```bash
EXPO_PUBLIC_APP_BUILD_PROFILE=<preview|production>
EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED=false
EXPO_PUBLIC_RUNTIME_VERSION=1.0.1
```

只有通过这个新脚本发布 OTA，脚本才会在 EAS 发布成功后解析输出并尝试登记 OTA 数据库记录。通过 Expo 控制台发布，或直接裸跑 `eas update` / `npx eas-cli@latest update`，不会自动入库。

自动登记需要在发布命令所在 shell 中配置后台私密环境变量：

```bash
HBWEB_API_BASE_URL=https://<backend-domain>
HBWEB_API_TOKEN=<backend-bearer-token>
```

`HBWEB_API_BASE_URL` 可以是站点根地址，也可以是带 `/api` 的 API base URL。脚本会 POST 到 `/api/mobile-app-builds/ota-updates`，请求头使用 `Authorization: Bearer <token>`。如果未配置 base URL/token，或后台登记失败，OTA 发布结果不会被回滚；脚本会输出 warning 和可手动补录的 JSON。

发布前需确认后端已通过 EAS Webhook 收到对应 profile 的最新 APK 记录，否则旧 APK 不会弹出下载提示。

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

数据库需先执行 `services/backend/BlazorApp.Api/Data/Migrations/20260615_CreateMobileAppBuild.sql`，确认 `MobileAppBuild` 表和两个 `IX_MobileAppBuild_*` 索引存在后，后台 App 下载页才会从空态或最新构建记录开始正常展示。

### 本地 mock 验证

本地验证的目标是确认后端可以完成签名校验、解析 EAS BUILD payload、保存最新 APK 地址，并通过最新版本接口读回。

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
curl "http://localhost:5002/api/mobile-app-builds/latest"
```

验证通过的判断标准：

- webhook POST 返回成功状态。
- `/api/mobile-app-builds/latest` 返回的 profile、项目和 APK 下载地址与 mock payload 一致。
- 不匹配的 secret、非 Android 构建、非成功构建、非允许账号/项目/profile 的 payload 不应更新“App 下载”页。
