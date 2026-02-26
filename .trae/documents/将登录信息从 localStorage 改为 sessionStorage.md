## 将登录信息从 localStorage 改为 sessionStorage

### 需求
使用 sessionStorage 存储登录信息，关闭浏览器后自动清除。

### 需要修改的文件

#### 1. `src/app.ts`
- 第124行：`localStorage.setItem('currentUser', ...)` → `sessionStorage.setItem('currentUser', ...)`
- 第147行：`localStorage.getItem('currentUser')` → `sessionStorage.getItem('currentUser')`
- 第269行：`localStorage.removeItem('currentUser')` → `sessionStorage.removeItem('currentUser')`

#### 2. `src/services/auth.ts`
- 第65-68行：`saveTokens` 函数中的所有 `localStorage` → `sessionStorage`
- 第75-79行：`clearTokens` 函数中的所有 `localStorage` → `sessionStorage`
- 第87行：`getAccessToken` 函数中的 `localStorage` → `sessionStorage`
- 第95行：`getRefreshToken` 函数中的 `localStorage` → `sessionStorage`
- 第103行：`isTokenExpired` 函数中的 `localStorage` → `sessionStorage`

### 修改内容
将所有 `localStorage` 替换为 `sessionStorage`，保持其他代码逻辑不变。

### 预期效果
- 登录信息存储在 sessionStorage 中
- 关闭浏览器标签页或窗口后自动清除登录信息
- 用户需要重新登录才能访问系统