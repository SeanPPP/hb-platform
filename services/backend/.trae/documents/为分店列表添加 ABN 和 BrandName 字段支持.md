## 修改方案

### 后端修改 (`BlazorApp.Api/Services/StoreService.cs`)

在以下方法的 `StoreDto` 映射中添加 `ABN = s.ABN` 和 `BrandName = s.BrandName`：
1. `GetAllStoresByNameAsync` (第 47-57 行)
2. `GetActiveStoresAsync` (第 85-93 行)
3. `GetStoresAsync` (第 211-227 行)
4. `GetStoreByCodeAsync` (第 306-316 行)
5. `UpdateStoreByGuidAsync` (第 377-389 行)
6. `CreateStoreAsync` (第 939-950 行)
7. `UpdateStoreAsync` (第 1003-1014 行)
8. `GetStoreDetailAsync` (第 848-857 行)

### 前端类型修改 (`ReactUmi/my-app/src/types/store.ts`)

在 `StoreDto` 接口中添加：
```typescript
/** 澳大利亚商业号码 */
abn?: string;
```

### 前端页面修改 (`ReactUmi/my-app/src/pages/StoreManagement/index.tsx`)

在表格列定义中，在 "品牌名称" 列后添加 "ABN" 列：
```typescript
{
  title: 'ABN',
  dataIndex: 'abn',
  key: 'abn',
  width: 150,
  ellipsis: true,
  search: false,
},
```