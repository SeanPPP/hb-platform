namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 登录请求数据传输对象，用于用户登录时传输用户名和密码
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;
        
        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 密码格式：raw 表示 HTTPS 传输的原始密码；clientSha256 表示旧客户端传来的 SHA256。
        /// </summary>
        public string PasswordFormat { get; set; } = string.Empty;
    }
}
