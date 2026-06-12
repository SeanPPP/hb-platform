## 修改计划

### 1. 修改 `.umirc.ts`
将 `/store-front` 路由的布局从 `KeepAliveTabLayout` 改为 `StoreLayout`：
```javascript
{
  name: 'storeFront',
  path: '/store-front',
  icon: 'ShopOutlined',
  component: '@/layouts/StoreLayout',  // 改为 StoreLayout
  hideInBreadcrumb: true,
  routes: [
    {
      path: '',
      component: './StoreHome',
    },
  ],
}
```

### 2. 修改 `app.tsx`
从 `pathComponentMap` 中移除 `/store-front` 的映射，因为使用 `StoreLayout` 的页面不需要在 Tab 系统中管理。

这样 `/store-front` 路由将使用独立的 `StoreLayout` 布局，包含商店前台特有的头部、购物车、分类导航等功能。