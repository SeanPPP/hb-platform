## 目标
- 针对 `d:/Development/cline/blazor/BlazorApp.Api/Services/Pricing/AutoPricingService.cs` 的策略加载/选择：当 `storeCode` 为空时，仅参与匹配的策略为：
  - 全局策略（无分店目标）
  - 仅有供应商目标且没有分店目标的策略

## 实施位置
- 方法：`FindStrategyForPriceAsync(purchasePrice, supplierCode, storeCode)`
- 步骤：
  1. 扁平化 LeftJoin 查询与 GroupBy 组装已完成，得到 `strategies`（含 `Details/Targets`）。
  2. 若 `string.IsNullOrEmpty(storeCode)`，先对 `strategies` 进行收敛：
     - `strategies = strategies.Where(s => !(s.Targets?.Any(t => t.TargetType == "Store") ?? false)).ToList();`
     - 保留无 `Store` 目标的策略（全局或仅供应商）。
  3. 再进行价格区间过滤与优先级选择（同时命中 → 供应商 → 分店 → 全局兜底 → 默认倍率）。

## 验证
- 当 `storeCode` 为空：不会命中任何包含 `Store` 目标的策略；仅命中全局或仅供应商目标的策略。
- 当 `storeCode` 有值：维持现有顺序：同时命中 → 供应商 → 分店 → 全局兜底。
- 评估端点随之生效，策略测试区域按期望返回。