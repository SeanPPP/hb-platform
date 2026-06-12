# React + Vite 后台框架

当前已经完成第一阶段迁移：

- React + Vite + TypeScript
- Ant Design 后台布局
- 路由驱动 Tabs
- KeepAlive 页面缓存
- 登录页与 Cookie 认证接入
- 当前用户与权限状态
- 系统管理菜单骨架
- 用户 / 角色 / 分店列表与详情页
- 详情页按完整路径单独开 Tab
- 动态更新 Tab 标题

## 启动

```bash
npm install
npm run dev
```

## 后端联调

默认开发代理：

- `/api` -> `http://localhost:5002`
- `/hangfire` -> `http://localhost:5002`

如果后端不是本地 5002，请修改 `vite.config.ts`。
