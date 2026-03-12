using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Filters
{
    public class ApiExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<ApiExceptionFilter> _logger;

        public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
        {
            _logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            var (statusCode, errorCode, message) = GetExceptionDetails(context.Exception);

            _logger.LogError(
                context.Exception,
                "API异常 - 路径: {Path}, 方法: {Method}, 状态码: {StatusCode}, 错误: {Message}",
                context.HttpContext.Request.Path,
                context.HttpContext.Request.Method,
                statusCode,
                context.Exception.Message
            );

            var response = ApiResponse<object>.Error(message, errorCode, context.Exception.StackTrace);

            context.Result = new JsonResult(response)
            {
                StatusCode = statusCode,
                ContentType = "application/json"
            };

            context.ExceptionHandled = true;
        }

        private static (int statusCode, string errorCode, string message) GetExceptionDetails(Exception exception)
        {
            return exception switch
            {
                ArgumentException argEx => (400, "ARGUMENT_ERROR", argEx.Message),
                UnauthorizedAccessException => (401, "UNAUTHORIZED", "未经授权的访问"),
                KeyNotFoundException => (404, "NOT_FOUND", "请求的资源不存在"),
                InvalidOperationException invalidOpEx => (400, "INVALID_OPERATION", invalidOpEx.Message),
                TimeoutException => (408, "TIMEOUT", "请求超时"),
                NotImplementedException => (501, "NOT_IMPLEMENTED", "功能未实现"),
                _ => (500, "INTERNAL_ERROR", "服务器内部错误")
            };
        }
    }
}
