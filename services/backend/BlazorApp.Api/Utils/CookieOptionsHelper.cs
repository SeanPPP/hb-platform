using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using BlazorApp.Api.Models;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// Cookie 配置辅助类
    /// 🍪 提供统一的 Cookie 配置方法，确保安全性和一致性
    /// </summary>
    public static class CookieOptionsHelper
    {
        private static IConfiguration? _configuration;
        private static CookieSettings? _settings;
        private static IWebHostEnvironment? _environment;

        /// <summary>
        /// 初始化 Cookie 配置（在 Program.cs 中调用）
        /// </summary>
        /// <param name="configuration">应用程序配置</param>
        /// <param name="environment">主机环境（可选，用于开发环境自动禁用 Secure）</param>
        public static void Initialize(IConfiguration configuration, IWebHostEnvironment? environment = null)
        {
            _configuration = configuration;
            _environment = environment;
            _settings = configuration.GetSection("Cookie").Get<CookieSettings>() ?? new CookieSettings();

            if (_environment?.IsDevelopment() == true)
            {
                _settings.Secure = false;
            }
        }

        /// <summary>
        /// 获取 Cookie 配置设置
        /// </summary>
        private static CookieSettings Settings => _settings ?? new CookieSettings();

        /// <summary>
        /// 解析 SameSite 策略字符串为枚举值
        /// </summary>
        private static SameSiteMode ParseSameSiteMode(string sameSite)
        {
            return sameSite.ToLowerInvariant() switch
            {
                "strict" => SameSiteMode.Strict,
                "lax" => SameSiteMode.Lax,
                "none" => SameSiteMode.None,
                _ => SameSiteMode.Strict
            };
        }

        /// <summary>
        /// 创建基础 Cookie 选项
        /// 🔐 配置安全选项：HttpOnly、Secure、SameSite
        /// </summary>
        private static CookieOptions CreateBaseCookieOptions()
        {
            var settings = Settings;

            var options = new CookieOptions
            {
                HttpOnly = true, // 防止 XSS 攻击，JavaScript 无法访问
                Secure = settings.Secure, // 生产环境必须使用 HTTPS
                SameSite = ParseSameSiteMode(settings.SameSite), // 防止 CSRF 攻击
                Path = settings.Path,
            };

            // 如果配置了域名，则设置域名
            if (!string.IsNullOrEmpty(settings.Domain))
            {
                options.Domain = settings.Domain;
            }

            return options;
        }

        /// <summary>
        /// 创建访问令牌 Cookie 选项
        /// 🔑 短期有效的 Cookie（默认 15 分钟）
        /// </summary>
        /// <returns>配置好的 Cookie 选项对象</returns>
        public static CookieOptions CreateAccessTokenCookieOptions()
        {
            var options = CreateBaseCookieOptions();
            var settings = Settings;

            // 设置访问令牌的过期时间
            options.Expires = DateTime.UtcNow.AddMinutes(settings.AccessTokenExpiryMinutes);

            // 或者使用 MaxAge（优先级高于 Expires）
            // options.MaxAge = TimeSpan.FromMinutes(settings.AccessTokenExpiryMinutes);

            return options;
        }

        /// <summary>
        /// 创建刷新令牌 Cookie 选项
        /// 🔄 长期有效的 Cookie（默认 7 天）
        /// </summary>
        /// <returns>配置好的 Cookie 选项对象</returns>
        public static CookieOptions CreateRefreshTokenCookieOptions()
        {
            var options = CreateBaseCookieOptions();
            var settings = Settings;

            // 设置刷新令牌的过期时间
            options.Expires = DateTime.UtcNow.AddDays(settings.RefreshTokenExpiryDays);

            // 或者使用 MaxAge（优先级高于 Expires）
            // options.MaxAge = TimeSpan.FromDays(settings.RefreshTokenExpiryDays);

            return options;
        }

        /// <summary>
        /// 创建已过期的 Cookie 选项
        /// 🚫 用于登出时清除 Cookie
        /// </summary>
        /// <returns>配置好的 Cookie 选项对象</returns>
        public static CookieOptions CreateExpiredCookieOptions()
        {
            var options = CreateBaseCookieOptions();

            // 设置过期时间为过去的某个时间点
            options.Expires = DateTime.UtcNow.AddDays(-1);

            return options;
        }

        /// <summary>
        /// 创建自定义过期时间的 Cookie 选项
        /// ⏰ 根据需要自定义 Cookie 过期时间
        /// </summary>
        /// <param name="expiryMinutes">过期时间（分钟）</param>
        /// <returns>配置好的 Cookie 选项对象</returns>
        public static CookieOptions CreateCustomExpiryCookieOptions(int expiryMinutes)
        {
            var options = CreateBaseCookieOptions();

            // 设置自定义过期时间
            options.Expires = DateTime.UtcNow.AddMinutes(expiryMinutes);

            return options;
        }

        /// <summary>
        /// 创建 Cookie 选项（使用 MaxAge 而不是 Expires）
        /// ⏰ 推荐使用 MaxAge，因为它不受客户端时区影响
        /// </summary>
        /// <param name="maxAge">最大有效期</param>
        /// <returns>配置好的 Cookie 选项对象</returns>
        public static CookieOptions CreateCookieOptionsWithMaxAge(TimeSpan maxAge)
        {
            var options = CreateBaseCookieOptions();

            // 设置 MaxAge（优先级高于 Expires）
            options.MaxAge = maxAge;

            return options;
        }

        /// <summary>
        /// 创建访问令牌 Cookie 选项（使用 MaxAge）
        /// </summary>
        public static CookieOptions CreateAccessTokenCookieOptionsWithMaxAge()
        {
            var settings = Settings;
            return CreateCookieOptionsWithMaxAge(TimeSpan.FromMinutes(settings.AccessTokenExpiryMinutes));
        }

        /// <summary>
        /// 创建刷新令牌 Cookie 选项（使用 MaxAge）
        /// </summary>
        public static CookieOptions CreateRefreshTokenCookieOptionsWithMaxAge()
        {
            var settings = Settings;
            return CreateCookieOptionsWithMaxAge(TimeSpan.FromDays(settings.RefreshTokenExpiryDays));
        }

        /// <summary>
        /// 创建用于 Cookie 认证的选项
        /// 🔐 用于配置 ASP.NET Core 的 Cookie 认证中间件
        /// </summary>
        /// <param name="cookieName">Cookie 名称</param>
        /// <param name="loginPath">登录路径</param>
        /// <param name="accessDeniedPath">拒绝访问路径</param>
        /// <returns>配置好的 Cookie 认证选项</returns>
        public static Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions CreateCookieAuthenticationOptions(
            string cookieName = ".AspNetCore.Cookies",
            string loginPath = "/api/Auth/login",
            string accessDeniedPath = "/api/Auth/access-denied"
        )
        {
            var settings = Settings;

            return new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions
            {
                Cookie = new CookieBuilder
                {
                    Name = cookieName,
                    SameSite = ParseSameSiteMode(settings.SameSite),
                    SecurePolicy = settings.Secure
                        ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
                        : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest,
                    HttpOnly = true,
                    Path = settings.Path,
                    IsEssential = true, // 允许在 GDPR 下使用
                },
                LoginPath = loginPath,
                AccessDeniedPath = accessDeniedPath,
                SlidingExpiration = true, // 滑动过期时间
                ExpireTimeSpan = TimeSpan.FromMinutes(settings.AccessTokenExpiryMinutes),
            };
        }
    }
}
