## 修复计划

### 1. 修复 `useRef` 初始化 (第 24 行)
- 将 `const actionRef = useRef<ActionType>();` 
- 改为 `const actionRef = useRef<ActionType | null>(null);`

### 2. 修复 `sort` 参数处理逻辑 (第 254-256 行)
- 将 `sort` 从对象格式转换为数组格式
- 遍历 `sort` 对象的键值对
- 过滤 `null` 值
- 构造 `[{ ColId: field, Sort: order }]` 格式

### 具体修改：
```typescript
SortModel: sort 
  ? Object.entries(sort)
      .filter(([_, order]) => order !== null)
      .map(([field, order]) => ({ ColId: field, Sort: order }))
  : null,
```

这样可以正确处理 ProTable 的排序对象，并生成后端 API 期望的数组格式。