namespace BlazorApp.Api.Models
{
    /// <summary>
    /// Cookie 配置设置
    /// 🍪 定义 Cookie 认证方案的安全配置选项
    /// </summary>
    public class CookieSettings
    {
        /// <summary>
        /// 是否启用安全标志（HTTPS only）
        /// 生产环境应设置为 true，开发环境可设置为 false
        /// </summary>
        public bool Secure { get; set; } = true;

        /// <summary>
        /// SameSite 策略
        /// Strict: 严格模式，防止 CSRF 攻击
        /// Lax: 宽松模式，允许跨站导航时发送 Cookie
        /// None: 允许跨站请求（必须配合 Secure=true）
        /// </summary>
        public string SameSite { get; set; } = "Strict";

        /// <summary>
        /// Cookie 域名（可选）
        /// 留空则自动设置为当前域名
        /// </summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>
        /// Cookie 路径
        /// 默认为 "/"，表示整个站点都可访问
        /// </summary>
        public string Path { get; set; } = "/";

        /// <summary>
        /// 访问令牌过期时间（分钟）
        /// 默认 15 分钟
        /// </summary>
        public int AccessTokenExpiryMinutes { get; set; } = 15;

        /// <summary>
        /// 刷新令牌过期时间（天）
        /// 默认 7 天
        /// </summary>
        public int RefreshTokenExpiryDays { get; set; } = 7;
    }
}
