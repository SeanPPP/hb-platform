我将修复 Ant Design 组件的弃用警告。

### 1. 修复 `bodyStyle` 警告
*   **目标文件**:
    *   `ReactUmi/my-app/src/pages/StoreOrder/OrderList/components/StoreSidebar.tsx`
    *   `ReactUmi/my-app/src/pages/StoreOrder/components/CategorySidebar.tsx`
*   **操作**: 将 `Card` 组件中的 `bodyStyle={{ ... }}` 替换为 `styles={{ body: { ... } }}`。这是 Ant Design v5 的新 API。

### 2. 调查 `addonAfter` 警告
*   **目标**: 在 `ReactUmi/my-app/src` 目录下进行全局搜索。
*   **原因**: 初步检查未在当前编辑的文件中发现 `addonAfter` 的使用。该警告可能来自其他被加载的组件。
*   **操作**: 搜索代码库中的 `addonAfter`，找到后如果适用，将其替换为 `Space.Compact` 布局方案。

### 3. 验证
*   确认代码修改符合语法且正确使用了新的 API。
