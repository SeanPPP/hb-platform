## 原因分析
- Umi 对路由 `component` 的解析默认使用相对 `src/pages` 的模块解析；`./PosAdmin/PricingStrategies/index.tsx` 在当前 preset 解析器下未被识别。
- 同项目其他页面均使用不带扩展名的目录形式，或使用 `@` 别名，解析更稳定。

## 修改方案
1. 将 `.umirc.ts` 中定价策略的 `component` 改为 `@/pages/PosAdmin/PricingStrategies`（使用 `@` 指向 `src`，不带扩展名）。
2. 保持 `path` 为 `/pos-admin/pricing-strategies`，其它配置不变。
3. 清理 Umi 缓存并重启开发服务器，确保新路由生效。

## 验证步骤
- `npm run dev` 启动后登录；
- 左侧菜单“收银后台管理 → 自动价格策略”出现；
- 点击菜单可打开页面 Tab，并加载列表数据。