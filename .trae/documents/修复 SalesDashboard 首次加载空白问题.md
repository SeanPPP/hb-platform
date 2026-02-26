## 问题根因

第一次打开 SalesDashboard 时出现空白页面，原因是：

1. **KeepAlive 缓存初始化时的渲染时序问题**
   - 首次加载时 GlobalStateGuard 渲染了 4 次（正常应该 2 次）
   - IntlProvider 在 KeepAlive 内部，首次挂载时上下文可能未就绪

2. **多层异步状态检查导致的重复渲染**
   - GlobalStateGuard 的 50ms 轮询
   - useCurrentUser/useAccessInfo 的 50ms 轮询
   - 这些机制在首次加载时叠加，导致渲染混乱

## 解决方案

### 方案 1：优化 TabContent 的 IntlProvider 位置（推荐）

**修改文件**：`ReactUmi/my-app/src/layouts/KeepAliveTabLayout.tsx`

**改动**：
- 将 `IntlProvider` 移到 `KeepAlive` 外层
- 确保 intl 上下文在组件缓存前就已就绪

```tsx
// 当前（第 211-218 行）
<KeepAlive name={tabKey} id={tabKey}>
  <IntlProvider locale={locale} messages={messages}>
    <GlobalStateGuard>
      <Component {...params} />
    </GlobalStateGuard>
  </IntlProvider>
</KeepAlive>

// 修改为
<IntlProvider locale={locale} messages={messages}>
  <KeepAlive name={tabKey} id={tabKey}>
    <GlobalStateGuard>
      <Component {...params} />
    </GlobalStateGuard>
  </KeepAlive>
</IntlProvider>
```

### 方案 2：优化 GlobalStateGuard 的初始化逻辑

**修改文件**：`ReactUmi/my-app/src/components/GlobalStateGuard.tsx`

**改动**：
- 减少轮询频率（50ms → 100ms）
- 添加首次渲染的快速通道
- 优化 ready 状态的判断逻辑

### 方案 3：为 SalesDashboard 添加额外的加载保护

**修改文件**：`ReactUmi/my-app/src/pages/SalesDashboard/index.tsx`

**改动**：
- 在组件顶部添加全局状态就绪检查
- 确保在状态未就绪时显示 loading

## 推荐执行顺序

1. **先执行方案 1**（IntlProvider 位置调整）- 这是最直接的修复
2. **验证效果**，如果仍有问题，再执行方案 2
3. 方案 3 作为兜底保护

## 预期效果

- 首次打开 SalesDashboard 时正常显示内容
- GlobalStateGuard 渲染次数减少到 2 次
- 不再出现空白页面问题
