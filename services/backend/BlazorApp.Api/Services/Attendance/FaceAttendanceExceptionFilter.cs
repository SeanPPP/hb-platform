using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace BlazorApp.Api.Services.Attendance;

/// <summary>本功能错误不经过会回显堆栈的通用筛选器，禁止在响应中返回照片或模板。</summary>
public sealed class FaceAttendanceExceptionFilter(ILogger<FaceAttendanceExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, code) = context.Exception switch
        {
            FaceAttendanceException error => (error.StatusCode, error.Code),
            JsonException => (400, "EVENT_METADATA_INVALID"),
            BadHttpRequestException => (400, "FACE_REQUEST_INVALID"),
            _ => (500, "FACE_INTERNAL_ERROR")
        };
        if (status >= 500) logger.LogError("人脸考勤请求失败 type={ExceptionType}", context.Exception.GetType().Name);
        context.Result = new ObjectResult(new { code, errorCode = code }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
