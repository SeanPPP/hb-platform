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
    }
}
