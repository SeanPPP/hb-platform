## 修复 TypeScript 类型错误

### 问题
`access({ currentUser })` 传入的对象缺少 `name` 属性，导致类型错误。

### 解决方案
先构建完整的 `finalState`，然后用它来生成 `access` 对象。

### 具体修改

**文件：** `src/app.ts`

**修改位置 1（正常流程，第127-135行）：**
```typescript
const finalState = {
  name: 'HB Platform',
  currentUser,
};

// 🔐 添加 access 对象供 menuDataRender 使用
const accessObj = access(finalState);

// 将状态也保存到全局变量（调试用）
(window as any).g_initialState = finalState;

return {
  ...finalState,
  access: accessObj,
};
```

**修改位置 2（localStorage 恢复流程，第146-153行）：**
```typescript
const finalState = {
  name: 'HB Platform',
  currentUser,
};

// 🔐 添加 access 对象供 menuDataRender 使用
const accessObj = access(finalState);

// 将状态也保存到全局变量（调试用）
(window as any).g_initialState = finalState;

return {
  ...finalState,
  access: accessObj,
};
```

### 预期效果
- TypeScript 类型错误消失
- `access` 函数接收完整的 `InitialState` 对象
- 权限过滤功能正常工作