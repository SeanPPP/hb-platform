## 目标
- 提高页面中分店商品价格表格的可视高度，让更多数据行在一屏内可见。

## 方案
- 使用 Ant Design Table 的 `scroll.y` 控制垂直滚动区域高度，当前代码为：`scroll={{ x: 1800, y: 'calc(100vh - 400px)' }}`。
- 将 `y` 调整为更高的数值，例如 `calc(100vh - 300px)` 或 `calc(100vh - 260px)`，保留顶部筛选与操作区域的空间。

## 实施细节
- 文件：`ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx`
- 位置：Table 组件的 `scroll` 属性（约 569 行附近）。
- 修改：
  - 简单直接：把 `y: 'calc(100vh - 400px)'` 改为 `y: 'calc(100vh - 300px)'`。
  - 可维护：在文件顶部定义常量 `const TABLE_V_SCROLL = 'calc(100vh - 300px)'`，并在 Table 中使用 `y: TABLE_V_SCROLL`，后续只需改常量即可调整高度。

## 备选（CSS 容器）
- 若希望由外层 div 控制高度：
  - 在 Table 外包一层 `<div style={{ height: 'calc(100vh - 300px)', overflow: 'auto' }}>`。
  - 同时可以保留或移除 Table 的 `scroll.y`（推荐保留 Table 的 `scroll.y`，Antd 在固定表头/列时更稳定）。

## 验证
- 打开页面后，观察表格可视区域明显增高；滚动体验正常；筛选区和按钮不被遮挡。
- 根据需要微调 `-300px` 的偏移量，找到你最满意的高度。

确认后我将按上述方式把表格高度提高为 `calc(100vh - 300px)` 并将其提取为常量以便后续调整。