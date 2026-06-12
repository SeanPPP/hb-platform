## 原因分析
- 报错来源：KeepAliveTabLayout 的 TabContent 渲染中，当找不到 `componentMap[component]` 时会显示“组件未找到: PricingStrategies”。
- 现状：`src/layouts/KeepAliveTabLayout.tsx` 的 `componentMap` 未注册 `PricingStrategies`，虽然路由与 `app.ts` 的路径映射已配置，但 Tab 系统仍无法动态加载该组件。

## 修改方案
1. 在 `src/layouts/KeepAliveTabLayout.tsx` 的 `componentMap` 中添加：
   - `PricingStrategies: React.lazy(() => import('@/pages/PosAdmin/PricingStrategies') as any)`
2. 可选：在 `iconMap` 中补充 `SettingOutlined`，保证图标显示一致（当前不影响功能）。
3. 热更新验证或重启：该文件为前端组件，通常热更新即可；如未生效，重启前端 `npm run dev`。

## 验证步骤
- 登录后点击“收银后台管理 → 自动价格策略”，应正常打开 Tab，不再显示“组件未找到”；
- 列表显示分店/供应商标签，编辑弹窗回填规则与目标；保存/删除提示正常。

## 风险与影响
- 仅前端注册映射，安全无副作用；
- 不影响已有页面与路由配置。