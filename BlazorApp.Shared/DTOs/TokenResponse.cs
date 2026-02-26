namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 令牌响应数据传输对象，用于返回访问令牌和刷新令牌信息
    /// </summary>
    public class TokenResponse
    {
        /// <summary>
        /// 访问令牌
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;
        
        /// <summary>
        /// 刷新令牌
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;
        
        /// <summary>
        /// 访问令牌过期时间
        /// </summary>
        public DateTime AccessTokenExpiry { get; set; }
        
        /// <summary>
        /// 刷新令牌过期时间
        /// </summary>
        public DateTime RefreshTokenExpiry { get; set; }
        
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 刷新令牌请求数据传输对象，用于请求新的访问令牌
    /// </summary>
    public class RefreshTokenRequest
    {
        /// <summary>
        /// 访问令牌
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;
        
        /// <summary>
        /// 刷新令牌
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;
    }
}