## 问题分析
- AntD Card 警告：`bodyStyle` 已废弃，需改用 `styles.body`。现有使用点：
  - `ReactUmi/my-app/src/pages/PosAdmin/ProductManagement/index.tsx:558`
  - `ReactUmi/my-app/src/pages/PosAdmin/LocalSupplierInvoiceDetail/index.tsx:923`
  - `ReactUmi/my-app/src/pages/PosAdmin/SupplierManagement/index.tsx:63`
  - `ReactUmi/my-app/src/pages/SystemSettings/HqDataSync/index.tsx:585, 616, 703, 789, 818, 848, 864, 880`
- AntD Input 警告：`addonAfter` 已废弃，推荐 `Space.Compact`。日志出现在 `<Search>` 组件（`ProductManagementPage` 处），尽管代码未直接使用 `addonAfter`，但 `<Input.Search>` 在当前版本内部可能仍使用旧接口，触发警告。
- 运行时异常：`TypeError: Cannot convert undefined or null to object` 源于 `antd-virtualized-table` 的 `VRow` 对 `props.style` 进行对象展开（`style: { ...style, height: rowHeight }`），当 `style` 为 `undefined/null` 时会抛错。库代码位置：`node_modules/antd-virtualized-table/dist/index.js:708-709`。

## 修改方案
- ProductManagement 页：
  - 将 `<Card bodyStyle={...}>` 改为 `<Card styles={{ body: ... }}>`（`index.tsx:558`）。
  - 用 `Space.Compact` 重写搜索输入：用 `<Input>` + `<Button type="primary">` 组合，保留 `allowClear`、`value`、`onChange`、`Enter` 提交与点击触发 `loadProducts`，替换 `<Input.Search>`（`index.tsx:582-589`）。
  - 为使用虚拟滚动的 `<Table>` 添加 `onRow={() => ({ style: {} })}`，确保 `VRow` 接收的 `style` 为对象，避免展开 `undefined`（`index.tsx:603`）。
- 其他使用 `bodyStyle` 的页面：逐一替换为 `styles={{ body: ... }}`。
  - `LocalSupplierInvoiceDetail/index.tsx:923`
  - `SupplierManagement/index.tsx:63`
  - `SystemSettings/HqDataSync/index.tsx` 多处（585、616、703、789、818、848、864、880）。
- 若页面存在 `headStyle`，同步迁移为 `styles={{ header: ... }}`，保持 v5 写法一致。

## 验证步骤
- 打开 ProductManagement 页面，确认：
  - 控制台不再出现 `[antd: Card] bodyStyle is deprecated` 与 `[antd: Input] addonAfter is deprecated` 警告。
  - 表格正常渲染并虚拟滚动，滚动与分页无异常。
  - 原有搜索（回车与按钮）与批量操作功能可用。
- 打开已替换的其他页面，确认无 `bodyStyle` 警告、布局与交互一致。

## 影响与风险控制
- 样式迁移为官方推荐属性，不改变视觉表现；`Space.Compact` 与 `<Input.Search>` 功能等价（更可控）。
- `onRow` 增补仅提供空样式对象，无副作用，专用于兼容库内部展开逻辑。
- 改动范围有限且分页面实施，易于回滚。

## 回滚方案
- 每个页面独立修改，若出现问题可单独撤销对应文件的变更。

请确认以上方案，确认后我将实施并进行验证。