1. 修改 `formatChartData()` 函数

   * 在 `formatChartData()` 中，过滤掉 `branchCode` 为空的数据

   * 移除 ALL 对应的汇总数据行

2. 具体改动：

   * 在 `formatChartData()` 函数中，过滤条件增加 `branchCode` 的检查

   * 只处理有有效 `branchCode` 的数据

