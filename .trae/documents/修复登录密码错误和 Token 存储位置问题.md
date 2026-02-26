## 修复登录和导航显示问题

### 问题 1：登录密码错误
**原因：** 数据库中 admin 用户的密码是 `admin`，不是 `password123`

**解决方案：** 告诉用户使用正确的密码 `admin`

### 问题 2：Token 存储位置错误
**原因：** `request.ts` 中仍然使用 `localStorage` 获取 token，但我们已经将所有存储改为 `sessionStorage`

**解决方案：** 修改 `src/utils/request.ts` 中的所有 `localStorage` 为 `sessionStorage`

### 具体修改

**文件：** `src/utils/request.ts`

**需要修改的位置：**
1. 第52行：`localStorage.getItem('accessToken')` → `sessionStorage.getItem('accessToken')`
2. 第119行：`localStorage.getItem('refreshToken')` → `sessionStorage.getItem('refreshToken')`
3. 第132行：`localStorage.getItem('accessToken')` → `sessionStorage.getItem('accessToken')`
4. 第142-150行：所有 `localStorage.setItem(...)` → `sessionStorage.setItem(...)`
5. 第170-174行：所有 `localStorage.removeItem(...)` → `sessionStorage.removeItem(...)`

### 预期效果
- 用户可以使用正确的密码 `admin` 登录
- Token 正确存储在 `sessionStorage` 中
- 登录后可以正常获取用户信息
- 管理员可以看到所有导航菜单