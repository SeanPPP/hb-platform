# HB POS iPad Unlisted 发布与门店回退 Runbook

本 Runbook 只适用于独立应用 `HB POS`（Bundle ID
`com.hbweb.posipad`）。它不得复用 `apps/mobile` 的 EAS Project、App Store
记录、Bundle ID、审核模式或签名资料。

## 1. 发布前冻结条件

只有以下条件全部有可追溯证据时，才允许创建 Production Build：

- Hbpos.Api 与管理端的向后兼容版本已部署，WPF 回归测试通过。
- 仅试点 iPad 的设备注册记录已审批并启用；注册成功的启用设备即可交易。
- `PosIpad.MinimumSupportedVersion`、`LatestVersion` 与 `ForceUpdate` 已按本次
  发布计划配置并验证。
- `npm run test`、`npm run export:ios`、iPad Simulator E2E 和 iOS 原生构建
  全部通过。
- 真机矩阵已完成：HID 500 次扫码、打印/切纸/钱箱 50 次、外屏 30 次热插拔
  与 60 分钟连续显示、Square/Linkly 故障恢复、离线现金杀进程和重复补传。
- 所有 `Unknown` 支付均已人工恢复到终态；不存在未决分期支付、退款或待处理
  本地交易。
- App Store Connect 中的 App、EAS Project、证书和 Provisioning Profile 均是
  `com.hbweb.posipad` 专用资源。

不得把单元测试、模拟 provider、Simulator 或 Expo Go 结果当成上述真机证据。

## 2. Development、Preview 与 Production

1. Development Build：只用于开发机和原生模块调试。
2. Preview Build：用于门店设备注册、sandbox 支付和完整真机矩阵。
3. Production Build：仅在发布冻结条件满足后创建，并上传 TestFlight。

原生模块、Expo SDK 或 native dependency 变化时必须提升应用版本，从而提升当前
基于 `appVersion` 的 `runtimeVersion`，再创建新的二进制。只有与当前
`runtimeVersion` 兼容的 JavaScript/资源修复才能通过 EAS Update 发布。

构建和提交必须由持有对应 Apple/EAS 权限的发布人员执行；凭据不得写入仓库、日志
或工单：

```bash
npm --prefix apps/pos-ipad run test
npm --prefix apps/pos-ipad run export:ios
npx eas-cli build --platform ios --profile preview
npx eas-cli build --platform ios --profile production
npx eas-cli submit --platform ios --profile production
```

## 3. TestFlight 试点范围

- 只审批试点门店的 iPad 设备；每台 iPad 与 WPF 必须使用独立设备记录。
- 注册成功且设备记录启用后即可交易；注册验证、目录下载和只读页面应先在 Preview
  Build 与 sandbox 支付环境完成，再审批 Production 试点设备。
- 至少运行 5 个连续营业日；每日核对后台订单数、销售/退款金额、支付 provider
  状态、本地待同步数、审计补传和日结。
- 按功能矩阵核对试点角色权限；支持导出必须同时授予既有
  `Permissions.PosTerminal.History.View` 与
  `Permissions.PosTerminal.Audit.View`，手动补传另需
  `Permissions.PosTerminal.System.Sync`。
- WPF 保持可运行且不迁移或删除其设备记录、配置和本地数据。
- 试点中不得清理 iPad SQLCipher 数据、Keychain、支付恢复证据或支持导出。

## 4. 回退步骤

发现重复扣款风险、账本不一致、无法恢复的 `Unknown`、持续 403、目录损坏或原生
硬件阻断时：

1. 全局事故需要立即阻止新交易时，将 `LatestVersion` 设为安全目标版本并启用
   `PosIpad.ForceUpdate=true`，确认更新策略对旧版本返回 `forceUpdate=true`。
   单店事故不再有 `EnabledStoreCodes` 门禁，应立即停止该店 iPad 新收银并由负责人
   封存设备；未完成支付恢复和补传前不得禁用设备注册记录。
2. 保持设备验证、支付状态恢复、订单/审计补传和支持导出接口可用。
3. 在 iPad 上停止新收银，恢复每一笔 `Unknown`，并补传或导出所有待处理记录。
4. 按 `OrderGuid`、provider ID 和金额逐笔与 Hbpos.Api、Square/Linkly 对账。
5. 确认待处理数为零或已形成经主管签字的异常清单后，切回该门店的 WPF 设备。
6. 不卸载 App、不清除 Keychain/数据库、不删除设备记录；保留现场用于根因分析。

回退只关闭“新交易”，不能阻断设备验证、待处理订单/审计补传、支付恢复和支持
导出。

## 5. App Review 与 Unlisted 申请

1. 先完成正常 App Review；审核说明需明确这是注册设备才能使用的门店 POS，
   提供可审核的演示账号/流程和硬件依赖说明。
2. 二进制通过审核后，由组织账号提交 Apple Unlisted App Distribution 申请。
3. 获得 Unlisted 链接后先在非生产设备验证安装、深链、首次注册、最低版本门禁和
   强制更新。
4. Unlisted 链接只解决发现与分发，不代表业务授权；应用仍须通过设备注册、审批、
   启用状态、门店范围和收银员权限。
5. 将最终 App Store URL 写入 Hbpos.Api 的 iPad 更新策略；先只审批试点设备，
   经变更审批后再逐店注册并启用设备。

## 6. 发布记录

每个发布必须记录：

- Git commit、应用版本、build number、runtimeVersion、EAS build ID。
- Hbpos.Api/管理端版本、`PosIpad` 更新策略和已审批设备清单。
- TestFlight 组、试点门店、设备代码与审批人。
- 真机矩阵报告、已知限制、所有未决 `Unknown`（预期为零）。
- App Review、Unlisted 申请和最终链接状态。
- 明确的 WPF 回退负责人、触发阈值和执行时间。
