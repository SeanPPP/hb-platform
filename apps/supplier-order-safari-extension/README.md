# HB Supplier Order Safari Web Extension

这是 `apps/supplier-order-extension` 的 iPhone + iPad Safari Web Extension 宿主项目。项目只包含 iOS App 与 iOS Safari Extension，不提供 macOS target。业务逻辑、内容脚本、助手 UI、供应商配置、鉴权与 API 契约继续由共享扩展源码维护。

## iOS 功能策略

- 最低支持 iOS / iPadOS 16.4，与 `storage.session`、动态内容脚本和 Manifest V3 service worker 的运行要求一致。
- iOS Safari 不支持 `windows.create`；工具栏、`/shop` 页面入口和供应商商品按钮统一打开完整助手页。
- Safari 优先通过 `options_ui` 打开助手；若当前系统拒绝该调用，则复用或创建扩展标签页。
- 助手保留登录、门店、供应商授权、商品采购记录、60/90 天热销 TOP 10% 和中英文切换，并针对触控、安全区和窄屏表格做适配。

## 环境

- Node.js 20+
- iOS / iPadOS 16.4+
- Xcode 26.5（项目由该版本的 `safari-web-extension-packager --ios-only` 生成）

## 测试与构建

```bash
cd apps/supplier-order-safari-extension
npm --prefix ../supplier-order-extension ci
npm test
npm run build
npm run build:xcode
```

`npm test` 会先构建共享扩展并同步 `dist/safari`。`npm run build:xcode` 对通用 iPhone/iPad Simulator 目标执行无签名构建。Xcode Extension 的 `Resources` 是生成快照，不应直接编辑。

## Simulator 运行

先启动一个 iPhone 或 iPad Simulator，然后运行：

```bash
./script/build_and_run.sh
```

如果同时启动了多个 Simulator，可指定设备：

```bash
HB_IOS_SIMULATOR_ID=<simulator-uuid> ./script/build_and_run.sh --verify
```

脚本还支持 `--debug`、`--logs`、`--telemetry` 和 `--verify`。首次安装后，在 Simulator 的“设置 → Safari → 扩展”中启用 `HB Supplier Order`，再按站点授予网页访问权限。

## iPhone / iPad 实机运行

1. 用 Xcode 打开 `xcode/HB Supplier Order Safari/HB Supplier Order Safari.xcodeproj`。
2. 为 App 与 Extension target 选择同一个 Apple 开发团队；不要把个人 Team ID 或描述文件提交到 Git。
3. 选择已配对且开启开发者模式的 iPhone 或 iPad，运行 `HB Supplier Order Safari` scheme。
4. 在设备的“设置 → Safari → 扩展”中启用扩展并授予站点权限。
5. 在 Safari 扩展菜单、Hot Bargain `/shop` 页面或供应商商品按钮中打开助手。

## 发布边界

当前项目只完成开发与测试配置，不自动归档、上传 TestFlight 或提交 App Store。正式发布前仍需配置 App Store Connect、签名、隐私信息和商店素材；不得把账号凭据或证书提交到仓库。
