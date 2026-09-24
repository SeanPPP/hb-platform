using System.Text.Json;
using BlazorApp.Api.Interfaces.React;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlazorApp.Api.Filters;

/// <summary>
/// 把本次请求产生的分店价格通知汇总写入响应头 X-Price-Notification。
/// 用响应头而不是改响应体：仓库改价入口有十几个、各自返回不同的 DTO，
/// 一个全局过滤器即可覆盖全部，且对不关心该信息的旧客户端完全无感。
/// 内容只有数字字段，天然满足响应头的 ASCII 限制。
/// </summary>
public sealed class PriceNotificationHeaderFilter : IAsyncResultFilter
{
    public const string HeaderName = "X-Price-Notification";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IPriceNotificationSummaryAccessor _summary;

    public PriceNotificationHeaderFilter(IPriceNotificationSummaryAccessor summary)
    {
        _summary = summary;
    }

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // 只有发生过价格评估的请求才会有汇总；全为 0 也要返回，前端据此提示"未产生通知"。
        var summary = _summary.GetSummary();
        if (summary != null && !context.HttpContext.Response.HasStarted)
        {
            context.HttpContext.Response.Headers[HeaderName] = JsonSerializer.Serialize(summary, JsonOptions);
        }
        return next();
    }
}
