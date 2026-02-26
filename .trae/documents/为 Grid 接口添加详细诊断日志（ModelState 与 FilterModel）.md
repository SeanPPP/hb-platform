## 问题
- 目前页面的 Handsontable 未开启填充手柄（fill handle），导致无法拖拽复制。

## 方案
- 在网格组件上启用以下设置：
  - `fillHandle: { autoInsertRow: false, direction: 'vertical' }`（允许纵向拖拽填充，避免自动插入行）
  - `copyPaste: true`（启用复制/粘贴插件，配合拖拽批量填充）
  - 保留现有 `afterChange` 处理逻辑，来源非 `loadData` 的改动都会被收集保存（拖拽会触发多条变更）。

## 修改文件
- `ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx`
  - 在 `<HotTable />` 上增加上述两个属性；不改动其它逻辑。

## 验证
- 重载页面后，在可编辑列（复选框与数字列）选中一个单元格，用单元格右下角手柄向下拖拽，应批量填充。
- 保存修改，后端分别处理价格与特殊商品标记的批量更新。

确认后我将直接实施修改并完成验证。