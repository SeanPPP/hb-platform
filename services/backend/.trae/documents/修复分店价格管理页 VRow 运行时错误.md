## 原因分析
- 分店价格管理页使用 `antd-virtualized-table` 的 `VList`，生成的虚拟行组件 `VRow` 在内部执行 `Object.prototype.hasOwnProperty.call(ref, 'current')` 时，传入的 `ref` 可能为 `undefined/null`，导致 `TypeError: Cannot convert undefined or null to object`。
- 此问题与库内部实现细节和 React/rc-table 版本交互有关，属于第三方库兼容性缺陷。

## 修复方案
- 暂时移除该页的虚拟滚动，改回 AntD 原生 Table 滚动：
  - 删除 `components={components}`（由 `VList` 返回）。
  - 保留 `scroll={{ y: TABLE_V_SCROLL }}` 提供滚动体验。
  - 添加 `onRow={() => ({ style: {} })}` 防御性样式，避免行样式为 `undefined`。
- 如需继续使用虚拟滚动，可后续评估升级库或为其增加 `ref` 判空修复；当前以稳定为先。

## 修改范围
- 仅修改 `ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx` 的 `<Table>` 属性。

## 验证
- 启动前端后打开分店价格管理页：
  - 控制台不再出现 `Cannot convert undefined or null to object`。
  - 表格滚动、分页与编辑逻辑正常。

确认后我将实施并验证。