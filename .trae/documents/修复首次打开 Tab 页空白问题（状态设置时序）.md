## 修复首次打开 Tab 页空白问题（状态设置时序）

### 根本原因
`finally` 块中的 `setLoading(false)` 会在数据状态设置之前执行，导致组件提前渲染空白状态。

### 解决方案

#### 修改 1：移除 setTimeout 强制渲染
**文件**: `src/pages/SalesDashboard/index.tsx`

移除 `setTimeout(() => { setLoading(false); }, 0)`，这是不必要的 hack，会导致状态更新混乱。

#### 修改 2：移除 finally 中的 setLoading(false)
**文件**: `src/pages/SalesDashboard/index.tsx`

`finally` 块无论如何都会执行（包括异常情况），但我们应该只在数据成功设置后才关闭 loading。

#### 修改 3：在数据设置完成后统一设置 loading
**文件**: `src/pages/SalesDashboard/index.tsx`

在所有状态设置完成后，统一设置 `loading = false`，确保状态更新顺序正确。

```typescript
// 在所有 if 语句之后
setLoading(false);
```

---

### 修改后的代码结构

```typescript
try {
  const [summaryRes, hourlyRes, storeRes, supplierRes, chinaSupplierRes] =
    await Promise.all([...]);

  if (summaryRes.success) {
    setDashboardSummary(summaryRes.data ?? null);
  }
  // ... 其他数据设置

  // 统一设置 loading = false
  setLoading(false);
} catch (error) {
  message.error(formatMessage({ id: 'error.unknown' }));
  console.error(error);
  setLoading(false); // 异常时也关闭 loading
}
```

### 预期效果
- ✅ 数据状态设置完成后才更新 `loading`
- ✅ 确保组件在数据就绪后才渲染
- ✅ 首次打开 Tab 页不再空白