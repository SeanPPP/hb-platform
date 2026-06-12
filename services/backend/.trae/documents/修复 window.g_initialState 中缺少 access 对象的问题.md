## 修复 window.g_initialState 中缺少 access 对象的问题

### 问题
在 `app.ts` 中，`window.g_initialState` 被设置为 `finalState`，但是 `finalState` 中没有包含 `access` 对象。`access` 对象只在 `return` 语句中添加，但是 `window.g_initialState` 在 `return` 之前就被设置了。

这导致 `menuDataRender` 函数从 `window.g_initialState` 获取 `access` 对象时，得到的是 `undefined`，导致所有菜单项都被过滤掉。

### 解决方案
修改 `app.ts` 中的两处代码，将 `window.g_initialState` 设置为包含 `access` 对象的完整状态。

### 具体修改

**文件：** `src/app.ts`

**修改位置 1（第137行）：**
```typescript
// 修改前
(window as any).g_initialState = finalState;

return {
  ...finalState,
  access: accessObj,
};

// 修改后
const stateWithAccess = {
  ...finalState,
  access: accessObj,
};
(window as any).g_initialState = stateWithAccess;

return stateWithAccess;
```

**修改位置 2（第162行）：**
```typescript
// 修改前
(window as any).g_initialState = finalState;

return {
  ...finalState,
  access: accessObj,
};

// 修改后
const stateWithAccess = {
  ...finalState,
  access: accessObj,
};
(window as any).g_initialState = stateWithAccess;

return stateWithAccess;
```

### 预期效果
- `window.g_initialState` 中包含 `access` 对象
- `menuDataRender` 函数可以正确获取 `access` 对象
- 权限检查正常工作
- 管理员可以看到所有导航菜单