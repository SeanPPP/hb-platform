## 修复管理员看不到所有导航的问题

### 问题
`menuDataRender` 中无法获取 `access` 对象，导致所有权限检查失败。

### 解决方案
在 `getInitialState()` 中手动构建 `access` 对象并添加到返回的 `initialState` 中。

### 具体修改

**文件：** `src/app.ts`

**修改步骤：**

1. 导入 `access` 函数
2. 在 `getInitialState()` 中，构建 `finalState` 后，调用 `access` 函数生成权限对象
3. 将 `access` 对象添加到 `finalState` 中

**修改代码：**
```typescript
// 1. 导入 access 函数
import access from './access';

// 2. 在 getInitialState() 中
const finalState = {
  name: 'HB Platform',
  currentUser,
  access: access(finalState),  // ✅ 添加 access 对象
};
```

### 预期效果
- `menuDataRender` 可以正确获取 `access` 对象
- 管理员可以看到所有导航菜单
- 权限过滤功能正常工作