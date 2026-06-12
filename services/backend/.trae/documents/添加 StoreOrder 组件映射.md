# 修复“组件未找到: StoreOrder”错误

用户报告“组件未找到: StoreOrder”错误。这意味着在 `KeepAliveTabLayout.tsx` 的 `componentMap` 中缺少 `StoreOrder` 组件的映射，导致动态加载时无法找到该组件。

## 1. 问题分析

在 `KeepAliveTabLayout.tsx` 中，`TabContent` 组件通过 `componentMap` 查找组件：

```typescript
const Component = componentMap[component];
if (!Component) {
  return <div>组件未找到: {component}</div>;
}
```

搜索结果显示 `KeepAliveTabLayout.tsx` 中已经有很多组件映射，但确实缺少 `StoreOrder`。

```typescript
// 动态导入组件映射
const componentMap: Record<string, any> = {
  Home: React.lazy(() => import('@/pages/Home') as any),
  StoreManagement: React.lazy(() => import('@/pages/StoreManagement') as any),
  // ... 其他组件
  WarehouseProducts: React.lazy(() => import('@/pages/WarehouseProducts') as any),
  // ⚠️ 缺少 StoreOrder
};
```

之前在 `app.ts` 中添加了路由映射：
```typescript
'/warehouse/store-order': {
  component: 'StoreOrder',
  // ...
},
```
这里指定了 `component` 名为 `'StoreOrder'`，但 `KeepAliveTabLayout` 不知道 `'StoreOrder'` 对应哪个文件。

## 2. 解决方案

在 `src/layouts/KeepAliveTabLayout.tsx` 的 `componentMap` 中添加 `StoreOrder` 的动态导入配置。

### 预期修改
在 `componentMap` 对象中添加：
```typescript
  StoreOrder: React.lazy(() => import('@/pages/StoreOrder') as any),
```

## 3. 验证计划
*   **代码修改**：应用上述修改到 `KeepAliveTabLayout.tsx`。
*   **验证**：用户（或我）刷新页面，再次点击“分店订货”菜单，应该能正常加载组件，不再显示“组件未找到”。

## 4. 额外检查
*   检查是否还有其他在 `app.ts` 中引用但未在 `KeepAliveTabLayout.tsx` 中定义的组件。
    *   `WarehouseProducts` (已存在)
    *   `StoreOrder` (本次添加)
    *   `StoreManagement` (已存在)
    *   ... (看起来其他主要的都在)

## 5. 执行步骤
1.  修改 `src/layouts/KeepAliveTabLayout.tsx`，在 `componentMap` 中添加 `StoreOrder`。
2.  通知用户已修复组件加载问题。
