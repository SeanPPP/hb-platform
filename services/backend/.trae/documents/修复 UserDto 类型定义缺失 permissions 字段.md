## 修复类型错误方案

### 问题
前端 `UserDto` 类型定义缺少 `permissions` 字段，导致 TypeScript 编译错误。

### 解决方案
在 `src/types/auth.ts` 的 `UserDto` 接口中添加 `permissions` 字段。

### 具体修改
```typescript
export interface UserDto {
  userGUID: string;
  username: string;
  email: string;
  fullName?: string;
  phone?: string;
  lastLoginAt?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  currentStore?: string;
  roleNames: string[];
  storeNames: string[];
  roles?: RoleDto[];
  stores?: StoreDto[];
  permissions: string[];  // ✅ 添加此字段
}
```

### 预期效果
- TypeScript 编译错误消失
- 前端类型定义与后端 API 响应保持一致
- `app.ts` 中的 `userData.permissions` 访问不再报错