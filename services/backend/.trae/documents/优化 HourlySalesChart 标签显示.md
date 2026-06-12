1. 优化 `formatAmount` 函数：对小于 1000 的值保留 2 位小数，避免小数值显示为 `$0`

2. 调整标签配置：

   * 合计模式（`isAggregate = true`）：对数值大于 100 的点显示标签，减少标签重叠

   * 多分店模式（`isAggregate = false`）：使用 `autoHide: true` 让图表自动处理密集标签

   * 使用 `layout` 配置优化标签布局

