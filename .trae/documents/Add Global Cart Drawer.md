# 在 StoreLayout 添加全局购物车抽屉

我将在 `StoreLayout` 中添加一个全局购物车抽屉，使其在所有使用该布局的页面中均可访问。根据您的要求，这个抽屉将是一个全新的组件，独立于 `StoreOrder` 页面中已有的抽屉。

## 1. 创建新组件: `src/components/LayoutCartDrawer/index.tsx`
我将创建一个专用于布局的 Drawer 组件。
*   **Props 参数**:
    *   `visible`: boolean (控制显示隐藏)
    *   `onClose`: function (关闭回调)
    *   `cart`: `StoreOrderCartDto` (购物车数据)
    *   `refreshCart`: function (数据刷新回调)
*   **功能**:
    *   显示购物车商品列表（图片、名称、编号、数量、价格）。
    *   支持 **删除商品**。
    *   支持 **修改数量**。
    *   **Checkout 按钮**：用于跳转到结算页。
*   **逻辑**:
    *   调用 `removeFromCart` 和 `updateCartItem` API（与原有抽屉使用相同的后端接口，但前端逻辑独立实现）。
    *   在任何修改操作后，调用 `refreshCart()` 和 `(window as any).refreshGlobalCart()` 以确保全局数据同步。

## 2. 修改 `src/layouts/StoreLayout.tsx`
*   **引入** 新的 `LayoutCartDrawer` 组件。
*   **添加状态**: `const [cartDrawerVisible, setCartDrawerVisible] = useState(false);`
*   **添加交互**:
    *   定位到 "Shopping Cart" 头部区域 (约第 178 行)。
    *   在 `styles.cartSummary` div 上添加 `onClick={() => setCartDrawerVisible(true)}`。
    *   添加 `cursor: pointer` 样式，提示用户该区域可点击。
*   **渲染组件**:
    *   在 JSX 的末尾渲染 `<LayoutCartDrawer ... />`，并传递必要的 props (`cart`, `visible`, `onClose` 等)。

## 3. 验证
*   点击顶部的 "Shopping Cart" 区域应打开新抽屉。
*   抽屉应显示当前选中分店的正确商品。
*   在新抽屉中修改商品应立即更新顶部的购物车摘要。
