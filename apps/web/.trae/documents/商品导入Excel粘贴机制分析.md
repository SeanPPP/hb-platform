# 国内商品导入页面 Excel 粘贴机制分析与改进

## 当前粘贴机制分析

### 粘贴流程

1. 用户在页面任意位置按 `Ctrl+V` 触发全局 `paste` 事件
2. `handlePaste` 获取剪贴板文本，按 `\n` 分行、`\t` 分列
3. 定位目标单元格：通过 `e.target.closest('td')` 找到当前聚焦的 `<td>`
4. 计算 `startColIndex = tdIndex - 1`（减1是因为第一个 `<td>` 是 rowSelection 复选框列）
5. 根据 `startColIndex` 在 `editableColumns` 数组中映射到对应字段
6. 逐行逐列填充数据

### `editableColumns` 与表格列的对应关系

```
editableColumns 索引:  [0:quantity, 1:productCode, 2:barcode, 3:productName, 4:englishName, 5:domesticPrice, 6:oemPrice, 7:midPackQuantity, 8:casePackQuantity, 9:volume]
表格可见列:             件数       货号         条码    商品名称    英文名称    国内价格    贴牌价格   中包数      单件装箱数     单件体积
```

### 核心问题

**当前粘贴机制依赖 `<td>` 在 DOM 中的索引位置来映射字段。**

问题场景：
1. **`startColIndex = tdIndex - 1`**：假设第一个 `<td>` 是 rowSelection 复选框列，但如果 Ant Design 渲染顺序变化（如 `fixed:'left'` 列），索引会偏移
2. **Excel 列顺序必须与表格列顺序完全一致**：用户 Excel 的列排列如果和 `editableColumns` 顺序不同，数据会错列
3. **用户需要先点击一个单元格作为起点**：如果没点中表格内的 `<td>`（如点在了 `<input>` 上），粘贴会失败

### 当前使用方式

用户需要：
1. 先点击表格中某行的某个单元格（如"件数"列）
2. 从 Excel 复制数据（列顺序必须匹配表格列顺序）
3. 在页面按 `Ctrl+V`

## 需要确认的问题

这是一个**调研/分析**任务，不是代码修改任务。用户问的是"怎么才能粘贴 Excel 数据到对应列中"，即想了解粘贴功能的使用方法或当前是否有 bug。

### 如果粘贴不生效，可能的原因

1. **没选中表格单元格**：粘贴前需要先点击表格中的输入框
2. **焦点在 Input 内部**：代码第344-345行，如果焦点在 `<input>` 内且粘贴的是单个值，会跳过自定义粘贴逻辑，让浏览器默认处理
3. **rowSelection 复选框列导致列偏移**：`tdIndex - 1` 的计算可能不准确

### 改进方向（如需要）

1. **列映射用 `data-index` 属性**：给每个可编辑列的 `<td>` 添加 `data-col-key` 属性，粘贴时直接读取而不依赖索引
2. **提供列映射配置**：让用户自定义 Excel 列与表格列的对应关系
3. **增加粘贴预览**：粘贴前弹窗预览数据将如何填入
