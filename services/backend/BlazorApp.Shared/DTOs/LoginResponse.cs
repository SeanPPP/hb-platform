namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 登录响应数据传输对象，用于返回登录结果和用户信息
    /// </summary>
    public class LoginResponse
    {
        /// <summary>
        /// 登录是否成功
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// 访问令牌
        /// </summary>
        public string Token { get; set; } = string.Empty;
        
        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// 登录用户信息
        /// </summary>
        public LoginUserDto? User { get; set; }
    }

    /// <summary>
    /// 登录用户信息数据传输对象，包含登录成功后返回的基本用户信息
    /// </summary>
    public class LoginUserDto
    {
        /// <summary>
        /// 用户唯一标识符
        /// </summary>
        public string UserGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;
        
        /// <summary>
        /// 邮箱地址
        /// </summary>
        public string Email { get; set; } = string.Empty;
        
        /// <summary>
        /// 用户全名
        /// </summary>
        public string? FullName { get; set; }
    }
}
