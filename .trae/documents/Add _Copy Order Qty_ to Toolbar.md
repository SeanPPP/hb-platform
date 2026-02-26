# 添加“复制订单数量”功能（仅针对已选行）

我将实现一个功能，允许用户将**已选行**的“订单数量”复制到“发货数量”。为了与现有的“批量更新”功能保持一致，该按钮将放置在表格标题区域（当有选中行时显示）。

## 1. 导入图标
- 在 `d:\Development\cline\blazor\reactumi\my-app\src\pages\storeorder\orderdetails\index.tsx` 中导入 `CopyOutlined`。

## 2. 实现处理函数
- 创建 `handleCopySelectedOrderQty` 函数。
- **逻辑:**
  1. 检查 `selectedRowKeys` 是否有值。
  2. 在 `order.items` 中查找对应的商品。
  3. 筛选出 `quantity > 0` （订单数量大于0）的商品。
  4. 构建批量更新请求：`{ productCode, quantity: item.quantity }`。
     - *注意：此操作将把 Order Qty 的值写入 Send Qty (allocQuantity)。*
  5. 调用 `batchUpdateOrderLine` 服务。
  6. 成功后刷新页面并清空选择（可选，或保留选择）。

## 3. 更新 UI
- 在 `ProTable` 的 `headerTitle` 属性中，在现有的“Batch Update”按钮旁添加新按钮。
- **按钮配置:**
  - **条件:** `isEditable && selectedRowKeys.length > 0`
  - **图标:** `<CopyOutlined />`
  - **文本:** "Copy Order Qty"
  - **交互:** 使用 `Popconfirm` 提示 "Copy Order Qty to Send Qty for selected items?"（确定将所选项目的订单数量复制到发货数量吗？）。

## 4. 验证
- 选中几行数据。
- 点击“Copy Order Qty”按钮。
- 确认“Send Qty”列的值已更新为对应的“Order Qty”。
