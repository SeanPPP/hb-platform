# Cookie 认证方案使用指南

## 概述

本项目现在支持通过 Cookie 存储 JWT 令牌，提供与传统 Authorization Header 认证方案并行的 Cookie 认证支持。

## 已实现的功能

### 1. CookieHelper 辅助类

位置：`backend/BlazorApp.Api/Utils/CookieHelper.cs`

提供以下静态方法：

#### 读取令牌
```csharp
// 从 Cookie 读取访问令牌
string? accessToken = CookieHelper.GetAccessToken(HttpContext context);

// 从 Cookie 读取刷新令牌
string? refreshToken = CookieHelper.GetRefreshToken(HttpContext context);

// 一次性获取两个令牌
var (accessToken, refreshToken) = CookieHelper.GetTokens(HttpContext context);

// 检查是否存在任何令牌（Cookie 或 Header）
bool hasToken = CookieHelper.HasAnyToken(HttpContext context);
```

#### 设置令牌
```csharp
// 设置访问令牌到 Cookie
CookieHelper.SetAccessToken(HttpResponse response, "your-access-token");

// 设置刷新令牌到 Cookie
CookieHelper.SetRefreshToken(HttpResponse response, "your-refresh-token");

// 一次性设置两个令牌
CookieHelper.SetTokens(HttpResponse response, "accessToken", "refreshToken");
```

#### 清除令牌
```csharp
// 清除所有认证相关的 Cookie（用于登出）
CookieHelper.ClearAuthCookies(HttpResponse response);
```

### 2. AuthService 扩展

位置：`backend/BlazorApp.Api/Services/AuthService.cs`

新增方法：

```csharp
public interface IAuthService
{
    // 原有方法（支持 Authorization Header 方案）
    Task<TokenResponse?> RefreshTokensAsync(
        string accessToken,
        string refreshToken,
        string ipAddress,
        string userAgent
    );

    // 新增方法（支持 Cookie 认证方案）
    Task<TokenResponse?> RefreshTokensAsync(
        HttpContext context,
        string ipAddress,
        string userAgent
    );
}
```

## Controller 使用示例

### 示例 1：登录时设置 Cookie

```csharp
[HttpPost("login-with-cookies")]
public async Task<ActionResult<ApiResponse<LoginResponse>>> LoginWithCookies([FromBody] LoginRequest request)
{
    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var userAgent = Request.Headers["User-Agent"].ToString();

    // 1. 验证用户凭据
    var loginResponse = await _authService.LoginAsync(request);
    if (!loginResponse.Success)
    {
        return BadRequest(ApiResponse<LoginResponse>.Error("登录失败", "LOGIN_FAILED"));
    }

    // 2. 生成令牌对
    var user = await _userService.GetUserByUsernameAsync(request.Username);
    var tokenResponse = await _authService.GenerateTokensAsync(user, ipAddress, userAgent);

    // 3. 将令牌存储到 Cookie
    CookieHelper.SetTokens(Response, tokenResponse.AccessToken, tokenResponse.RefreshToken);

    // 4. 返回响应（也可以选择不返回令牌，因为已在 Cookie 中）
    return Ok(ApiResponse<LoginResponse>.OK(loginResponse, "登录成功"));
}
```

### 示例 2：从 Cookie 刷新令牌

```csharp
[HttpPost("refresh-with-cookies")]
public async Task<ActionResult<ApiResponse<TokenResponse>>> RefreshWithCookies()
{
    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var userAgent = Request.Headers["User-Agent"].ToString();

    // 从 Cookie 刷新令牌
    var tokenResponse = await _authService.RefreshTokensAsync(HttpContext, ipAddress, userAgent);

    if (tokenResponse == null || !tokenResponse.Success)
    {
        return Unauthorized(ApiResponse<TokenResponse>.Error("令牌刷新失败", "REFRESH_FAILED"));
    }

    // 将新令牌更新到 Cookie
    CookieHelper.SetTokens(Response, tokenResponse.AccessToken, tokenResponse.RefreshToken);

    return Ok(ApiResponse<TokenResponse>.OK(tokenResponse, "令牌刷新成功"));
}
```

### 示例 3：登出时清除 Cookie

```csharp
[HttpPost("logout-with-cookies")]
public async Task<ActionResult<ApiResponse<object>>> LogoutWithCookies()
{
    // 1. 从 Cookie 读取刷新令牌
    var refreshToken = CookieHelper.GetRefreshToken(HttpContext);

    if (!string.IsNullOrEmpty(refreshToken))
    {
        // 2. 撤销刷新令牌
        await _authService.RevokeRefreshTokenAsync(refreshToken);
    }

    // 3. 清除 Cookie
    CookieHelper.ClearAuthCookies(Response);

    return Ok(ApiResponse<object>.CreateSuccess("登出成功"));
}
```

### 示例 4：混合认证方案（支持 Cookie 和 Header）

```csharp
[Authorize]
[HttpGet("protected-endpoint")]
public async Task<ActionResult<ApiResponse<UserInfo>>> GetUserInfo()
{
    // 优先从 Cookie 获取令牌，如果没有则从 Header 获取
    var accessToken = CookieHelper.GetAccessToken(HttpContext)
        ?? Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

    if (string.IsNullOrEmpty(accessToken))
    {
        return Unauthorized(ApiResponse<UserInfo>.Error("未提供认证令牌", "NO_TOKEN"));
    }

    // 使用令牌获取用户信息
    var userGuid = User.FindFirst("userGuid")?.Value;
    var user = await _userService.GetUserByGuidAsync(userGuid);

    return Ok(ApiResponse<UserInfo>.OK(user, "获取用户信息成功"));
}
```

### 示例 5：手动读取 Cookie 中的令牌（不使用 AuthService）

```csharp
[HttpPost("custom-refresh")]
public async Task<ActionResult<ApiResponse<TokenResponse>>> CustomRefresh()
{
    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var userAgent = Request.Headers["User-Agent"].ToString();

    // 手动从 Cookie 读取令牌
    var accessToken = CookieHelper.GetAccessToken(HttpContext);
    var refreshToken = CookieHelper.GetRefreshToken(HttpContext);

    // 使用原有的 RefreshTokensAsync 方法
    var tokenResponse = await _authService.RefreshTokensAsync(
        accessToken ?? string.Empty,
        refreshToken ?? string.Empty,
        ipAddress,
        userAgent
    );

    if (tokenResponse == null || !tokenResponse.Success)
    {
        return Unauthorized(ApiResponse<TokenResponse>.Error("令牌刷新失败", "REFRESH_FAILED"));
    }

    // 更新 Cookie
    CookieHelper.SetTokens(Response, tokenResponse.AccessToken, tokenResponse.RefreshToken);

    return Ok(ApiResponse<TokenResponse>.OK(tokenResponse, "令牌刷新成功"));
}
```

## Cookie 配置

Cookie 配置通过 `CookieOptionsHelper` 类管理，配置项在 `appsettings.json` 中：

```json
{
  "Cookie": {
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7,
    "Secure": true,
    "SameSite": "Strict",
    "Path": "/",
    "Domain": ""
  }
}
```

### Cookie 选项说明

- **AccessTokenExpiryMinutes**: 访问令牌过期时间（分钟）
- **RefreshTokenExpiryDays**: 刷新令牌过期时间（天）
- **Secure**: 是否仅通过 HTTPS 传输
- **SameSite**: CSRF 保护策略（Strict/Lax/None）
- **Path**: Cookie 有效路径
- **Domain**: Cookie 有效域名

## 安全注意事项

1. **HTTPS**: 生产环境必须使用 HTTPS，设置 `Secure: true`
2. **HttpOnly**: Cookie 已设置为 HttpOnly，防止 XSS 攻击
3. **SameSite**: 设置为 Strict 或 Lax 防止 CSRF 攻击
4. **令牌过期**: 访问令牌有效期短（15分钟），刷新令牌有效期长（7天）
5. **登出处理**: 必须调用 `RevokeRefreshTokenAsync` 和 `ClearAuthCookies`

## 前端集成

前端可以使用标准的 Cookie API 或使用 Axios 的 `withCredentials` 选项：

### 使用 Fetch API

```javascript
// 自动发送 Cookie
fetch('/api/auth/refresh-with-cookies', {
  method: 'POST',
  credentials: 'include', // 包含 Cookie
  headers: {
    'Content-Type': 'application/json'
  }
});
```

### 使用 Axios

```javascript
// 配置全局设置
axios.defaults.withCredentials = true;

// 或在单个请求中
axios.post('/api/auth/refresh-with-cookies', null, {
  withCredentials: true
});
```

## 故障排查

### Cookie 未被发送

1. 检查 `SameSite` 设置是否正确
2. 确认使用 HTTPS（如果 `Secure: true`）
3. 检查域名和路径配置
4. 确保前端设置了 `credentials: 'include'`

### 令牌刷新失败

1. 检查 Cookie 是否已过期
2. 确认刷新令牌是否在数据库中存在
3. 检查 `IHttpContextAccessor` 是否已正确注入

### 编译错误

1. 确保已添加 `using Microsoft.AspNetCore.Http;` 引用
2. 确保在 Program.cs 中已注册 `IHttpContextAccessor`：
   ```csharp
   builder.Services.AddHttpContextAccessor();
   ```

## 迁移指南

从 Authorization Header 方案迁移到 Cookie 方案：

1. **登录端点**：添加 `SetTokens` 调用
2. **刷新端点**：使用新的 `RefreshTokensAsync(HttpContext, ...)` 重载
3. **登出端点**：添加 `ClearAuthCookies` 调用
4. **前端**：添加 `credentials: 'include'` 配置

## 相关文件

- `backend/BlazorApp.Api/Utils/CookieHelper.cs` - Cookie 辅助方法
- `backend/BlazorApp.Api/Utils/CookieOptionsHelper.cs` - Cookie 配置管理
- `backend/BlazorApp.Api/Services/AuthService.cs` - 认证服务
- `backend/BlazorApp.Api/Models/CookieSettings.cs` - Cookie 配置模型
- `backend/BlazorApp.Api/Program.cs` - 服务注册
