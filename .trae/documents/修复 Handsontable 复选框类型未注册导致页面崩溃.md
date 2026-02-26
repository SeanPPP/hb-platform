## 问题
- 报错：You declared cell type "checkbox" as a string that is not mapped to a known object
- 原因：Handsontable v16 使用模块化类型，需要显式注册 `CheckboxCellType`（和 `NumericCellType`）后才可在列中使用 `type: 'checkbox' / 'numeric'`

## 方案
1. 在页面 `StoreRetailPrices/index.tsx` 引入并注册单元格类型：
   - `import { registerCellType } from 'handsontable/cellTypes'`
   - `import { CheckboxCellType } from 'handsontable/cellTypes/checkboxType'`
   - `import { NumericCellType } from 'handsontable/cellTypes/numericType'`
   - 执行 `registerCellType(CheckboxCellType); registerCellType(NumericCellType);`
2. 保持列定义中的 `type: 'checkbox' / 'numeric'` 不变，注册后正常工作。
3. 注册可在模块顶层或 `useEffect` 中保证仅一次执行。

## 验证
- 重新加载页面，Handsontable 不再崩溃，复选框与数值列正常渲染与编辑。