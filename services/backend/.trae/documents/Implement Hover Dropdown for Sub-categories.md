我将实现二级分类的悬停下拉显示功能。

### 实施计划

1.  **修改 `StoreLayout.tsx`**:
    *   引入 Ant Design 的 `Dropdown` 组件。
    *   在渲染蓝色导航栏分类时：
        *   判断当前分类是否有子分类 (`cat.children`).
        *   如果有子分类，将其转换为 Dropdown 的菜单项 (`items`).
        *   使用 `<Dropdown>` 包裹分类名称，设置 `menu` 属性，实现鼠标悬停显示下拉菜单。
        *   点击子分类时同样触发 `handleCategoryClick` 跳转。

2.  **修改 `StoreLayout.less`**:
    *   添加下拉菜单的样式，确保视觉风格整洁统一。

这样，当鼠标移到一级分类上时，会自动向下展开显示其二级子分类，满足您的需求。

确认后我将开始执行。