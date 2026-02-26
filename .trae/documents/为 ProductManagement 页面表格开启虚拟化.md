我将为 `ProductManagement` 页面中的商品表格开启虚拟化，以提升渲染性能与滚动流畅度。

### 实施方案
- 引入并使用 `antd-virtualized-table` 的 `VList`，复用已安装的依赖。
- 在页面顶部创建虚拟化组件配置：`const components = useMemo(() => VList({ height: 'calc(100vh - 300px)', vid: 'product-management-table' }), [])`。
- 在 `<Table>` 上：
  - 保持现有 `scroll={{ x: 1500, y: 'calc(100vh - 300px)' }}`。
  - 增加 `components={components}`。
- 保持列固定与分页逻辑不变；虚拟化对固定列与选择列兼容。

### 变更范围
- 仅修改 `src/pages/PosAdmin/ProductManagement/index.tsx`，不影响其他页面逻辑。

确认后我将按上述方案修改并验证页面正常工作。