# HB POS Handheld 架构边界

## 独立应用

- npm：`@hb/pos-handheld`
- Expo slug：`hb-pos-handheld`
- scheme：`hbpos-handheld`
- iOS bundle / Android package：`com.hbweb.poshandheld`
- 目标：iPhone iOS 17+；Android PDA 11/API 30+
- 技术栈与 `apps/pos-ipad` 对齐，但代码、发布通道和应用身份独立。

## 共享与隔离

- 共享现有 Hbpos POS API、业务合同、离线队列、SQLCipher 数据模型和支付恢复规则。
- 手持端通过平台端口注入 `iOS` 或 `Android`，不得发送 `iPadOS`。
- EAS 构建、OTA、原生更新与下载记录按 `pos-handheld` AppKey/Project 隔离。
- 不包含客显模块、客显状态、客显设置、广告缓存或第二 React surface。
- 不新增 Square 移动回调；继续使用 checkout 查询和服务器 webhook。

## 外设

- 扫码：HID 与相机可用；Android 厂商 Intent 仅保留默认禁用的扩展端口。
- 打印机：iOS BLE；Android BLE GATT + Classic SPP，由用户选择 transport。
- 单次打印/钱箱 operation 锁定 transport，未知结果不自动重放或跨通道 fallback。
- Android 以不可导出的 Keystore 包装密钥保护 A256 考勤密钥；现有注册合同所需的 A256 材料仅允许一次性读取，之后保持已消费状态。
- Android APK 只能在签名/摘要/大小/包身份验证通过后交给系统安装器。

## UI

- 设计基线：[`design/prompt-set.md`](./design/prompt-set.md)。
- 46 个状态：[`state-matrix.json`](../test-fixtures/handheld-design/state-matrix.json)。
- 单列竖屏、8px 间距、至少 48px 触控、小圆角、固定关键操作区。
- 无渐变、玻璃、夸张卡片、底部 Tab、页面 Logo 或水印。

## 发布

- iPhone：EAS `production`，TestFlight / App Store。
- Android：EAS `android-internal`，签名内部 APK。
- 本仓库配置不代表已发布；发布必须另行授权并经过真机验收。
