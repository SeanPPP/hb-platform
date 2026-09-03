using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlazorApp.Api.Controllers.React;

/// <summary>
/// 仅用于浏览器扩展热销榜单：将 ApiController 在模型绑定阶段产生的错误统一为扩展约定的响应信封。
/// </summary>
public sealed class BrowserExtensionInvalidRequestFilter : ActionFilterAttribute
{
    // 必须先于 ApiController 自带的 ModelStateInvalidFilter（-2000）运行，避免默认 ProblemDetails 泄漏到扩展协议。
    public BrowserExtensionInvalidRequestFilter()
    {
        Order = -3000;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        context.Result = new BadRequestObjectResult(
            ApiResponse<BrowserExtensionSupplierTopSalesDto>.Error(
                "请求参数无效。",
                "INVALID_REQUEST"
            )
        );
    }
}
