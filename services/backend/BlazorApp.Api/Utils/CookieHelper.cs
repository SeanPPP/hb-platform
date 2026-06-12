using Microsoft.AspNetCore.Http;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// Cookie 辅助类
    /// 🍪 提供统一的 Cookie 读取和清除方法，用于 Cookie 认证方案
    /// </summary>
    public static class CookieHelper
    {
        private const string AccessTokenCookieName = "access_token";
        private const string RefreshTokenCookieName = "refresh_token";

        /// <summary>
        /// 从 Cookie 中读取访问令牌
        /// 🔑 读取存储在 Cookie 中的 accessToken
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>访问令牌字符串，Cookie 不存在返回 null</returns>
        public static string? GetAccessToken(HttpContext context)
        {
            if (context == null)
            {
                return null;
            }

            // 尝试从 Cookie 中读取 accessToken
            return context.Request.Cookies.TryGetValue(AccessTokenCookieName, out var token)
                ? token
                : null;
        }

        /// <summary>
        /// 从 Cookie 中读取刷新令牌
        /// 🔄 读取存储在 Cookie 中的 refreshToken
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>刷新令牌字符串，Cookie 不存在返回 null</returns>
        public static string? GetRefreshToken(HttpContext context)
        {
            if (context == null)
            {
                return null;
            }

            // 尝试从 Cookie 中读取 refreshToken
            return context.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var token)
                ? token
                : null;
        }

        /// <summary>
        /// 同时获取访问令牌和刷新令牌
        /// 📦 一次性获取两个令牌，减少代码重复
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>包含访问令牌和刷新令牌的元组</returns>
        public static (string? AccessToken, string? RefreshToken) GetTokens(HttpContext context)
        {
            if (context == null)
            {
                return (null, null);
            }

            var accessToken = GetAccessToken(context);
            var refreshToken = GetRefreshToken(context);

            return (accessToken, refreshToken);
        }

        /// <summary>
        /// 清除认证相关的 Cookie（用于登出）
        /// 🚫 删除存储在客户端的 accessToken 和 refreshToken Cookie
        /// </summary>
        /// <param name="response">HTTP 响应对象</param>
        public static void ClearAuthCookies(HttpResponse response)
        {
            if (response == null)
            {
                return;
            }

            // 创建已过期的 Cookie 选项
            var expiredOptions = CookieOptionsHelper.CreateExpiredCookieOptions();

            // 删除 accessToken Cookie
            response.Cookies.Delete(AccessTokenCookieName, expiredOptions);

            // 删除 refreshToken Cookie
            response.Cookies.Delete(RefreshTokenCookieName, expiredOptions);
        }

        /// <summary>
        /// 设置访问令牌到 Cookie
        /// 🔑 将访问令牌写入 Cookie
        /// </summary>
        /// <param name="response">HTTP 响应对象</param>
        /// <param name="accessToken">访问令牌</param>
        public static void SetAccessToken(HttpResponse response, string accessToken)
        {
            if (response == null || string.IsNullOrEmpty(accessToken))
            {
                return;
            }

            var options = CookieOptionsHelper.CreateAccessTokenCookieOptions();
            response.Cookies.Append(AccessTokenCookieName, accessToken, options);
        }

        /// <summary>
        /// 设置刷新令牌到 Cookie
        /// 🔄 将刷新令牌写入 Cookie
        /// </summary>
        /// <param name="response">HTTP 响应对象</param>
        /// <param name="refreshToken">刷新令牌</param>
        public static void SetRefreshToken(HttpResponse response, string refreshToken)
        {
            if (response == null || string.IsNullOrEmpty(refreshToken))
            {
                return;
            }

            var options = CookieOptionsHelper.CreateRefreshTokenCookieOptions();
            response.Cookies.Append(RefreshTokenCookieName, refreshToken, options);
        }

        /// <summary>
        /// 同时设置访问令牌和刷新令牌到 Cookie
        /// 📦 一次性设置两个令牌到 Cookie
        /// </summary>
        /// <param name="response">HTTP 响应对象</param>
        /// /// <param name="accessToken">访问令牌</param>
        /// <param name="refreshToken">刷新令牌</param>
        public static void SetTokens(HttpResponse response, string accessToken, string refreshToken)
        {
            SetAccessToken(response, accessToken);
            SetRefreshToken(response, refreshToken);
        }

        /// <summary>
        /// 检查请求中是否包含有效的令牌
        /// 🔍 检查 Cookie 或 Authorization Header 中是否存在令牌
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>true 表示存在至少一个令牌，false 表示都不存在</returns>
        public static bool HasAnyToken(HttpContext context)
        {
            if (context == null)
            {
                return false;
            }

            // 检查 Cookie 中的令牌
            var hasCookieToken = !string.IsNullOrEmpty(GetAccessToken(context))
                || !string.IsNullOrEmpty(GetRefreshToken(context));

            // 检查 Authorization Header 中的令牌
            var hasHeaderToken = !string.IsNullOrEmpty(
                context.Request.Headers["Authorization"].FirstOrDefault()
            );

            return hasCookieToken || hasHeaderToken;
        }
    }
}
