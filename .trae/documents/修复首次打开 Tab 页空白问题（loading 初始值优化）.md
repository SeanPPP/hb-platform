## 修复首次打开 Tab 页空白问题

### 根本原因
`useEffect` 在首次渲染后执行，导致 `loading` 初始值为 `false`，页面在数据加载前就渲染了空白状态。

### 解决方案

#### 修改 1：调整 `loading` 初始值
**文件**: `src/pages/SalesDashboard/index.tsx`

将 `loading` 初始值从 `false` 改为 `true`，确保首次渲染时就显示加载状态。

```typescript
// 修改前
const [loading, setLoading] = useState(false);

// 修改后
const [loading, setLoading] = useState(true);
```

#### 修改 2：优化 `useEffect` 触发逻辑
**文件**: `src/pages/SalesDashboard/index.tsx`

在 `useEffect` 中增加 `ready` 状态检查，确保在全局状态完全就绪后再加载数据。

```typescript
const accessReady = Object.keys(access).length > 0;

useEffect(() => {
  // 只有在 currentUser 和 access 都就绪后才加载数据
  if (!currentUser || !accessReady) {
    console.log('⏳ SalesDashboard: 等待数据就绪...', { currentUser: !!currentUser, accessReady });
    return;
  }
  
  console.log('🔄 SalesDashboard: 开始加载数据...');
  loadDashboardData(dateRange);
}, [
  currentUser,
  accessReady,  // ⭐ 添加 access 状态依赖
  dateRange,
  selectedSupplier,
  selectedChinaSupplier,
  selectedBranchCode,
  selectedBranchName,
]);
```

#### 修改 3：移除调试代码
**文件**: `src/pages/SalesDashboard/index.tsx`

移除第 302-304 行的调试框，因为问题已解决。

---

### 预期效果
- ✅ 首次打开 Tab 时立即显示加载状态（不再空白）
- ✅ `useEffect` 在数据就绪后执行，加载仪表板数据
- ✅ 数据加载完成后正常显示