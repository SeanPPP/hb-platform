## 目标
- 在 `d:/Development/cline/blazor/ReactUmi/my-app/src/pages/PosAdmin/ProductManagement/index.tsx` 的编辑弹窗中，无论商品类型为“套装商品(1)”还是“多码商品(2)”，底部表都展示 `ProductSetCode` 表中的 `SetPurchasePrice`、`SetRetailPrice` 列，并支持价格编辑与保存。

## 现状
- “多码商品(2)”子表已使用 `product-set-codes/grid`，列含 `setPurchasePrice`、`setRetailPrice`（已满足）。
- “套装商品(1)”子表使用国内套装子项接口（`/domestic-products/{productCode}/set-items`），列是 `domesticPrice/oemPrice/importPrice`，与 `ProductSetCode` 不一致。
- 后端模型 `ProductSetCode` 包含 `SetPurchasePrice/SetRetailPrice`（BlazorApp.Shared/Models/HBweb/ProductSetCode.cs:45-56）。

## 调整方案
1. 统一数据源：套装商品(1)也使用 `product-set-codes/grid` 接口加载数据（与多码一致）。
   - 过滤方式：沿用当前“多码”逻辑按父商品 `barcode equals` 过滤（index.tsx 现有 `getSetGridData` 调用）。
2. 统一列定义：在两种类型下的子表都使用以下列：
   - `setBarcode`（支持点击预览）
   - `setPurchasePrice`（可编辑 InputNumber）
   - `setRetailPrice`（可编辑 InputNumber）
   - `isActive`、`updatedAt`
3. 统一保存：
   - 两种类型下的价格编辑都通过 `batchUpdateSetPrices({ items: [{ id:SetCodeId, setPurchasePrice, setRetailPrice }] })` 提交。
   - 删除套装子项的国内接口保存分支（`updateSetItems`）避免列含混。
4. 交互与空态：
   - 保留条码预览按钮（复用 `openBarcode`）。
   - 空态文案统一为“暂无套装/多码条码”。

## 具体修改点
- `index.tsx`
  - 将 `productTypeWatch === 1` 的加载函数改为调用 `getSetGridData` 并赋值到 `setCodes`（参考 `productTypeWatch === 2` 的现有逻辑）。
  - 替换 `productTypeWatch === 1` 子表为 `setCodes` 表，列包含 `setBarcode/setPurchasePrice/setRetailPrice/isActive/updatedAt`。
  - 在保存函数 `handleSaveEdit` 中，移除 `updateDomesticSetItems` 分支，统一构造 `items` 调用 `batchUpdateSetPrices`。（index.tsx:220-260 范围）
  - 保留 `Admin` 权限提示（批量价格更新接口需要 Admin）。

## 验证
- 用类型=1 的商品打开编辑弹窗：应显示 `setBarcode/SetPurchasePrice/SetRetailPrice` 并可编辑保存；刷新后数据正确。
- 用类型=2 的商品打开编辑弹窗：保持当前行为，编辑保存价格成功。

## 说明
- 若需要“套装商品”的条码编辑功能，建议单独提供按钮和接口（目前 `product-set-codes` 未提供条码更新）。本次按需求聚焦展示与编辑采购/零售价。