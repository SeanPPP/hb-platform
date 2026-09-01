using System.Threading.RateLimiting;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.RateLimiting;

namespace BlazorApp.Api.Services;

/// <summary>
/// 浏览器扩展会话交接端点的独立限流策略，避免匿名随机授权码查询持续占用数据库。
/// </summary>
public static class BrowserExtensionSessionGrantRateLimits
{
    public const string AuthorizePolicyName = "browser-extension-authorize";
    public const string ExchangePolicyName = "browser-extension-token";
    public const string RateLimitedErrorCode = "EXTENSION_RATE_LIMITED";

    internal const int AuthorizePermitLimit = 12;
    internal const int ExchangePermitLimit = 120;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static void Configure(RateLimiterOptions options)
    {
        options.AddPolicy(
            AuthorizePolicyName,
            context => RateLimitPartition.GetFixedWindowLimiter(
                ResolveAuthorizePartitionKey(context),
                _ => CreateOptions(AuthorizePermitLimit)
            )
        );
        options.AddPolicy(
            ExchangePolicyName,
            context => RateLimitPartition.GetFixedWindowLimiter(
                ResolveExchangePartitionKey(context),
                _ => CreateOptions(ExchangePermitLimit)
            )
        );
        options.OnRejected = async (context, cancellationToken) =>
        {
            var policyName = context
                .HttpContext.GetEndpoint()
                ?.Metadata.GetMetadata<EnableRateLimitingAttribute>()
                ?.PolicyName;
            var isBrowserExtensionPolicy =
                string.Equals(policyName, AuthorizePolicyName, StringComparison.Ordinal)
                || string.Equals(policyName, ExchangePolicyName, StringComparison.Ordinal);
            if (!isBrowserExtensionPolicy || context.HttpContext.Response.HasStarted)
            {
                return;
            }

            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsJsonAsync(
                ApiResponse<object>.Error(
                    "浏览器扩展授权请求过于频繁，请稍后重试",
                    RateLimitedErrorCode
                ),
                cancellationToken
            );
        };
    }

    internal static string ResolveAuthorizePartitionKey(HttpContext context)
    {
        var sessionId = context.User.FindFirst("sessionId")?.Value;
        return !string.IsNullOrWhiteSpace(sessionId)
            ? $"session:{sessionId}"
            : ResolveExchangePartitionKey(context);
    }

    internal static string ResolveExchangePartitionKey(HttpContext context)
    {
        var resolver = context.RequestServices.GetService<IClientIpResolver>();
        var clientIp = resolver?.Resolve(context);
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
        return $"ip:{clientIp}";
    }

    private static FixedWindowRateLimiterOptions CreateOptions(int permitLimit) => new()
    {
        AutoReplenishment = true,
        PermitLimit = permitLimit,
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        Window = Window,
    };
}
