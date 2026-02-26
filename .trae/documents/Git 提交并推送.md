## 问题根因

找到了真正的问题！在 `SalesDashboard/index.tsx` 第 272 行：

```tsx
{isDataReady && (
  <div>
    // 所有页面内容
  </div>
)}
```

**问题流程**：

1. 首次渲染时 GlobalStateGuard 渲染 4 次
2. 前几次渲染时 `isDataReady = false`（因为 `currentUser` 或 `access` 未就绪）
3. 条件渲染跳过所有内容，返回空的 `PageContainer`
4. **KeepAlive 缓存了这个空白状态**
5. 即使后续数据就绪，KeepAlive 也不会更新已缓存的内容

## 解决方案

### 方案 1：移除条件渲染，改用 Loading 状态（推荐）

**修改文件**：`ReactUmi/my-app/src/pages/SalesDashboard/index.tsx`

**改动**：

* 移除第 272 行的 `{isDataReady && (`

* 移除对应的闭合 `)}`

* 在数据未就绪时显示 Loading，而不是返回空白

```tsx
// 当前（第 267-273 行）
const isDataReady = currentUser && Object.keys(access).length > 0;

return (
  <PageContainer title={formatMessage({ id: 'salesDashboard.title' })}>
    {isDataReady && (
      <div>
        // 所有内容
      </div>
    )}
  </PageContainer>
);

// 修改为
const isDataReady = currentUser && Object.keys(access).length > 0;

// 如果数据未就绪，显示 Loading
if (!isDataReady) {
  return (
    <PageContainer title={formatMessage({ id: 'salesDashboard.title' })}>
      <div style={{ 
        display: 'flex', 
        justifyContent: 'center', 
        alignItems: 'center', 
        height: '400px' 
      }}>
        <Spin size="large" tip="正在加载..." />
      </div>
    </PageContainer>
  );
}

return (
  <PageContainer title={formatMessage({ id: 'salesDashboard.title' })}>
    <div>
      // 所有内容（不再需要条件渲染）
    </div>
  </PageContainer>
);
```

### 方案 2：优化 GlobalStateGuard（辅助）

如果方案 1 不够，可以进一步优化 GlobalStateGuard 的轮询机制，减少不必要的重渲染。

## 为什么这样能解决问题

1. **避免缓存空白状态**：

   * 数据未就绪时返回 Loading UI（有内容）

   * KeepAlive 缓存的是 Loading 状态，而不是空白

   * 数据就绪后，组件重新渲染并更新缓存

2. **保持一致的渲染结构**：

   * 无论数据是否就绪，都返回完整的 `PageContainer`

   * 避免条件渲染导致的结构变化

3. **更好的用户体验**：

   * 用户看到 Loading 提示，而不是空白页面

   * 明确告知正在加载数据

## 预期效果

* 首次打开 SalesDashboard 时显示 Loading 状态

* 数据加载完成后正常显示内容

* 不再出现空白页面问题

* GlobalStateGuard 渲染次数不影响最终显示

