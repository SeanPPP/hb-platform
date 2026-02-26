**原因分析**：
用户反馈首页内容仍为 "Welcome" 页面，是因为：
1. 之前的修改仅将根路径 `/` 重定向到了 `/warehouse/store-order`。
2. 但是系统菜单中的 "首页" (`/home`) 对应的组件配置仍然是 `component: '@/layouts/KeepAliveTabLayout'`。
3. 当用户点击 "首页" 标签或直接访问 `/home` 时，渲染的是 `KeepAliveTabLayout`，而该组件在没有子路由的情况下可能默认显示了欢迎信息。

**解决方案**：
要让 "首页" (`/home`) 真正显示 "订货页面" 的内容，我们需要直接修改 `/home` 路由的组件指向。

1. **修改 `.umirc.ts`**：
   - 将 `/home` 路由的 `component` 修改为 `./StoreOrder`（即订货页面组件）。
   - 将根路径 `/` 的重定向改回 `/home`，保持 URL 的整洁性（访问域名直接进入 `/home`，显示订货内容）。

**代码变更预览**：

```typescript
    // .umirc.ts
    {
      path: '/',
      redirect: '/home', // 改回 /home
    },
    {
      name: 'home',
      path: '/home',
      icon: 'HomeOutlined',
      component: './StoreOrder', // 指向订货页面组件
    },
```

这样，"首页" 菜单项将直接加载订货页面。

