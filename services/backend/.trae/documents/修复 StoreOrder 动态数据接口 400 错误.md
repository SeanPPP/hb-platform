**原因分析**:
错误 `400 Bad Request` 是因为前端在调用 `dynamic-data` 接口时，`storeCode` 为空字符串。
后端接口定义中 `StoreOrderDynamicDataRequestDto.StoreCode` 标记了 `[Required]` 属性，因此拒绝了空值的请求。
这种情况发生在页面刚加载时，用户尚未选择分店，或者系统还在加载分店列表的过程中。

**修复方案**:
我们需要在前端 `ProductGrid.tsx` 中添加检查，确保仅在 `storeCode` 有效时才调用该接口。

1. **修改** **`ProductGrid.tsx`**:

   * 在 `fetchDynamicData` 函数中添加防卫语句：如果 `storeCode` 为空，直接返回，不发送请求。

   * 更新调用 `fetchDynamicData` 的 `useEffect` 依赖数组，加入 `storeCode`。这样当用户选择分店后，会自动触发数据刷新。

   * 当 `storeCode` 为空时（如清除选择），清空本地的 `dynamicData` 状态。

**代码变更预览**:

```typescript
  const fetchDynamicData = async () => {
    if (data.length === 0 || !storeCode) return; // 添加 !storeCode 检查
    // ...
  };

  useEffect(() => {
    if (storeCode) {
        fetchDynamicData();
    } else {
        setDynamicData({}); // 如果没有分店，清空动态数据
    }
  }, [data, storeCode]); // 添加 storeCode 依赖
```

