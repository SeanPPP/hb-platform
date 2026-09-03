namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 已登录网站为浏览器扩展申请一次性授权码的请求。
/// </summary>
public sealed class BrowserExtensionAuthorizeRequest
{
    public string CodeChallenge { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
}

/// <summary>
/// 一次性授权码；授权码只能兑换一次且不会携带网站 Cookie 或刷新令牌。
/// </summary>
public sealed class BrowserExtensionAuthorizeResponse
{
    public string Code { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
/// 浏览器扩展用 PKCE verifier 兑换短期访问令牌的请求。
/// </summary>
public sealed class BrowserExtensionTokenRequest
{
    public string Code { get; set; } = string.Empty;

    public string CodeVerifier { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
}

/// <summary>
/// 仅供浏览器扩展当前会话使用的短期访问令牌，不包含刷新令牌。
/// </summary>
public sealed class BrowserExtensionTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiry { get; set; }

    public string UserGuid { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
}
