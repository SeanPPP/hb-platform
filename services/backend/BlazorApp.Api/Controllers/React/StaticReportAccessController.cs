using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

/// <summary>
/// 静态报告访问鉴权，供 nginx auth_request 子请求调用。
/// /reports/&lt;名称&gt;/ 下的报告页由 nginx 直接提供静态文件，每个请求先问这里：
/// 只回答当前登录用户能否打开，不返回任何业务数据。
/// 204 = 放行；401 = 未登录或令牌过期（nginx 转到续期中转页）；403 = 无权限。
/// </summary>
[ApiController]
[Route("api/react/v1/static-report-access")]
public sealed class StaticReportAccessController(
    IRoleService roleService,
    ILogger<StaticReportAccessController> logger
) : ControllerBase
{
    /// <summary>
    /// KFC（Uncle Bills，本地供应商 257）补货信号报告。
    /// 报告含全部门店的销量、余量与调拨建议，所以除「本地商品分析」查看权限外，
    /// 还要求能看全部门店：与 LocalSupplierProductSalesAnalysisController 的全店判定一致，
    /// 只认超级管理员与仓库管理员角色，且以实时角色快照为准，不信任 JWT 里可能过期的角色声明。
    /// </summary>
    [HttpGet("kfc-uncle-bills")]
    [Authorize(Policy = Permissions.SalesDashboard.LocalProductAnalysisView)]
    public async Task<IActionResult> KfcUncleBills()
    {
        // nginx 与浏览器都不能缓存鉴权结果，否则撤销权限后仍可访问
        Response.Headers.CacheControl = "no-store";

        var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var snapshot = await roleService.GetUserPermissionSnapshotAsync(userGuid);
            if (snapshot?.Success != true || snapshot.Data == null)
            {
                // 读不到实时角色时拒绝，不退化为信任 JWT 声明
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            return HasAllStoreAccess(snapshot.Data.RoleNames)
                ? NoContent()
                : StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (Exception ex)
        {
            // auth_request 把 401/403 以外的状态都当 500；这里按拒绝处理并记录
            logger.LogError(ex, "静态报告鉴权读取角色快照失败 UserGuid={UserGuid}", userGuid);
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    internal static bool HasAllStoreAccess(IEnumerable<string>? roleNames) =>
        (roleNames ?? Array.Empty<string>()).Any(role =>
            Permissions.SuperAdminRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase)
            || Permissions.WarehouseManagerRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase));
}
