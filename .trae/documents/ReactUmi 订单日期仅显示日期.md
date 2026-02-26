## 目标
- 在 ReactUmi 的订单列表页面，只显示订单日期的“日期”部分，不显示时间，统一为 dd/MM/yyyy。

## 修改点
- 文件：ReactUmi/my-app/src/pages/StoreOrder/OrderList/index.tsx
- 将列定义中 Order Date 的 render 从 `toLocaleString()` 改为自定义日期格式化，仅显示 dd/MM/yyyy。
- 处理字符串形式（如 "2025-12-02T00:00:00"）避免时区偏移，优先使用字符串分割获取日期部分，否则使用 Date 构造格式化。

## 验证
- 列表中所有订单的日期仅显示如 02/12/2025。
- 切换筛选和分页后格式保持一致。

## 兼容
- 不引入额外依赖库（dayjs/moment），直接使用轻量级格式化函数，避免引入新依赖。