using Microsoft.AspNetCore.Http.Features;

namespace BlazorApp.Api.Services.Performance;

/// <summary>
/// 动态排除日志/指标路径和附件响应；可预先识别的流式端点应使用 .NET 内建
/// <c>[DisableHttpMetrics]</c>，由框架在执行端点前关闭请求指标。
/// </summary>
public sealed class PerformanceMetricsEndpointExclusionMiddleware
{
    private readonly RequestDelegate _next;

    public PerformanceMetricsEndpointExclusionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var metricsFeature = context.Features.Get<IHttpMetricsTagsFeature>();
        if (AspNetCoreRequestMetricListener.ShouldDisablePath(context.Request.Path))
        {
            if (metricsFeature != null)
            {
                metricsFeature.MetricsDisabled = true;
            }
            await _next(context);
            return;
        }

        await _next(context);
        if (metricsFeature != null && IsAttachment(context.Response))
        {
            // 下载响应通常包含文件生成/传输等待，不能与普通 API 延迟混为一组。
            metricsFeature.MetricsDisabled = true;
        }
    }

    internal static bool IsAttachment(HttpResponse response)
    {
        var disposition = response.Headers.ContentDisposition.ToString();
        return disposition.Contains("attachment", StringComparison.OrdinalIgnoreCase);
    }
}
