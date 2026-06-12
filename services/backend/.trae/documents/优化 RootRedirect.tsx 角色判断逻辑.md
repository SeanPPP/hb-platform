## 优化 RootRedirect.tsx 角色判断逻辑

### 修改文件
- `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\RootRedirect.tsx`

### 改动内容
1. 引入 `useAccess` hook
2. 使用 `access.isAdmin` 替代模糊匹配逻辑
3. 实现明确的路由映射：
   - Admin → /home
   - Manager/User/其他角色 → /store
4. 移除 `includes('admin')` 等模糊匹配代码

### 路由映射规则
- `isAdmin` → `/home` （后台管理）
- `isManager` → `/store` （商店前台）
- `isUser` → `/store` （商店前台）
- 其他角色 → `/store` （商店前台）

### 预期效果
- 角色判断逻辑与 access.ts 保持一致
- 支持中英文角色名（Admin/管理员）
- 代码更简洁、可维护性更高