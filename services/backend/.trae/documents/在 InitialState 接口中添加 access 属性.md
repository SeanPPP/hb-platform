## 修复 InitialState 类型定义缺少 access 属性

### 问题
`InitialState` 接口定义中没有 `access` 属性，导致类型错误。

### 解决方案
在 `InitialState` 接口中添加 `access` 属性。

### 具体修改

**文件：** `src/app.ts`

**修改位置：** 第44-50行

**修改内容：**
```typescript
export interface InitialState {
  name: string;
  currentUser?: CurrentUser;
  loading?: boolean;
  selectedStore?: UserStoreDto | null;
  userStores?: UserStoreDto[];
  access?: any;  // ✅ 添加 access 属性
}
```

### 预期效果
- TypeScript 类型错误消失
- `access` 属性可以在 `InitialState` 中使用
- `menuDataRender` 可以正确获取权限信息