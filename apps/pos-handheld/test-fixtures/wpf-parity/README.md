# WPF 对齐基线

- `feature-matrix.json` 是机器可校验的功能、真实 POS 权限码、联网方式、工作包和验收映射。
- `implementation-audit.json` 为每项功能记录生产 iPad 接线路径、自动测试路径和机器可读 blocker。
- `cash-rounding-cases.json` 固定澳币现金取整黄金用例。
- `order-sync.cash-sale.sample.json` 按现有 `OrderSyncRequest`、`OrderLineSyncDto` 和 `PaymentSyncDto` 形状提供最小现金销售载荷。

状态只允许：

- `implemented-and-tested`：存在生产接线和自动测试证据，且没有未解决 blocker。
- `in-progress`：已有部分实现或测试，但仍有生产接线、WPF 对齐、自动测试或真机验收 blocker。
- `planned`：尚无可计入生产功能的实现。

`real-device` 与 `external-release` blocker 必须由真机记录或外部发布结果关闭，不能以 fake、POC、页面存在或单元测试代替。

后续黄金载荷必须从 WPF 测试或受控测试宿主导出；不得通过手工修改生成目录掩盖合同漂移。
