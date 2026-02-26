## 修改计划

### 1. 修改 `.umirc.ts`
在 routes 数组中添加新的路由配置（在 `home` 路由之后）：
```javascript
{
  name: 'storeFront',
  path: '/store-front',
  icon: 'ShopOutlined',
  component: '@/layouts/KeepAliveTabLayout',
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
在 `pathComponentMap` 对象中添加映射（在 `/home` 映射之后）：
```javascript
'/store-front': {
  component: 'StoreHome',
  icon: 'ShopOutlined',
  keepAlive: true,
},
```

### 3. 修改 `zh-CN.ts`
添加菜单翻译：
```javascript
'menu.storeFront': '商店前台',
```

### 4. 修改 `en-US.ts`
添加英文菜单翻译：
```javascript
'menu.storeFront': 'Store Front',
```

这样用户在主菜单中点击"商店前台"后，会在 Tab 中打开 StoreHome 页面（实际跳转到 `/store-front`，组件内容与 StoreHome 相同）。