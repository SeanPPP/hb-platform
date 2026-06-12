## 实施计划：在 Tab 中打开个人中心

### 1. 目标
用户希望点击"个人中心"时，不是跳转到 `/profile` 路由（全页加载），而是在现有的 `KeepAliveTabLayout` 中打开一个新的 Tab。

### 2. 变更内容

#### A. `src/layouts/KeepAliveTabLayout.tsx`
- **注册组件**：在 `componentMap` 中添加 `UserProfile` 组件的懒加载引用。
- **引用路径**：`'@/pages/UserProfile'`。

#### B. `src/app.tsx`
- **修改点击事件**：在 `rightContentRender` 的 `menuItems` 中，将"个人中心"的 `onClick` 事件从 `history.push('/profile')` 改为调用全局的 `g_tabModel.addTab` 方法。
- **配置 Tab 参数**：
    - key: `'profile'`
    - title: 使用国际化 key `'menu.profile'`
    - path: `'/profile'` (保持逻辑路径一致)
    - component: `'UserProfile'` (对应 componentMap 中的 key)
    - icon: `'UserOutlined'`

### 3. 实施步骤
1.  修改 `KeepAliveTabLayout.tsx`，在 `componentMap` 中添加 `UserProfile`。
2.  修改 `app.tsx`，更新 `menuItems` 中的"个人中心"点击逻辑，使其通过 `addTab` 打开。

这样修改后，点击个人中心将会在主工作区打开一个新的 Tab，而不是整页跳转，保持了系统操作的一致性。