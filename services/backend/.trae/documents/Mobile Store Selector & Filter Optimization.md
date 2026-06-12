我将按照以下计划实施 Store Home 页面的移动端优化：

### 计划

**1. 实现移动端分店选择器 (Mobile Store Selector)**
*   **位置**: `src/pages/StoreHome/index.tsx`
*   **逻辑**:
    *   引入 `getUserStores` 接口（参考 `StoreLayout.tsx` 的实现）获取当前用户关联的分店列表。
    *   添加一个 `Select` 组件用于切换分店，数据绑定到全局状态 `initialState.selectedStore`。
*   **UI**:
    *   将选择器放置在页面主要内容区域的顶部。
    *   通过 CSS 控制：仅在移动端（屏幕宽度小于 768px）显示，桌面端隐藏。
    *   样式适配移动端，采用全宽或大尺寸点击区域。

**2. 优化移动端 "Sort by" 和 "Items per page" 显示**
*   **位置**: `src/pages/StoreHome/index.tsx` & `src/pages/StoreHome/index.less`
*   **修改**:
    *   重构筛选区域的 HTML 结构，以便更灵活地控制布局。
    *   **移动端适配**:
        *   隐藏 "Sort by:" 和 "Items per page:" 文本标签，节省屏幕空间。
        *   调整下拉框（Select）布局：在移动端让它们自动填充宽度（Flex 布局）或分行显示。
        *   确保 "排序字段"、"排序顺序" 和 "每页条数" 控件在手机屏幕上排列整齐且易于操作。

### 实施步骤

1.  **修改 `src/pages/StoreHome/index.tsx`**:
    *   导入 `getUserStores` API 和 `UserStoreDto` 类型。
    *   在组件中添加获取分店列表的逻辑 (`useEffect`)。
    *   添加移动端分店选择器的 JSX 代码块。
    *   给筛选区的文本标签添加 `className`（如 `styles.filterLabel`），以便在 CSS 中控制。
2.  **修改 `src/pages/StoreHome/index.less`**:
    *   新增 `.mobileStoreSelector` 样式类：默认 `display: none`，在 `@media` 移动端查询中设为 `display: block`。
    *   在 `@media (max-width: 768px)` 中优化 `.filters`：
        *   隐藏 `.filterLabel`。
        *   调整 `.filters` 容器为 `flex-wrap: wrap`。
        *   设置 `Select` 组件宽度为 `100%` 或 `flex: 1`，增加间距，防止拥挤。