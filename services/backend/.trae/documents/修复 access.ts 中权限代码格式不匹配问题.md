## 修复权限代码格式不匹配问题

### 问题
后端权限代码使用大写开头（如 `Users.View`），但前端 `access.ts` 中的权限检查使用小写开头（如 `user.view`），导致权限检查失败，导航菜单被过滤掉。

### 解决方案
修改 `src/access.ts` 中的权限检查，将小写权限代码改为大写格式，以匹配后端的权限代码。

### 具体修改

**文件：** `src/access.ts`

**需要修改的权限检查：**

1. **用户管理权限（第65-70行）：**
   - `hasPermission('user.view')` → `hasPermission('Users.View')`
   - `hasPermission('user.create')` → `hasPermission('Users.Create')`
   - `hasPermission('user.edit')` → `hasPermission('Users.Edit')`
   - `hasPermission('user.delete')` → `hasPermission('Users.Delete')`
   - `hasPermission('user.manage_roles')` → `hasPermission('Users.ManageRoles')`
   - `hasPermission('user.manage_stores')` → `hasPermission('Users.ManageStores')`
   - `hasPermission('user.reset_password')` → `hasPermission('Users.ResetPassword')`

2. **角色管理权限（第73-77行）：**
   - `hasPermission('role.view')` → `hasPermission('Roles.View')`
   - `hasPermission('role.create')` → `hasPermission('Roles.Create')`
   - `hasPermission('role.edit')` → `hasPermission('Roles.Edit')`
   - `hasPermission('role.delete')` → `hasPermission('Roles.Delete')`
   - `hasPermission('role.manage_permissions')` → `hasPermission('Roles.ManagePermissions')`
   - `hasPermission('role.manage_users')` → `hasPermission('Roles.ManageUsers')`

3. **分店管理权限（第80-84行）：**
   - `hasPermission('store.view')` → `hasPermission('Stores.View')`
   - `hasPermission('store.create')` → `hasPermission('Stores.Create')`
   - `hasPermission('store.edit')` → `hasPermission('Stores.Edit')`
   - `hasPermission('store.delete')` → `hasPermission('Stores.Delete')`
   - `hasPermission('store.manage_users')` → `hasPermission('Stores.ManageUsers')`
   - `hasPermission('store.sync')` → `hasPermission('Stores.Sync')`

4. **系统管理权限（第87-90行）：**
   - `hasPermission('system.view_logs')` → `hasPermission('System.ViewLogs')`
   - `hasPermission('system.manage_settings')` → `hasPermission('System.ManageSettings')`
   - `hasPermission('system.backup')` → `hasPermission('System.Backup')`
   - `hasPermission('system.restore')` → `hasPermission('System.Restore')`

### 预期效果
- 权限检查与后端权限代码格式一致
- 管理员可以看到所有导航菜单
- 普通用户只能看到有权限的菜单
- 菜单权限过滤功能正常工作