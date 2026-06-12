# 完善批量修改功能

根据用户需求，需要完善现有的 "Batch Update" 模态框，主要涉及 UI 文本调整和输入限制。

## 修改点

### 1. 修改 `OrderDetails/index.tsx` 中的 `Batch Update` Modal

*   **文本调整**: 将 Radio 选项 `Alloc Qty` 修改为 `Send Qty`，以符合用户术语。
*   **输入限制**: 在 `shouldUpdate` 的 render props 中，根据 `type` 动态设置 `InputNumber` 的 `precision` 属性。
    *   当 `type === 'quantity'` 时，设置 `precision={0}`，确保只能输入整数。同时保持 `min={0}`。
    *   当 `type === 'price'` 时，保持 `precision={2}`。

## 执行步骤

1.  修改 `OrderDetails/index.tsx` 文件。
2.  定位到 `Batch Update` Modal 的 `Form` 部分。
3.  修改 `Radio.Group` 中的 `Radio` 标签。
4.  修改 `Form.Item` 中的 `InputNumber` 组件，使其根据 `getFieldValue('type')` 动态设置 `precision`。

## 验证计划

*   打开 Batch Update Modal。
*   选择 `Send Qty`，验证是否只能输入非负整数。
*   选择 `Import Price`，验证是否可以输入两位小数。
