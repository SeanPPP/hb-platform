## 目标
- 在 `StoreRetailPriceReactService` 的网格与计数查询中完全移除对 `ChinaSupplier`（国内供应商）表的依赖及引用，保留 `HBLocalSupplier` 信息。

## 变更范围
- 文件：`BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs`
  - 网格查询构建：`41-43`、`254-257`
  - 全局搜索与筛选：`45-58`、`86-91`、`288-297`
  - 排序：`185-239`（其中 `supplierName` 排序）
  - 选择投影：`394-415`（`SupplierName` 字段）
  - 文本/数字筛选辅助方法调用：`76-106`、`114-129`、`283-312`、`318-352`

## 具体修改
1. 移除 `ChinaSupplier` 关联
   - 删除 `.LeftJoin<ChinaSupplier>((p, prod, sup, cs) => p.SupplierCode == cs.SupplierCode)`（`41-43` 与 `254-257`）。
   - 将所有 `(p, prod, sup, cs, st)` 的 5 元组 Lambda 改为 `(p, prod, sup, st)` 的 4 元组。

2. 调整全局搜索与筛选逻辑
   - 全局搜索去掉 `cs.SupplierName`：仅保留 `sup.Name`（`45-58`）
   - 列过滤 `supplierName`：移除 `ApplyTextEval(cs.SupplierName, ...)`，仅保留 `ApplyTextEval(sup.Name, ...)`（`86-91`、`288-297`）
   - 将 `ApplyText5(...)` 的调用替换为已有的 4 表版本 `ApplyText(...)`（`76-106`、`283-312`）

3. 数字筛选辅助方法
   - 新增 `ApplyNumber(...)`（签名与 `ApplyNumber5(...)` 相同逻辑，但类型为 `ISugarQueryable<StoreRetailPrice, Product, HBLocalSupplier, Store>`）。
   - 将 `query`/`countQuery` 中的数字筛选改用 `ApplyNumber(...)`（`114-129`、`318-352`）。

4. 排序与投影的 `supplierName`
   - 排序中的 `supplierName` 从 `SqlFunc.IsNull(sup.Name, cs.SupplierName)` 改为 `sup.Name`（`197-200`）。
   - 选择投影里的 `SupplierName` 从 `SqlFunc.IsNull(sup.Name, cs.SupplierName)` 改为 `sup.Name`（`403`）。

5. 兼容性与清理
   - 保留已存在的 `ApplyText5/ApplyNumber5` 方法但不再使用，避免影响其它潜在调用。
   - 若编译器提示未使用，可在后续清理中删除。

## 验证
- 构建项目并确保无编译错误。
- 基本回归：
  - 全局搜索与 `supplierName` 过滤仍正常（仅匹配本地供应商 `HBLocalSupplier.Name`）。
  - 排序与分页、数字过滤（价格、折扣、零售价）正常返回。
- 若需保留原有“国内/本地供应商二选一”的展示，请确认再回滚至 `SqlFunc.IsNull` 方案。

确认后我将按以上步骤实施并提供构建与验证结果。