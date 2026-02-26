## 目标
- 去掉“折扣信息”列。
- 将“货号/商品名称”拆分为“货号”和“商品名称”两列。
- 增加筛选：供应商下拉、货号输入、商品名称输入；提交后按供应商服务端过滤、按货号/名称在前端过滤。

## 实现要点
- 列调整：
  - 供应商、货号（显示 `itemNumber || productCode`）、商品名称、进货价、零售价、商品类型。
- 表单筛选：
  - `supplier` 使用本地供应商列表构建选项；
  - `itemNumber` 与 `productName` 文本输入；
  - 提交时读取表单值，调用接口参数只传 `localSupplierCode` 与 `search`（由两者拼接）；列表返回后在前端再按 `itemNumber`/`productName`过滤以满足“分开”的需求。
- 初始化：打开弹窗时加载供应商选项与首次数据。

## 受影响文件
- `src/pages/PosAdmin/Promotions/ProductPicker.tsx`：调整 import、状态、`load`、`loadSuppliers`、表单与列定义。

## 验证
- 打开选择器：列显示为分开的“货号/商品名称”，无折扣列。
- 使用供应商、货号、商品名称筛选后，列表正确过滤。
- 原有批量/单选与去重逻辑保持。