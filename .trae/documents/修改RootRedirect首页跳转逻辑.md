## 修改 RootRedirect 首页跳转逻辑

### 修改文件

**文件：** `src/pages/RootRedirect.tsx`

### 修改内容

**1. 导入 access 函数：**

```typescript
import access from '@/access';
```

**2. 修改跳转逻辑（第17-32行）：**

**修改前：**

```typescript
// Check roles
const roles = currentUser.roleNames || [];

// Logic: If Admin, go to System Dashboard (/home). Else, go to Store (/store).
const isAdmin = roles.some(r => 
  r.toLowerCase().includes('admin') || 
  r.toLowerCase() === 'system administrator' ||
  r.toLowerCase() === 'superadmin'
);

if (isAdmin) {
  history.replace('/home');
} else {
  history.replace('/store');
}
```

**修改后：**

```typescript
// 使用 access.ts 中的权限检查
const accessObj = access({ currentUser });

// Logic: If Admin, Manager, or User, go to System Dashboard (/home). Else, go to Store (/store).
if (accessObj.isAdmin || accessObj.isManager || accessObj.isUser) {
  history.replace('/home');
} else {
  history.replace('/store');
}
```

### 预期效果

✅ Admin → 跳转到 `/home`\
✅ Manager → 跳转到 `/home`\
✅ User → 跳转到 `/home`\
✅ 其他角色（如仓库管理员）→ 跳转到 `/store`\
✅ 使用统一的权限检查逻辑，避免重复代码
