## 原因分析
- Tab系统通过 `app.ts` 的 `menuItemRender` 拦截菜单点击，并使用 `pathComponentMap` 将路由路径映射为组件键。如果路径未在映射表中，点击不会调用 `addTab` → 不会打开Tab。
- Tab内容渲染依赖 `KeepAliveTabLayout.tsx` 的 `componentMap` 将组件键映射到实际页面。如果缺少键，也无法加载内容。
- 新页面路由使用 `MoneyCollectOutlined` 图标，当前 `iconMap` 未包含该图标，菜单与Tab图标无法显示。

## 更新项目
1. 在 `src/app.ts` 的 `pathComponentMap` 中添加：`'/pos-admin/store-retail-prices' → { component: 'StoreRetailPrices', icon: 'MoneyCollectOutlined', keepAlive: true }`。
2. 在 `src/layouts/KeepAliveTabLayout.tsx` 的 `componentMap` 中添加组件键：`StoreRetailPrices: React.lazy(() => import('@/pages/PosAdmin/StoreRetailPrices'))`。
3. 补全图标映射：
   - `src/app.ts` 的 `iconMap` 增加 `MoneyCollectOutlined` 映射。
   - `src/layouts/KeepAliveTabLayout.tsx` 的 `iconMap` 增加 `MoneyCollectOutlined` 映射。

## 结果
- 点击菜单“分店商品价格管理”后会调用 `addTab` 打开对应Tab，并在Tab内容区域正确渲染页面。
- 菜单与Tab标签显示 `MoneyCollectOutlined` 图标。

## 验证
- 重新启动前端 dev（或热更新），在“收银后台管理”菜单下点击“分店商品价格管理”，确认Tab出现并页面正常加载与编辑保存。