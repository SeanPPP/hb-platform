# JWT令牌使用指南

## 🔑 概述

JWT（JSON Web Token）是一种用于身份验证和授权的标准令牌格式。在HB Platform多店铺管理系统中，JWT用于保护API端点，确保只有经过认证的用户才能访问受保护的资源。

## 📋 令牌结构

JWT令牌由三部分组成，用点（.）分隔：
```
Header.Payload.Signature
```

### 示例令牌
```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
```

## 🚀 获取令牌

### 1. 用户登录
```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "your-password"
}
```

### 2. 响应示例
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "refresh_token_here",
    "accessTokenExpiry": "2024-01-01T12:00:00Z",
    "refreshTokenExpiry": "2024-01-08T12:00:00Z"
  },
  "message": "登录成功"
}
```

## 🔐 使用令牌

### 在Swagger UI中测试API

1. **打开Swagger页面**
   - 访问：`https://localhost:7001/swagger`

2. **点击Authorize按钮**
   - 在页面右上角找到"Authorize"按钮

3. **输入令牌**
   ```
   Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
   ```

   ⚠️ **重要格式要求**：
   - 必须包含 `Bearer` 前缀
   - 必须有一个空格分隔 `Bearer` 和令牌
   - 不要包含大括号 `{}`
   - 令牌值直接粘贴，不要额外处理

4. **点击Authorize**
   - 确认授权设置

5. **测试API端点**
   - 现在可以访问需要认证的API端点

### 在HTTP请求中使用

```http
GET /api/stores
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json
```

### 在JavaScript中使用

```javascript
// 设置请求头
const headers = {
  'Authorization': `Bearer ${accessToken}`,
  'Content-Type': 'application/json'
};

// 发送请求
fetch('/api/stores', {
  method: 'GET',
  headers: headers
});
```

## 🔄 令牌刷新

当访问令牌过期时，可以使用刷新令牌获取新的令牌对：

```http
POST /api/auth/refresh
Content-Type: application/json

{
  "accessToken": "expired_access_token",
  "refreshToken": "valid_refresh_token"
}
```

## 🚫 常见错误

### 1. 401 Unauthorized
- **原因**：令牌无效、过期或格式错误
- **解决**：重新登录获取新令牌

### 2. "The signature key was not found"
- **原因**：令牌签名验证失败
- **解决**：检查令牌格式，确保使用正确的Bearer格式

### 3. 403 Forbidden
- **原因**：用户权限不足
- **解决**：联系管理员分配适当角色

## 📝 令牌内容

JWT令牌包含以下信息：

### 标准声明
- `sub`：用户唯一标识符
- `name`：用户名
- `email`：用户邮箱
- `exp`：过期时间
- `iat`：签发时间

### 自定义声明
- `uid`：用户GUID
- `userId`：数据库用户ID
- `role`：用户角色（Admin、Manager等）

## 🔒 安全建议

1. **令牌存储**
   - 前端：存储在内存或安全的本地存储
   - 不要存储在localStorage（容易被XSS攻击）

2. **令牌传输**
   - 始终使用HTTPS
   - 在请求头中传输，不要在URL中

3. **令牌过期**
   - 访问令牌：15分钟
   - 刷新令牌：7天
   - 及时刷新过期令牌

4. **令牌撤销**
   - 登出时调用撤销接口
   - 定期清理过期的刷新令牌

## 🛠️ 调试技巧

### 1. 解码令牌
使用在线工具（如jwt.io）解码令牌内容：
```
https://jwt.io/
```

### 2. 检查令牌格式
确保令牌格式正确：
```
Bearer [space] [token]
```

### 3. 验证令牌有效性
```bash
# 使用curl测试
curl -H "Authorization: Bearer YOUR_TOKEN" \
     -H "Content-Type: application/json" \
     https://localhost:7001/api/test/authenticated
```

## 📞 技术支持

如果遇到JWT相关问题，请检查：

1. 令牌格式是否正确
2. 令牌是否过期
3. 用户角色是否正确分配
4. 网络连接是否正常

联系技术支持时，请提供：
- 错误信息
- 令牌格式（隐藏敏感部分）
- 请求的API端点
- 浏览器控制台错误信息 