using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Middleware;

/// <summary>
/// 将 browser_extension 用途的短期 JWT 严格限定在订货助手 API。
/// </summary>
public sealed class BrowserExtensionTokenScopeMiddleware
{
    public const string ScopeDeniedErrorCode = "EXTENSION_TOKEN_SCOPE_DENIED";
    public const string ExtensionTokenExpiredErrorCode = "EXTENSION_TOKEN_EXPIRED";

    private static readonly PathString ExtensionApiPath =
        new("/api/react/v1/browser-extension");
    private readonly RequestDelegate _next;
    private readonly TimeProvider _timeProvider;

    public BrowserExtensionTokenScopeMiddleware(
        RequestDelegate next,
        TimeProvider? timeProvider = null
    )
    {
        _next = next;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.User.HasClaim("token_use", "browser_extension"))
        {
            await _next(context);
            return;
        }

        // JWT bearer 默认允许约五分钟 ClockSkew；扩展令牌必须按自身 exp 严格结束五分钟寿命。
        var expirationClaim = context.User.FindFirst("exp")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.Expiration)?.Value;
        if (
            !long.TryParse(expirationClaim, out var expirationUnixSeconds)
            || expirationUnixSeconds <= _timeProvider.GetUtcNow().ToUnixTimeSeconds()
        )
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Error(
                    "浏览器扩展令牌已过期",
                    ExtensionTokenExpiredErrorCode
                ),
                context.RequestAborted
            );
            return;
        }

        var extensionApi = context.Request.Path.StartsWithSegments(
            ExtensionApiPath,
            StringComparison.OrdinalIgnoreCase
        );
        if (extensionApi)
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            ApiResponse<object>.Error(
                "浏览器扩展令牌无权访问该接口",
                ScopeDeniedErrorCode
            ),
            context.RequestAborted
        );
    }
}
