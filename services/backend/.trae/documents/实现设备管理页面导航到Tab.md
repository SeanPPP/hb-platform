## 实现点击导航在 Tab 中打开设备管理页面

### 需要修改的文件

#### 1. app.tsx
在 `pathComponentMap` 中添加设备管理页面的映射配置：
```typescript
'/system/device-management': {
  component: 'DeviceManagement',
  icon: 'MobileOutlined',
  keepAlive: true,
}
```

#### 2. KeepAliveTabLayout.tsx  
在 `componentMap` 中添加 DeviceManagement 组件的动态导入：
```typescript
DeviceManagement: React.lazy(() => import('@/pages/SystemSettings/DeviceManagement') as any),
```

在 `iconMap` 中添加 MobileOutlined 图标映射：
```typescript
MobileOutlined: <MobileOutlined />,
```

这样，当用户点击左侧菜单的"设备管理"时，系统会：
1. 通过 `menuItemRender` 拦截点击事件
2. 调用 `g_tabModel.addTab()` 添加新的 Tab
3. Tab 内容通过 `componentMap` 加载对应的组件
4. 页面在 Tab 中打开，保持多标签页体验