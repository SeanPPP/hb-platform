using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace BlazorApp.Api.Services.RustDeskCompat;

/// <summary>登录尝试按服务端可信 IP 限流，不排队、不读取或记录密码。</summary>
public static class RustDeskLoginRateLimits
{
    public const string PolicyName = "RustDeskLogin";

    public static void Configure(RateLimiterOptions options)
    {
        options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
            context.RequestServices.GetService<IClientIpResolver>()?.Resolve(context)
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0, AutoReplenishment = true,
            }));

        // 保留之前注册的限流响应，避免影响移动端和浏览器扩展。
        var previousOnRejected = options.OnRejected;
        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != PolicyName)
            {
                if (previousOnRejected is not null) await previousOnRejected(context, cancellationToken);
                return;
            }
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.RetryAfter = "60";
            await context.HttpContext.Response.WriteAsJsonAsync(new { error = "登录尝试过于频繁，请稍后重试" }, cancellationToken);
        };
    }
}
