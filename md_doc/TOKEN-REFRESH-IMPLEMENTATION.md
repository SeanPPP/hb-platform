# 长短Token无感刷新实现文档

## 架构概述

本项目实现了基于JWT的长短Token无感刷新机制，有效平衡了安全性和用户体验。

### Token类型
- **短Token (Access Token)**: 有效期15分钟，用于API调用认证
- **长Token (Refresh Token)**: 有效期7天，用于获取新的短Token

## 实现细节

### 1. 后端实现 (BlazorApp.Api)

#### 核心组件
- **AuthService**: 负责Token生成、验证和刷新
- **RefreshToken**: 数据库存储刷新令牌信息
- **AuthController**: 提供认证端点

#### 新增API端点
```
POST /api/auth/login     # 登录获取Token对
POST /api/auth/register  # 注册获取Token对  
POST /api/auth/refresh   # 刷新Token对
POST /api/auth/logout    # 登出撤销刷新令牌
```

#### 关键代码
```csharp
// 生成Token对
var accessToken = GenerateAccessToken(user, jwtSettings); // 15分钟
var refreshToken = GenerateRefreshToken(); // 7天
```

### 2. 前端实现 (BlazorApp)

#### 存储管理
- **TokenStorageService**: 负责localStorage中的Token存储
- **AuthServiceNew**: 新的认证服务，支持长短Token

#### 无感刷新机制
- **TokenHandler**: HTTP消息处理器，自动处理Token刷新
- **自动刷新**: 在Token过期前5分钟触发刷新

#### 使用方式
```csharp
// 在Program.cs中注册服务
builder.Services.AddAuthServices();

// 在组件中使用
@inject IAuthServiceNew AuthService

// 登录
var tokens = await AuthService.LoginAsync("username", "password");

// 自动刷新 - 无需手动处理
// TokenHandler会自动在后台处理所有HTTP请求的Token刷新
```

## 使用步骤

### 1. 部署后端
```bash
cd BlazorApp.Api
dotnet ef migrations add AddRefreshTokenTable
dotnet ef database update
dotnet run --urls "https://localhost:7001;http://localhost:5001"
```

### 2. 启动前端
```bash
cd BlazorApp
dotnet run --urls "https://localhost:7002;http://localhost:5002"
```

### 3. 测试流程

#### 登录获取Token
```bash
curl -X POST https://localhost:7001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"test","password":"password"}'
```

响应示例：
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "7f8a9b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0",
  "accessTokenExpiry": "2024-01-01T12:15:00Z",
  "refreshTokenExpiry": "2024-01-08T12:00:00Z",
  "success": true
}
```

#### 刷新Token
```bash
curl -X POST https://localhost:7001/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"accessToken":"expired_token","refreshToken":"valid_refresh_token"}'
```

#### 登出
```bash
curl -X POST https://localhost:7001/api/auth/logout \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"token_to_revoke"}'
```

## 安全考虑

### 1. Token存储
- **Access Token**: 存储在内存中，页面刷新后需重新获取
- **Refresh Token**: 存储在HttpOnly Cookie中（生产环境）

### 2. Token验证
- **签名验证**: 防止Token篡改
- **过期检查**: 严格的过期时间验证
- **撤销机制**: 支持手动撤销刷新令牌

### 3. 防护机制
- **IP绑定**: 可选的IP地址绑定
- **设备绑定**: User-Agent检查
- **频率限制**: 防止暴力刷新

## 无感刷新工作原理

1. **Token检查**: 每次HTTP请求前检查Token有效期
2. **自动刷新**: 在Token过期前5分钟自动调用刷新接口
3. **透明处理**: 用户无感知，所有API调用正常进行
4. **失败处理**: 刷新失败时自动跳转到登录页

## 错误处理

### 常见错误
- **401 Unauthorized**: Token无效或已过期
- **403 Forbidden**: 用户权限不足
- **400 Bad Request**: 请求参数错误

### 处理策略
- **Token过期**: 自动尝试刷新
- **刷新失败**: 清除本地Token，跳转到登录页
- **网络错误**: 显示友好错误提示

## 监控和日志

### 日志记录
- Token生成、刷新、撤销操作
- 认证失败详情
- 异常堆栈信息

### 监控指标
- Token刷新成功率
- Token过期频率
- 用户活跃会话数

## 最佳实践

### 开发环境
1. 使用SQLite数据库测试
2. 启用详细日志记录
3. 设置较短的Token有效期便于测试

### 生产环境
1. 使用SQL Server/PostgreSQL
2. 启用HTTPS
3. 配置更长的刷新Token有效期
4. 添加IP白名单和设备绑定

### Token生命周期管理
```
用户登录
    ↓
生成Token对
    ↓
存储到localStorage
    ↓
每次请求检查Token
    ↓
Token即将过期? → 自动刷新 → 保存新Token
    ↓
用户登出 → 撤销刷新Token → 清除本地存储
```

## 故障排除

### 常见问题
1. **Token刷新失败**: 检查网络连接和服务器状态
2. **401错误**: 验证Token是否正确
3. **CORS问题**: 检查后端CORS配置

### 调试工具
- 浏览器开发者工具
- Swagger UI: https://localhost:7001/swagger
- 日志查看: 后端控制台输出

## 性能优化

### 缓存策略
- Token缓存：减少重复验证
- 用户缓存：减少数据库查询

### 网络优化
- HTTP/2支持
- 压缩传输
- CDN加速（生产环境）

这套长短Token无感刷新机制提供了安全、流畅的用户体验，同时保持了系统的高可用性。