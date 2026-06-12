using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 微信登录请求DTO
    /// </summary>
    public class WeChatLoginRequest
    {
        /// <summary>
        /// 微信授权码 - 前端通过微信API获取
        /// </summary>
        [Required(ErrorMessage = "微信授权码不能为空")]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 微信授权状态参数（可选）
        /// </summary>
        public string? State { get; set; }
    }

    /// <summary>
    /// 微信绑定请求DTO
    /// </summary>
    public class WeChatBindRequest
    {
        /// <summary>
        /// 微信授权码
        /// </summary>
        [Required(ErrorMessage = "微信授权码不能为空")]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 要绑定的用户GUID（当前登录用户）
        /// </summary>
        [Required(ErrorMessage = "用户GUID不能为空")]
        public string UserGUID { get; set; } = string.Empty;
    }

    /// <summary>
    /// 微信解绑请求DTO
    /// </summary>
    public class WeChatUnbindRequest
    {
        /// <summary>
        /// 用户GUID
        /// </summary>
        [Required(ErrorMessage = "用户GUID不能为空")]
        public string UserGUID { get; set; } = string.Empty;
    }

    /// <summary>
    /// 微信用户信息DTO
    /// </summary>
    public class WeChatUserInfo
    {
        /// <summary>
        /// 微信OpenId
        /// </summary>
        public string OpenId { get; set; } = string.Empty;

        /// <summary>
        /// 微信UnionId（可选）
        /// </summary>
        public string? UnionId { get; set; }

        /// <summary>
        /// 微信昵称
        /// </summary>
        public string? Nickname { get; set; }

        /// <summary>
        /// 头像URL
        /// </summary>
        public string? HeadImgUrl { get; set; }

        /// <summary>
        /// 性别（1为男性，2为女性，0为未知）
        /// </summary>
        public int Sex { get; set; }

        /// <summary>
        /// 城市
        /// </summary>
        public string? City { get; set; }

        /// <summary>
        /// 省份
        /// </summary>
        public string? Province { get; set; }

        /// <summary>
        /// 国家
        /// </summary>
        public string? Country { get; set; }
    }

    /// <summary>
    /// 微信访问令牌响应DTO
    /// </summary>
    public class WeChatAccessTokenResponse
    {
        /// <summary>
        /// 访问令牌
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// 访问令牌过期时间（秒）
        /// </summary>
        public int ExpiresIn { get; set; }

        /// <summary>
        /// 刷新令牌
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// 微信OpenId
        /// </summary>
        public string OpenId { get; set; } = string.Empty;

        /// <summary>
        /// 授权作用域
        /// </summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>
        /// 微信UnionId（仅在用户将公众号绑定到微信开放平台帐号后才会出现）
        /// </summary>
        public string? UnionId { get; set; }
    }

    /// <summary>
    /// 微信登录响应DTO
    /// </summary>
    public class WeChatLoginResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 访问令牌（登录成功时返回）
        /// </summary>
        public string? AccessToken { get; set; }

        /// <summary>
        /// 刷新令牌（登录成功时返回）
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// 用户信息（登录成功时返回）
        /// </summary>
        /// <remarks>
        /// 返回完整的用户信息，类型为 UserDto
        /// </remarks>
        public object? User { get; set; }

        /// <summary>
        /// 是否为新用户（首次微信登录创建的账号）
        /// </summary>
        public bool IsNewUser { get; set; }
    }

    /// <summary>
    /// 微信绑定响应DTO
    /// </summary>
    public class WeChatBindResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 绑定时间
        /// </summary>
        public DateTime? BindTime { get; set; }

        /// <summary>
        /// 微信用户信息
        /// </summary>
        public WeChatUserInfo? WeChatUserInfo { get; set; }
    }

    /// <summary>
    /// 微信配置信息DTO
    /// </summary>
    public class WeChatConfigDto
    {
        /// <summary>
        /// 微信应用ID
        /// </summary>
        public string AppId { get; set; } = string.Empty;

        /// <summary>
        /// 授权回调地址
        /// </summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// 应用授权作用域
        /// </summary>
        public string Scope { get; set; } = "snsapi_login";

        /// <summary>
        /// 状态参数
        /// </summary>
        public string? State { get; set; }
    }

    /// <summary>
    /// 用户微信绑定状态DTO
    /// </summary>
    public class UserWeChatBindingDto
    {
        /// <summary>
        /// 用户GUID
        /// </summary>
        public string UserGUID { get; set; } = string.Empty;

        /// <summary>
        /// 是否已绑定微信
        /// </summary>
        public bool IsBound { get; set; }

        /// <summary>
        /// 微信绑定时间
        /// </summary>
        public DateTime? BindTime { get; set; }

        /// <summary>
        /// 微信昵称（如果已绑定）
        /// </summary>
        public string? WeChatNickname { get; set; }

        /// <summary>
        /// 微信头像URL（如果已绑定）
        /// </summary>
        public string? WeChatHeadImgUrl { get; set; }
    }
}
