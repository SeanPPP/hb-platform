# HB POS iPad

`apps/pos-ipad` 是独立的 iPad 收银应用，不复用 `apps/mobile` 的 Bundle ID、EAS Project 或审核配置。

## 本地启动

```bash
npm install
npm run test
npm run prebuild:ios
npm run ios
```

原生打印、SQLCipher 和外接客显必须使用 Development Build，Expo Go 不在支持范围内。

API 地址通过 `EXPO_PUBLIC_HBPOS_API_URL` 注入；可切换的备用 origin 必须在签名构建时通过
`EXPO_PUBLIC_HBPOS_TRUSTED_API_ORIGINS` 明确列出。设备注册成功后仍由 Hbpos.Api 的设备审批和门店权限控制，
持久化设置不能把设备或收银员凭据发送边界扩张到构建白名单之外。

银行卡终端必须通过 `EXPO_PUBLIC_HBPOS_CARD_PROVIDER=square|linkly`
显式选择。Square 与 Linkly 的公开配置可以同时保留，用于恢复切换前已经耐久化的
支付 attempt；未选择 provider 时禁止发起新的银行卡交易。token、密钥和 provider
交易引用不得写入 Expo public extra。

## App Store 与 OTA 更新

原生版本策略与 OTA 策略分别检查、缓存和失败回退。生产构建必须显式提供独立
iPad EAS 项目，缺少以下任一配置都会在解析 Expo production config 时终止：

```bash
EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID=<dedicated-project-uuid>
EXPO_PUBLIC_HBPOS_UPDATES_URL=https://u.expo.dev/<dedicated-project-uuid>
```

启动自动检查固定为 `NEVER`；应用只在后台门店策略命中后覆盖受信 channel，
并在下载前后核验 `runtimeVersion` 和 iOS update ID。由于 Expo 的 request
header override 会持久化，Updates 启用时冷启动会先 best-effort 清除旧 channel，
再等待本次可信门店策略。正式发布必须为每个 release 使用独立的
`pos-ipad-release-*` channel；`pos-ipad-production` 只作为原生
bootstrap header，不承载发布内容，也不得复用 `apps/mobile` 的 `production`
channel。中央后台会拒绝重复登记已被其他 release 使用的 channel。

发布并登记 release 使用专用脚本：

```bash
HBPOS_OTA_CENTER_BASE_URL=https://<center-host> \
HBPOS_OTA_CENTER_ACCESS_TOKEN=<administrator-access-token> \
EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID=<dedicated-project-uuid> \
EXPO_PUBLIC_HBPOS_UPDATES_URL=https://u.expo.dev/<dedicated-project-uuid> \
npm run ota:publish -- \
  --runtime-version <runtime-version> \
  --release-channel pos-ipad-release-<unique-release-key> \
  --message "<release-message>"
```

正式流程固定为：使用管理员 JWT 调用
`POST /api/pos-ipad/ota-releases/preflight`，通过后执行
`eas channel:create <channel> --json --non-interactive`，再执行 iOS
`eas update --json`，最后只登记 `/api/pos-ipad/ota-releases`。脚本不会创建或
激活 Center rollout。EAS channel 已存在或创建失败时会在发布内容前终止，也不会
自动删除 channel；因此每次重试发布必须换用全新的 release channel。
脚本与 `eas.json` 同时锁定 EAS CLI `21.3.0`，升级 CLI 前必须先复跑发布顺序和
重复 channel 的失败测试。

可先用 `--dry-run`，并用 `--mock-output-file <path>` 验证已保存的 EAS JSON；
它只打印预期的 channel 创建命令、update 命令和登记 payload，全程不调用 Center
或 EAS。正式流程需要具备 `System.ManageAppDownloads` 权限的三段 base64url
管理员 JWT，也可通过 `--access-token <token>` 显式传入；只读 `hbsvc_` service
token 或非 JWT token 会在任何网络/EAS 写入前被拒绝，管理员 token 也不会进入
EAS 子进程或命令日志。

如果 EAS update 已成功但 Center 登记失败，脚本会打印可重试登记 payload。
此时不得重新运行发布命令；应保留已经创建的 channel，并使用打印的 payload
单独重试管理员登记接口。
`branch` 与 channel 是不同概念，脚本不会把 EAS JSON 的 `branch` 当作登记 channel；
命令、登记 payload 及 JSON 中若存在的显式 channel 必须一致。
