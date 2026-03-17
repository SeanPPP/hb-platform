using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 认证控制器 - 处理用户登录、注册、令牌刷新和登出功能
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        /// <summary>
        /// 认证服务 - 处理用户认证相关业务逻辑
        /// </summary>
        private readonly IAuthService _authService;

        /// <summary>
        /// 数据库上下文 - 用于直接数据库操作
        /// </summary>
        private readonly SqlSugarContext _dbContext;

        /// <summary>
        /// 角色服务 - 用于查询用户权限
        /// </summary>
        private readonly IRoleService _roleService;

        /// <summary>
        /// 构造函数 - 依赖注入认证服务、数据库上下文和角色服务
        /// </summary>
        /// <param name="authService">认证服务</param>
        /// <param name="dbContext">数据库上下文</param>
        /// <param name="roleService">角色服务</param>
        public AuthController(
            IAuthService authService,
            SqlSugarContext dbContext,
            IRoleService roleService
        )
        {
            _authService = authService;
            _dbContext = dbContext;
            _roleService = roleService;
        }

        /// <summary>
        /// 用户登录接口
        /// </summary>
        /// <param name="request">登录请求参数，包含用户名和密码</param>
        /// <returns>登录结果，包含访问令牌和刷新令牌</returns>
        /// <remarks>
        /// 登录流程：
        /// 1. 验证请求数据格式
        /// 2. 调用认证服务验证用户名密码
        /// 3. 获取用户IP地址和用户代理信息
        /// 4. 查询用户完整信息（包含角色）
        /// 5. 生成JWT令牌和刷新令牌
        /// 6. 返回令牌响应
        ///
        /// 🔑 JWT令牌使用说明：
        /// - 在Swagger中测试API时，点击"Authorize"按钮
        /// - 输入格式：Bearer + 空格 + 令牌值
        /// - 示例：Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
        /// - 注意：必须包含"Bearer"前缀和空格，不能使用大括号{}
        /// </remarks>
        [HttpPost("login")]
        [AllowAnonymous] // 🔓 允许匿名访问，登录不需要认证
        public async Task<ApiResponse<TokenResponse>> Login([FromBody] LoginRequest request)
        {
            // 验证请求模型状态
            if (!ModelState.IsValid)
            {
                return ApiResponse<TokenResponse>.Error("请求数据格式无效");
            }

            // 调用认证服务进行登录验证
            var loginResponse = await _authService.LoginAsync(request);
            if (!loginResponse.Success)
            {
                return ApiResponse<TokenResponse>.Error("用户名或密码错误");
            }

            // 获取客户端IP地址和用户代理信息（用于安全审计）
            var ipAddress =
                Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var userAgent = Request.Headers["User-Agent"].ToString();


            // 根据用户名获取完整用户对象（包含角色信息，用户名大小写不敏感）
            var usernameLower = (request.Username ?? string.Empty).Trim().ToLower();
            var userList = await _dbContext
                .Db.Queryable<User>()
                .Includes(u => u.Roles)
                .Where(u => u.Username.ToLower() == usernameLower)
                .Take(1)
                .ToListAsync();
            var user = userList.FirstOrDefault();

            if (user == null)
            {
                return ApiResponse<TokenResponse>.Error("用户名或密码错误");
            }

            // 生成JWT访问令牌和刷新令牌
            var tokenResponse = await _authService.GenerateTokensAsync(user, ipAddress, userAgent);

            // 🍪 将令牌存储到 Cookie 中
            Response.Cookies.Append(
                "access_token",
                tokenResponse.AccessToken,
                CookieOptionsHelper.CreateAccessTokenCookieOptions()
            );

            Response.Cookies.Append(
                "refresh_token",
                tokenResponse.RefreshToken,
                CookieOptionsHelper.CreateRefreshTokenCookieOptions()
            );

            return ApiResponse<TokenResponse>.OK(tokenResponse, "登录成功");
        }

        /// <summary>
        /// 刷新访问令牌接口
        /// </summary>
        /// <param name="request">刷新令牌请求，包含当前访问令牌和刷新令牌（可选，向后兼容）</param>
        /// <returns>新的访问令牌和刷新令牌</returns>
        /// <remarks>
        /// 令牌刷新流程：
        /// 1. 优先从 Cookie 读取 accessToken 和 refreshToken（如果存在）
        /// 2. 如果 Cookie 中没有令牌，则从请求体参数读取（向后兼容）
        /// 3. 获取客户端IP地址和用户代理信息
        /// 4. 验证当前访问令牌和刷新令牌的有效性
        /// 5. 生成新的访问令牌和刷新令牌
        /// 6. 更新 Cookie 中的令牌
        /// 7. 返回新的令牌对
        ///
        /// 支持两种令牌传递方式：
        /// - Cookie 认证：令牌自动从 Cookie 读取（推荐）
        /// - 请求体认证：在请求体中显式传递 accessToken 和 refreshToken（向后兼容）
        /// </remarks>
        [HttpPost("refresh")]
        [AllowAnonymous] // 🔓 允许匿名访问，令牌刷新不需要认证
        public async Task<ApiResponse<TokenResponse>> RefreshToken(
            [FromBody] RefreshTokenRequest request
        )
        {
            // 获取客户端IP地址和用户代理信息（用于安全审计）
            var ipAddress =
                Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var userAgent = Request.Headers["User-Agent"].ToString();

            // 🍪 优先从 Cookie 中读取令牌（支持 Cookie 认证方案）
            var cookieAccessToken = CookieHelper.GetAccessToken(Request.HttpContext);
            var cookieRefreshToken = CookieHelper.GetRefreshToken(Request.HttpContext);

            // 📦 如果 Cookie 中存在令牌，使用 Cookie 中的令牌
            if (!string.IsNullOrEmpty(cookieAccessToken) || !string.IsNullOrEmpty(cookieRefreshToken))
            {
                var tokenResponse = await _authService.RefreshTokensAsync(
                    Request.HttpContext,
                    ipAddress,
                    userAgent
                );

                if (tokenResponse == null)
                {
                    return ApiResponse<TokenResponse>.Error("刷新令牌无效或已过期");
                }

                // 🍪 更新 Cookie 中的令牌
                CookieHelper.SetTokens(Response, tokenResponse.AccessToken, tokenResponse.RefreshToken);

                return ApiResponse<TokenResponse>.OK(tokenResponse, "令牌刷新成功");
            }

            // 🔙 向后兼容：如果 Cookie 中没有令牌，从请求体读取（支持传统的请求体认证方案）
            var accessToken = request?.AccessToken ?? string.Empty;
            var refreshToken = request?.RefreshToken ?? string.Empty;

            // 调用认证服务刷新令牌
            var tokenResponseFromBody = await _authService.RefreshTokensAsync(
                accessToken,
                refreshToken,
                ipAddress,
                userAgent
            );

            if (tokenResponseFromBody == null)
            {
                return ApiResponse<TokenResponse>.Error("刷新令牌无效或已过期");
            }

            // 🍪 更新 Cookie 中的令牌
            CookieHelper.SetTokens(
                Response,
                tokenResponseFromBody.AccessToken,
                tokenResponseFromBody.RefreshToken
            );

            return ApiResponse<TokenResponse>.OK(tokenResponseFromBody, "令牌刷新成功");
        }

        /// <summary>
        /// 获取当前用户信息接口
        /// </summary>
        /// <returns>当前登录用户的详细信息</returns>
        [HttpGet("current")]
        [Authorize] // 🔐 需要认证才能访问
        public async Task<ApiResponse<UserDto>> GetCurrentUser()
        {
            try
            {
                // 从JWT令牌中获取用户ID
                var userIdClaim = User.FindFirst("userId") ?? User.FindFirst("sub");
                if (userIdClaim == null)
                {
                    return ApiResponse<UserDto>.Error("无法获取用户信息");
                }

                var userId = userIdClaim.Value;

                // 查询用户完整信息（包含角色信息）
                var user = await _dbContext
                    .Db.Queryable<User>()
                    .Includes(u => u.Roles) // 包含角色信息
                    .FirstAsync(u => u.UserGUID == userId);

                if (user == null)
                {
                    return ApiResponse<UserDto>.Error("用户不存在");
                }

                // 🔐 获取用户所有权限（从所有角色中聚合）
                var allPermissions = new List<string>();
                if (user.Roles != null && user.Roles.Any())
                {
                    foreach (var role in user.Roles)
                    {
                        // 查询该角色的权限
                        var rolePermissionsResult = await _roleService.GetRolePermissionsAsync(
                            role.RoleGUID
                        );
                        if (rolePermissionsResult.Success && rolePermissionsResult.Data != null)
                        {
                            allPermissions.AddRange(rolePermissionsResult.Data);
                        }
                    }
                }

                // 构建返回数据
                var userDto = new UserDto
                {
                    UserGUID = user.UserGUID,
                    Username = user.Username,
                    Email = user.Email,
                    FullName = user.FullName,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt ?? user.CreatedAt,
                    Roles =
                        user.Roles?.Select(r => new RoleDto
                        {
                            RoleGUID = r.RoleGUID,
                            RoleName = r.RoleName,
                            Description = r.Description,
                            IsActive = r.IsActive,
                            CreatedAt = r.CreatedAt,
                            UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                            UserCount = 0, // 这里不需要计算用户数
                        })
                            .ToList() ?? new List<RoleDto>(),
                    RoleNames = user.Roles?.Select(r => r.RoleName).ToList() ?? new List<string>(),
                    Permissions = allPermissions.Distinct().ToList(), // 🔐 添加权限列表（去重）
                    StoreNames = new List<string>(), // TODO: 从用户分店关联表获取
                };

                return ApiResponse<UserDto>.OK(userDto, "获取用户信息成功");
            }
            catch (Exception ex)
            {
                return ApiResponse<UserDto>.Error($"获取用户信息失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 用户登出接口
        /// </summary>
        /// <param name="request">登出请求，包含要撤销的刷新令牌</param>
        /// <returns>登出操作结果</returns>
        /// <remarks>
        /// 登出流程：
        /// 1. 验证刷新令牌是否存在
        /// 2. 调用认证服务撤销刷新令牌（使其失效）
        /// 3. 清除 Cookie 中的访问令牌和刷新令牌
        /// 4. 返回登出成功响应
        ///
        /// 注意：此操作会使当前的刷新令牌失效，用户需要重新登录才能获取新的令牌
        /// </remarks>
        [HttpPost("logout")]
        public async Task<ApiResponse<object>> Logout([FromBody] RefreshTokenRequest request)
        {
            // 如果提供了刷新令牌，则撤销该令牌
            if (!string.IsNullOrEmpty(request.RefreshToken))
            {
                await _authService.RevokeRefreshTokenAsync(request.RefreshToken);
            }

            // 🍪 清除 Cookie 中的令牌
            Response.Cookies.Append(
                "access_token",
                "",
                CookieOptionsHelper.CreateExpiredCookieOptions()
            );

            Response.Cookies.Append(
                "refresh_token",
                "",
                CookieOptionsHelper.CreateExpiredCookieOptions()
            );

            return ApiResponse<object>.CreateSuccess("登出成功");
        }

        /// <summary>
        /// 用户注册接口
        /// </summary>
        /// <param name="user">用户注册信息</param>
        /// <returns>注册结果，包含访问令牌和刷新令牌</returns>
        /// <remarks>
        /// 注册流程：
        /// 1. 验证请求数据格式
        /// 2. 调用认证服务注册新用户
        /// 3. 检查注册是否成功（用户名是否已存在）
        /// 4. 获取客户端IP地址和用户代理信息
        /// 5. 为新注册用户生成JWT令牌
        /// 6. 返回令牌响应
        ///
        /// 注意：注册成功后用户会自动登录，无需再次输入用户名密码
        /// </remarks>
        [HttpPost("register")]
        [AllowAnonymous] // 🔓 允许匿名访问，注册不需要认证
        public async Task<ApiResponse<TokenResponse>> Register([FromBody] User user)
        {
            // 验证请求模型状态
            if (!ModelState.IsValid)
            {
                return ApiResponse<TokenResponse>.Error("请求数据格式无效");
            }

            // 调用认证服务注册新用户
            var registeredUser = await _authService.RegisterAsync(user);

            // 检查注册是否成功
            if (registeredUser == null)
            {
                return ApiResponse<TokenResponse>.Error("用户名已存在");
            }

            // 获取客户端IP地址和用户代理信息（用于安全审计）
            var ipAddress =
                Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var userAgent = Request.Headers["User-Agent"].ToString();

            // 为新注册用户生成JWT访问令牌和刷新令牌
            var tokenResponse = await _authService.GenerateTokensAsync(
                registeredUser,
                ipAddress,
                userAgent
            );

            // 🍪 将令牌存储到 Cookie 中
            Response.Cookies.Append(
                "access_token",
                tokenResponse.AccessToken,
                CookieOptionsHelper.CreateAccessTokenCookieOptions()
            );

            Response.Cookies.Append(
                "refresh_token",
                tokenResponse.RefreshToken,
                CookieOptionsHelper.CreateRefreshTokenCookieOptions()
            );

            return ApiResponse<TokenResponse>.OK(tokenResponse, "注册成功");
        }

        /// <summary>
        /// 修改当前用户密码
        /// </summary>
        [HttpPost("change-password")]
        [Authorize]
        public async Task<ApiResponse<bool>> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            if (!ModelState.IsValid)
            {
                return ApiResponse<bool>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState);
            }

            try
            {
                var userIdClaim = User.FindFirst("userId") ?? User.FindFirst("sub");
                if (userIdClaim == null)
                {
                    return ApiResponse<bool>.Error("无法获取用户信息");
                }

                var success = await _authService.ChangePasswordAsync(userIdClaim.Value, dto);
                if (success)
                {
                    return ApiResponse<bool>.OK(true, "密码修改成功");
                }
                return ApiResponse<bool>.Error("密码修改失败");
            }
            catch (Exception ex)
            {
                return ApiResponse<bool>.Error(ex.Message);
            }
        }
    }
}
