namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 浏览器 Cookie 会话响应，不返回明文令牌。
    /// </summary>
    public class SessionResponse
    {
        /// <summary>
        /// 访问令牌过期时间。
        /// </summary>
        public DateTime AccessTokenExpiry { get; set; }

        /// <summary>
        /// 刷新令牌过期时间。
        /// </summary>
        public DateTime RefreshTokenExpiry { get; set; }

        /// <summary>
        /// 本次 App 登录是否相对上次登录切换了设备。
        /// </summary>
        public bool IsDeviceSwitched { get; set; }

        /// <summary>
        /// 本次 App 登录设备是否为常用设备。
        /// </summary>
        public bool IsCommonDevice { get; set; }
    }
}
