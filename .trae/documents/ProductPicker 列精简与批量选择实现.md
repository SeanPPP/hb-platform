## 目标
- ProductPicker 只显示：供应商名称、货号/商品名称、进货价、零售价、折扣信息、商品类型。
- 支持批量选择商品并一次性回填到促销编辑表单。

## 变更方案
- 列精简与渲染：
  - 供应商名称：加载本地供应商列表，建立 `localSupplierCode -> name` 映射；列显示 `supplierNameMap[localSupplierCode] || localSupplierCode || '—'`。
  - 货号/商品名称：合并显示 `itemNumber` 与 `productName`，如：`[itemNumber] productName`，缺失显示 `—`。
  - 进货价/零售价：分别显示，保留两位小数。
  - 折扣信息：在无后端折扣字段前提下，显示“折扣率(估算)= (零售价-进货价)/零售价”，无数据显示“—”。若后续提供后端折扣字段，替换为真实值。
  - 商品类型：根据 `ProductType` 枚举显示文案（普通/套装/多码）。
  - 移除“操作”列及其他无关列。
- 批量选择：
  - 使用 antd Table `rowSelection` 管理选中行；在表格下方添加“选择所选”按钮，点击后将所有选中行的 `productCode` 回传。
  - 保留单项选择：支持行双击直接选择单个商品。
  - 扩展回调：为 ProductPicker 增加可选 `onPickMany?: (codes: string[]) => void`。
  - 在 `Promotions/index.tsx` 中传入 `onPickMany`，将 codes 批量 `Form.List.add({ productCode, unitWeight: 1 })`。

## 受影响文件
- `src/pages/PosAdmin/Promotions/ProductPicker.tsx`：列定义、供应商加载映射、批量选择、双击选择、props 扩展。
- `src/pages/PosAdmin/Promotions/index.tsx`：调用方增加 `onPickMany` 的处理逻辑。

## 验证
- 启动开发，打开“促销管理”→“新建促销”→“添加商品”：
  - 列仅显示指定字段，格式正确。
  - 选中多行点击“选择所选”后，促销编辑表单 `products` 列表增加对应多条记录。
  - 双击某行可快速选择单个商品。

## 说明
- 折扣信息暂采用估算展示，可随后对接真实折扣字段。
- 若需供应商名称完全准确，建议后端在商品列表返回中增加 `supplierName`，前端可直接显示。