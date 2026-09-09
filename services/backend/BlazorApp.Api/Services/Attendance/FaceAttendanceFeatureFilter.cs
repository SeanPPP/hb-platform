using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace BlazorApp.Api.Services.Attendance;

// 先部署兼容 schema 与私有识别服务，再按门店灰度启用；关闭功能不删除本机队列。
public sealed class FaceAttendanceFeatureFilter(IConfiguration configuration) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store, private";
        var stores = configuration.GetSection("FaceAttendance:StoreCodes").Get<string[]>() ?? [];
        var storeCode = context.HttpContext.Request.Headers["X-HB-Face-Store"].ToString();
        if (!configuration.GetValue("FaceAttendance:Enabled", false)
            || !stores.Contains(storeCode, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new { code = "FACE_ATTENDANCE_DISABLED", errorCode = "FACE_ATTENDANCE_DISABLED" }) { StatusCode = 503 };
            return;
        }
        await next();
    }
}
