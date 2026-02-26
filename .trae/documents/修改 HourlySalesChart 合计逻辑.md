1. 修改 `formatChartData()` 函数，添加分店合计逻辑

   * 当没有 `selectedBranchCode` 时，将所有分店的数据按小时合计

   * 将合计数据作为单一系列返回，系列名称为"Total"（可国际化）

   * 当有 `selectedBranchCode` 时，保持原有逻辑不变

2. 调整图表配置中的 `seriesField`：

   * 有合计数据时：不使用 `seriesField`（因为只有一条线）

   * 有多分店数据时：继续使用 `seriesField: 'branch'`

