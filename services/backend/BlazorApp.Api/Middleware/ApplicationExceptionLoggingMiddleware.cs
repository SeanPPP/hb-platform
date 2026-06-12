using BlazorApp.Api.Services.Logging;

namespace BlazorApp.Api.Middleware
{
    public class ApplicationExceptionLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApplicationExceptionLoggingMiddleware> _logger;

        public ApplicationExceptionLoggingMiddleware(
            RequestDelegate next,
            ILogger<ApplicationExceptionLoggingMiddleware> logger
        )
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "请求管道异常 - 路径: {Path}, 方法: {Method}, TraceId: {TraceId}",
                    context.Request.Path,
                    context.Request.Method,
                    context.TraceIdentifier
                );
                throw;
            }
        }
    }
}
