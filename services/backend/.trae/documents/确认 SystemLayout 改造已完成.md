这项任务实际上已经完成了。

**现状确认：**
1.  **SystemLayout.tsx** 已经添加了右上角用户区域、Avatar 和 Dropdown。
2.  **Dropdown 菜单项** 已经包含 "个人中心" 和 "退出登录"。
3.  **路由** 已经调整为 `/profile`（根据您之前的请求，已移出 `/system` 路径）。

**代码验证 (SystemLayout.tsx):**
```tsx
const menuItems = [
  {
    key: 'profile',
    label: '个人中心',
    icon: <UserOutlined />,
    onClick: () => history.push('/profile'), // 这里已经是 /profile 了
  },
  {
    key: 'logout',
    label: '退出登录',
    icon: <LogoutOutlined />,
    onClick: handleLogout,
  },
];
```

**可能需要做的微调：**
如果您的意图是**确认**或**再次修改**为跳转 `/system/profile`，请明确告知。但根据刚才的对话上下文，您要求将 UserProfile 移出一级菜单并独立路由，所以目前的实现 `/profile` 是符合最新要求的。

如果您只是想确认功能是否就绪，答案是：**是的，SystemLayout 改造已完成，功能已就绪。**

无需进行额外操作。