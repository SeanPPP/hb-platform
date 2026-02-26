# Tab 管理改为 antd Tabs (display:none) 方案

## 修改文件

### 1. KeepAliveTabLayout.tsx
- 移除 `KeepAlive` 和 `useAliveController` 导入
- 添加 refreshKeys 状态管理（用于刷新机制）
- 移除 `aliveController` 相关的所有逻辑（drop 调用）
- 修改 `TabContent` 组件：移除 KeepAlive 包裹
- 修改刷新逻辑：使用 key 强制重新渲染
- 修改右键菜单：更新刷新实现
- 移除 useEffect 调试代码

### 2. KeepAliveTabLayout.less  
- 添加隐藏 Tab 内容的样式

## 技术方案

- 所有 Tab 同时渲染，通过 `display: none` 控制可见性
- 激活 Tab 显示 `block`，非激活 Tab 隐藏
- 刷新功能：使用动态 key 强制组件重新渲染
- 保持拖拽、右键菜单等现有功能

## 注意事项

- 所有打开的 Tab 都会保持渲染状态，可能增加内存占用
- 组件不会卸载/重新挂载，只有显示/隐藏切换