# 使用 antd Tabs 实现多页签的最佳实践方案

## 当前问题
1. `umi-plugin-keep-alive` 存在 Context 传递问题
2. 所有 Tab 内容都在 DOM 中（即使未激活），性能浪费
3. 复杂的拖拽和缓存管理逻辑

## 最佳实践方案

### 方案选择：使用 antd Tabs 原生 + `react-activation`

#### 核心原则
1. **只渲染激活的 Tab** - antd Tabs 默认行为，自动销毁未激活内容
2. **使用 Keep-Alive 缓存** - 用 `<KeepAlive>` 包裹需要缓存的 Tab
3. **简化拖拽** - 保留 @dnd-kit 实现但优化代码结构

#### 修改内容

### 1. 回退到纯 `react-activation`（移除 umi-plugin-keep-alive）
- 卸载 `umi-plugin-keep-alive`
- 重新安装并使用 `react-activation`
- 移除 `.umirc.ts` 中的插件配置

### 2. 优化 KeepAliveTabLayout.tsx
- 移除 `display: block/none` 控制方式（让 antd Tabs 自动处理）
- 使用 `<AliveScope>` 包裹整个组件
- 只对 `keepAlive: true` 的 Tab 使用 `<KeepAlive>`
- 保留拖拽排序功能
- 保留右键菜单功能

### 3. 改进点
- 利用 antd Tabs 的 `items` API（更现代的写法）
- 简化组件映射
- 优化性能（未激活 Tab 不渲染）

## 优势
- ✅ 解决 Context 传递问题（`react-activation` 支持完整 Context）
- ✅ 更好的性能（未激活 Tab 不在 DOM 中）
- ✅ 代码更简洁（利用 antd Tabs 内置能力）
- ✅ 成熟的 Keep-Alive 方案（`react-activation`）